using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Web.Models.DbImport;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 19.22 — Excel → SQLite full-mirror DB import (admin-only). Two-step flow:
//   1) POST Preview: parse + dry-run diff (no writes); the file is stashed on disk under
//      a GUID so step 2 can reuse it without re-uploading.
//   2) POST Apply: admin re-enters their password (mirroring the Survey reset gate), the
//      stashed file is replayed under a single transaction with an automatic backup first.
[Authorize(Roles = "Admin")]
[Route("Admin/DbImport")]
public class AdminDbImportController : Controller
{
    private readonly DbImportService _import;
    private readonly DbExportService _export;  // Phase 19.26
    private readonly UserManager<AppUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AdminDbImportController> _log;

    public AdminDbImportController(
        DbImportService import,
        DbExportService export,
        UserManager<AppUser> userManager,
        IWebHostEnvironment env,
        ILogger<AdminDbImportController> log)
    {
        _import = import;
        _export = export;
        _userManager = userManager;
        _env = env;
        _log = log;
    }

    // ---------- Phase 19.26 — DB export endpoints ----------
    // Both endpoints stream the full live database in a read-only fashion.
    //   /Admin/DbImport/Export.xlsx  — mirror of DbImport format (round-trip ready)
    //   /Admin/DbImport/Export.db    — byte-perfect SQLite snapshot via VACUUM INTO
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string SqliteMime = "application/vnd.sqlite3";

    [HttpGet("Export.xlsx")]
    public async Task<IActionResult> ExportXlsx(CancellationToken ct)
    {
        var bytes = await _export.ExportXlsxAsync(ct);
        var name = $"StrategyHouse_DB_Export_{DateTime.UtcNow:yyyy-MM-dd_HHmm}.xlsx";
        return File(bytes, XlsxMime, name);
    }

    [HttpGet("Export.db")]
    public async Task<IActionResult> ExportSqlite(CancellationToken ct)
    {
        var bytes = await _export.ExportSqliteAsync(ct);
        var name = $"StrategyHouse_DB_Export_{DateTime.UtcNow:yyyy-MM-dd_HHmm}.db";
        return File(bytes, SqliteMime, name);
    }

    private string PendingDir => Path.Combine(_env.ContentRootPath, "App_Data", "import-pending");
    private string PendingPath(Guid id) => Path.Combine(PendingDir, id.ToString("N") + ".xlsx");

    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpPost("Preview")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Preview(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "يرجى اختيار ملف Excel (.xlsx).";
            return RedirectToAction(nameof(Index));
        }
        var ext = Path.GetExtension(file.FileName);
        if (!string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "صيغة الملف غير مدعومة. يرجى رفع ملف .xlsx فقط.";
            return RedirectToAction(nameof(Index));
        }

        // Stash the upload so Apply can reuse it after the password step.
        Directory.CreateDirectory(PendingDir);
        PrunePending();
        var id = Guid.NewGuid();
        var path = PendingPath(id);
        await using (var fs = System.IO.File.Create(path))
        {
            await file.CopyToAsync(fs);
        }

        DbImportPreview preview;
        try
        {
            await using var read = System.IO.File.OpenRead(path);
            preview = await _import.AnalyzeAsync(read);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DB import preview failed.");
            TryDelete(path);
            TempData["Error"] = "تعذّرت قراءة الملف. تأكد من أنه ملف Excel صحيح بصيغة التصدير.";
            return RedirectToAction(nameof(Index));
        }

        preview.PendingId = id;
        preview.FileName = Path.GetFileName(file.FileName);
        return View("Preview", preview);
    }

    [HttpPost("Apply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(Guid pendingId, string adminPassword)
    {
        var path = PendingPath(pendingId);
        if (pendingId == Guid.Empty || !System.IO.File.Exists(path))
        {
            TempData["Error"] = "انتهت صلاحية ملف المعاينة. يرجى رفع الملف من جديد.";
            return RedirectToAction(nameof(Index));
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            TempData["Error"] = "الجلسة غير صالحة. يرجى تسجيل الدخول من جديد.";
            return RedirectToAction(nameof(Index));
        }
        var ok = await _userManager.CheckPasswordAsync(user, adminPassword ?? string.Empty);
        if (!ok)
        {
            TempData["Error"] = "كلمة المرور غير صحيحة. لم يتم تطبيق أي تغييرات.";
            // Re-show the preview so the admin can retry the password without re-uploading.
            try
            {
                await using var read = System.IO.File.OpenRead(path);
                var pv = await _import.AnalyzeAsync(read);
                pv.PendingId = pendingId;
                pv.FileName = Path.GetFileName(path);
                return View("Preview", pv);
            }
            catch
            {
                return RedirectToAction(nameof(Index));
            }
        }

        DbImportResult result;
        try
        {
            await using var read = System.IO.File.OpenRead(path);
            result = await _import.ApplyAsync(read);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DB import apply threw.");
            result = new DbImportResult { Success = false, Error = ex.Message };
        }
        finally
        {
            TryDelete(path);
        }

        if (result.Success)
        {
            TempData["Success"] =
                $"تم استيراد القاعدة بنجاح. أُنشئت نسخة احتياطية: {result.BackupFileName} — " +
                $"إضافة {result.TotalInserts}، تحديث {result.TotalUpdates}، حذف {result.TotalDeletes}.";
        }
        else
        {
            TempData["Error"] = $"فشل الاستيراد — أُعيد التراجع. الخطأ: {result.Error}";
        }
        return RedirectToAction(nameof(Index));
    }

    // Delete stale pending uploads (older than 2 hours) so the folder doesn't grow.
    private void PrunePending()
    {
        try
        {
            foreach (var f in Directory.GetFiles(PendingDir, "*.xlsx"))
            {
                if (System.IO.File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddHours(-2))
                    TryDelete(f);
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not prune pending import files."); }
    }

    private void TryDelete(string path)
    {
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        catch (Exception ex) { _log.LogWarning(ex, "Could not delete pending import file {Path}", path); }
    }
}
