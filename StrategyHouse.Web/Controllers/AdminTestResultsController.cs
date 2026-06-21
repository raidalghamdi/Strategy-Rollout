using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 20 — delete sessions created by the `testing` user. Admin-only. Every action
// is audited. Sessions are matched by OwnerUserId = testing user's id.
[Authorize(Roles = "Admin")]
[Route("Admin/TestResults")]
public class AdminTestResultsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<AppUser> _users;
    private readonly JourneyResetService _reset;
    private readonly AuditLogService _audit;

    public AdminTestResultsController(ApplicationDbContext db, UserManager<AppUser> users,
        JourneyResetService reset, AuditLogService audit)
    {
        _db = db;
        _users = users;
        _reset = reset;
        _audit = audit;
    }

    private async Task<int?> TestingUserIdAsync()
    {
        var u = await _users.FindByNameAsync("testing");
        return u?.Id;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var testId = await TestingUserIdAsync();
        var sessions = testId == null
            ? new List<StrategySession>()
            : await _db.StrategySessions.Where(s => s.OwnerUserId == testId)
                .OrderByDescending(s => s.StartedAt).ToListAsync();

        var deptNames = await _db.Departments.ToDictionaryAsync(d => d.DeptCode, d => d.NameAr ?? d.DeptCode);

        var rows = sessions.Select(s => new TestResultRow
        {
            Id = s.Id,
            DeptCode = s.DeptCode,
            DeptName = deptNames.TryGetValue(s.DeptCode, out var n) ? n : s.DeptCode,
            Status = s.Status,
            CurrentStage = s.CurrentStage,
            StartedAt = s.StartedAt,
            CompletedAt = s.CompletedAt,
        }).ToList();

        return View(new TestResultsViewModel { Rows = rows, TestingUserExists = testId != null });
    }

    [HttpPost("DeleteFull/{id:guid}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFull(Guid id)
    {
        if (!await OwnedByTestingAsync(id)) return Forbid();
        await _reset.DeleteSessionFullAsync(id);
        await _audit.LogAsync(User.Identity?.Name ?? "system", "TEST_DELETE", "Session", id.ToString(),
            new { mode = "full" });
        TempData["Success"] = "تم حذف نتيجة الاختبار كاملةً.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("DeleteSelective/{id:guid}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSelective(Guid id, int[]? stages)
    {
        if (!await OwnedByTestingAsync(id)) return Forbid();
        var selected = stages ?? Array.Empty<int>();
        if (selected.Length == 0)
        {
            TempData["Error"] = "يجب اختيار مرحلة واحدة على الأقل.";
            return RedirectToAction(nameof(Index));
        }
        await _reset.ResetSessionAsync(id, selected, deleteSession: false);
        await _audit.LogAsync(User.Identity?.Name ?? "system", "TEST_DELETE", "Session", id.ToString(),
            new { mode = "selective", stages = selected });
        TempData["Success"] = "تم الحذف الانتقائي لنتيجة الاختبار.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("DeleteAll"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAll()
    {
        var testId = await TestingUserIdAsync();
        if (testId == null) return RedirectToAction(nameof(Index));
        var ids = await _db.StrategySessions.Where(s => s.OwnerUserId == testId).Select(s => s.Id).ToListAsync();
        foreach (var id in ids) await _reset.DeleteSessionFullAsync(id);
        await _audit.LogAsync(User.Identity?.Name ?? "system", "TEST_DELETE", "Session", "ALL",
            new { mode = "all", count = ids.Count });
        TempData["Success"] = $"تم حذف كل نتائج الاختبار ({ids.Count}).";
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> OwnedByTestingAsync(Guid id)
    {
        var testId = await TestingUserIdAsync();
        if (testId == null) return false;
        return await _db.StrategySessions.AnyAsync(s => s.Id == id && s.OwnerUserId == testId);
    }
}

public class TestResultRow
{
    public Guid Id { get; set; }
    public string DeptCode { get; set; } = "";
    public string DeptName { get; set; } = "";
    public string Status { get; set; } = "";
    public int CurrentStage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class TestResultsViewModel
{
    public List<TestResultRow> Rows { get; set; } = new();
    public bool TestingUserExists { get; set; }
}
