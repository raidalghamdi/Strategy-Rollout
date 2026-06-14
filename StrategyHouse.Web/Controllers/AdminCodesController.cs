using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

[Authorize(Roles = "Admin")]
[Route("Admin/Codes")]
public class AdminCodesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly AccessCodeService _codes;
    private readonly QrService _qr;

    public AdminCodesController(ApplicationDbContext db, AccessCodeService codes, QrService qr)
    {
        _db = db;
        _codes = codes;
        _qr = qr;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Departments = await _db.Departments.OrderBy(d => d.DeptCode).ToListAsync();
        var codes = await _db.DepartmentAccessCodes.OrderByDescending(c => c.CreatedAt).ToListAsync();
        return View(codes);
    }

    [HttpPost("Generate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(string deptCode)
    {
        if (string.IsNullOrWhiteSpace(deptCode))
        {
            TempData["Error"] = "اختر الإدارة.";
            return RedirectToAction(nameof(Index));
        }
        var created = await _codes.CreateForDepartmentAsync(deptCode, User.Identity?.Name);
        TempData["Saved"] = $"تم إنشاء الرمز {created.Code} للإدارة {deptCode}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Revoke/{code}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(string code)
    {
        var entity = await _db.DepartmentAccessCodes.FindAsync(code);
        if (entity != null)
        {
            entity.IsActive = false;
            entity.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    // A4 print-friendly sheet: dept name + big code + QR to /Journey?code=XXXXXX.
    [HttpGet("Sheet/{deptCode}")]
    public async Task<IActionResult> Sheet(string deptCode)
    {
        var dept = await _db.Departments.FindAsync(deptCode);
        if (dept == null) return NotFound();
        var code = await _db.DepartmentAccessCodes
            .Where(c => c.DeptCode == deptCode && c.IsActive)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();
        if (code == null)
        {
            TempData["Error"] = "لا يوجد رمز مفعّل لهذه الإدارة. أنشئ رمزاً أولاً.";
            return RedirectToAction(nameof(Index));
        }
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var url = $"{baseUrl}/Journey?code={code.Code}";
        ViewBag.Dept = dept;
        ViewBag.Code = code.Code;
        ViewBag.Qr = _qr.GenerateBase64Png(url, 10);
        ViewBag.Url = url;
        return View();
    }
}
