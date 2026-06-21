using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 20 — selective stage reset for a single department. Admin-only. Every run
// writes one JourneyAuditLog row.
[Authorize(Roles = "Admin")]
[Route("Admin/JourneyReset")]
public class AdminJourneyResetController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly JourneyResetService _reset;
    private readonly AuditLogService _audit;

    public AdminJourneyResetController(ApplicationDbContext db, JourneyResetService reset, AuditLogService audit)
    {
        _db = db;
        _reset = reset;
        _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var depts = await _db.Departments.OrderBy(d => d.DeptCode)
            .Select(d => new { d.DeptCode, d.NameAr })
            .ToListAsync();
        var vm = new JourneyResetViewModel
        {
            Departments = depts.Select(d => (d.DeptCode, d.NameAr ?? d.DeptCode)).ToList(),
        };
        return View(vm);
    }

    [HttpPost("Execute"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Execute(string deptCode, int[]? stages, bool allStages = false)
    {
        if (string.IsNullOrWhiteSpace(deptCode))
        {
            TempData["Error"] = "يجب اختيار إدارة.";
            return RedirectToAction(nameof(Index));
        }

        var selected = allStages ? new[] { 1, 2, 3, 4, 5 } : (stages ?? Array.Empty<int>());
        if (selected.Length == 0)
        {
            TempData["Error"] = "يجب اختيار مرحلة واحدة على الأقل.";
            return RedirectToAction(nameof(Index));
        }

        var sessionIds = await _reset.ResetDepartmentAsync(deptCode, selected, deleteSessions: allStages);

        _db.JourneyStageResets.Add(new Domain.Entities.JourneyStageReset
        {
            DeptCode = deptCode,
            SessionId = sessionIds.FirstOrDefault() == Guid.Empty ? null : sessionIds.FirstOrDefault(),
            StagesResetCsv = string.Join(",", selected),
            ResetBy = User.Identity?.Name ?? "system",
            ResetAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        await _audit.LogAsync(User.Identity?.Name ?? "system", "JOURNEY_RESET", "Department", deptCode,
            new { stages = selected, allStages, sessions = sessionIds });

        TempData["Success"] = $"تمت إعادة ضبط الإدارة {deptCode} للمراحل: {string.Join("، ", selected)}.";
        return RedirectToAction(nameof(Index));
    }
}

public class JourneyResetViewModel
{
    public List<(string Code, string Name)> Departments { get; set; } = new();
}
