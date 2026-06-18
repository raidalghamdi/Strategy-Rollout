using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Controllers;

// Phase 19.21 (Fix 5) — full CRUD management for journey access codes (e.g. GAC202,
// GAC206, GAC208). These map a human-friendly code to a department so a team can
// enter the strategy journey at /Journey?code=XXXX. Codes are stored in the existing
// DepartmentAccessCodes table (no new table / migration); the journey lookup in
// JourneyController.Start already reads from it, so codes created here work
// immediately. Department display names come from Departments.NameAr.
[Authorize(Roles = "Admin")]
[Route("Admin/JourneyCodes")]
public class AdminJourneyCodesController : Controller
{
    private readonly ApplicationDbContext _db;

    public AdminJourneyCodesController(ApplicationDbContext db) => _db = db;

    public class JourneyCodeRow
    {
        public string Code { get; set; } = "";
        public string DeptCode { get; set; } = "";
        public string DeptName { get; set; } = "";
        public bool IsActive { get; set; }
        public int UsedCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private async Task<List<(string DeptCode, string DeptName)>> DeptsAsync()
        => (await _db.Departments.OrderBy(d => d.DeptCode).ToListAsync())
            .Select(d => (d.DeptCode, d.NameAr ?? d.DeptCode)).ToList();

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var depts = await DeptsAsync();
        var deptMap = depts.ToDictionary(d => d.DeptCode, d => d.DeptName);
        var codes = await _db.DepartmentAccessCodes.OrderByDescending(c => c.CreatedAt).ToListAsync();
        ViewBag.Departments = depts;
        return View(codes.Select(c => new JourneyCodeRow
        {
            Code = c.Code,
            DeptCode = c.DeptCode,
            DeptName = deptMap.TryGetValue(c.DeptCode, out var n) ? n : c.DeptCode,
            IsActive = c.IsActive,
            UsedCount = c.UsedCount,
            CreatedAt = c.CreatedAt,
        }).ToList());
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string code, string deptCode)
    {
        code = (code ?? string.Empty).Trim();
        deptCode = (deptCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(deptCode))
        {
            TempData["Error"] = "أدخل الرمز واختر الإدارة.";
            return RedirectToAction(nameof(Index));
        }
        if (code.Length > 15)
        {
            TempData["Error"] = "الرمز يجب ألا يتجاوز 15 حرفاً.";
            return RedirectToAction(nameof(Index));
        }
        if (!await _db.Departments.AnyAsync(d => d.DeptCode == deptCode))
        {
            TempData["Error"] = "الإدارة المختارة غير موجودة.";
            return RedirectToAction(nameof(Index));
        }
        // Code uniqueness (case-insensitive) — Code is the PK so a duplicate would throw.
        if (await _db.DepartmentAccessCodes.AnyAsync(c => c.Code == code))
        {
            TempData["Error"] = $"الرمز «{code}» مستخدم بالفعل. اختر رمزاً آخر.";
            return RedirectToAction(nameof(Index));
        }
        _db.DepartmentAccessCodes.Add(new DepartmentAccessCode
        {
            Code = code,
            DeptCode = deptCode,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = User.Identity?.Name,
        });
        await _db.SaveChangesAsync();
        TempData["Saved"] = $"تم إنشاء الرمز «{code}» للإدارة المحددة.";
        return RedirectToAction(nameof(Index));
    }

    // Edit reassigns the department and/or active state. The code (PK) is immutable;
    // to change a code, delete and recreate it.
    [HttpPost("Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string code, string deptCode, bool isActive)
    {
        var entity = await _db.DepartmentAccessCodes.FindAsync(code);
        if (entity == null)
        {
            TempData["Error"] = "الرمز غير موجود.";
            return RedirectToAction(nameof(Index));
        }
        deptCode = (deptCode ?? string.Empty).Trim();
        if (!await _db.Departments.AnyAsync(d => d.DeptCode == deptCode))
        {
            TempData["Error"] = "الإدارة المختارة غير موجودة.";
            return RedirectToAction(nameof(Index));
        }
        entity.DeptCode = deptCode;
        entity.IsActive = isActive;
        entity.RevokedAt = isActive ? null : (entity.RevokedAt ?? DateTime.UtcNow);
        await _db.SaveChangesAsync();
        TempData["Saved"] = $"تم تحديث الرمز «{code}».";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string code)
    {
        var entity = await _db.DepartmentAccessCodes.FindAsync(code);
        if (entity != null)
        {
            _db.DepartmentAccessCodes.Remove(entity);
            await _db.SaveChangesAsync();
            TempData["Saved"] = $"تم حذف الرمز «{code}».";
        }
        return RedirectToAction(nameof(Index));
    }
}
