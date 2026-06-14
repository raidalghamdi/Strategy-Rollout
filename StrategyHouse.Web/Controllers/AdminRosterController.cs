using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Controllers;

// Phase 6 — admin CRUD for the predefined department roster.
[Authorize(Roles = "Admin")]
[Route("Admin/Roster")]
public class AdminRosterController : Controller
{
    private readonly ApplicationDbContext _db;

    public AdminRosterController(ApplicationDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? dept)
    {
        var depts = await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.DeptCode).ToListAsync();
        ViewBag.Departments = depts;
        ViewBag.SelectedDept = dept;

        var query = _db.DepartmentRoster.AsQueryable();
        if (!string.IsNullOrWhiteSpace(dept)) query = query.Where(r => r.DeptCode == dept);
        var roster = await query.OrderBy(r => r.DeptCode).ThenBy(r => r.NameAr).ToListAsync();
        return View(roster);
    }

    [HttpPost("Add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(string deptCode, string nameAr, string? role, bool isDefaultAttending = true)
    {
        if (string.IsNullOrWhiteSpace(deptCode) || string.IsNullOrWhiteSpace(nameAr))
        {
            TempData["Error"] = "الإدارة والاسم مطلوبان.";
            return RedirectToAction(nameof(Index), new { dept = deptCode });
        }
        _db.DepartmentRoster.Add(new DepartmentRoster
        {
            DeptCode = deptCode.Trim(),
            NameAr = nameAr.Trim(),
            Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim(),
            IsDefaultAttending = isDefaultAttending,
            IsActive = true,
        });
        await _db.SaveChangesAsync();
        TempData["Saved"] = "تمت إضافة العضو.";
        return RedirectToAction(nameof(Index), new { dept = deptCode });
    }

    [HttpPost("Update/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, string nameAr, string? role, bool isDefaultAttending = false, bool isActive = false)
    {
        var member = await _db.DepartmentRoster.FindAsync(id);
        if (member == null) return NotFound();
        if (!string.IsNullOrWhiteSpace(nameAr)) member.NameAr = nameAr.Trim();
        member.Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim();
        member.IsDefaultAttending = isDefaultAttending;
        member.IsActive = isActive;
        await _db.SaveChangesAsync();
        TempData["Saved"] = "تم تحديث العضو.";
        return RedirectToAction(nameof(Index), new { dept = member.DeptCode });
    }

    [HttpPost("Delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var member = await _db.DepartmentRoster.FindAsync(id);
        if (member == null) return NotFound();
        var dept = member.DeptCode;
        _db.DepartmentRoster.Remove(member);
        await _db.SaveChangesAsync();
        TempData["Saved"] = "تم حذف العضو.";
        return RedirectToAction(nameof(Index), new { dept });
    }
}
