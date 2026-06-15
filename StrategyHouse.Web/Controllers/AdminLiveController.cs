using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Controllers;

// Phase 9 — live operations dashboard: every department session with its
// current journey stage, activity, and assessment counts. Auto-refreshes.
[Authorize(Roles = "Admin,Facilitator")]
[Route("Admin")]
public class AdminLiveController : Controller
{
    private readonly ApplicationDbContext _db;

    public AdminLiveController(ApplicationDbContext db) { _db = db; }

    [HttpGet("LiveDashboard")]
    public async Task<IActionResult> LiveDashboard(string status = "All")
    {
        var rows = await BuildRowsAsync(status);
        ViewBag.Status = status;
        return View(rows);
    }

    // Partial body for the 10s auto-refresh (returns just the table).
    [HttpGet("LiveDashboard/Rows")]
    public async Task<IActionResult> Rows(string status = "All")
    {
        var rows = await BuildRowsAsync(status);
        ViewBag.Status = status;
        return PartialView("_LiveRows", rows);
    }

    private async Task<List<LiveSessionRow>> BuildRowsAsync(string status)
    {
        IQueryable<Domain.Entities.StrategySession> q = _db.StrategySessions;
        if (status == "InProgress") q = q.Where(s => s.Status == "InProgress");
        else if (status == "Completed") q = q.Where(s => s.Status != "InProgress" || s.CompletedAt != null);

        var sessions = await q.OrderByDescending(s => s.LastActivityAt ?? s.StartedAt).Take(300).ToListAsync();

        var deptNames = await _db.Departments
            .ToDictionaryAsync(d => d.DeptCode, d => d.NameAr ?? d.DeptCode);

        var sessionIds = sessions.Select(s => s.Id).ToList();

        var memberCounts = await _db.SessionMembers
            .Where(m => sessionIds.Contains(m.SessionId))
            .GroupBy(m => m.SessionId)
            .Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C);

        var mapsSigned = await _db.DepartmentStrategyMaps
            .Where(m => sessionIds.Contains(m.SessionId) && m.SignedAt != null)
            .Select(m => m.SessionId)
            .ToListAsync();
        var signedSet = mapsSigned.ToHashSet();

        var quizCounts = await _db.QuizAttempts
            .Where(a => a.SessionId != null && sessionIds.Contains(a.SessionId!.Value))
            .GroupBy(a => a.SessionId!.Value)
            .Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C);

        var deptCodes = sessions.Select(s => s.DeptCode).Distinct().ToList();
        var surveyByDept = await _db.SurveyResponses
            .Where(r => r.DeptCode != null && deptCodes.Contains(r.DeptCode))
            .GroupBy(r => r.DeptCode!)
            .Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C);

        return sessions.Select(s => new LiveSessionRow
        {
            Id = s.Id,
            DeptCode = s.DeptCode,
            DeptName = deptNames.TryGetValue(s.DeptCode, out var n) ? n : s.DeptCode,
            AccessCode = s.AccessCodeUsed,
            Status = s.Status,
            CurrentStage = Math.Clamp(s.CurrentStage, 1, 6),
            StartedAt = s.StartedAt,
            LastActivityAt = s.LastActivityAt,
            Members = memberCounts.TryGetValue(s.Id, out var mc) ? mc : 0,
            MapSigned = signedSet.Contains(s.Id),
            QuizAttempts = quizCounts.TryGetValue(s.Id, out var qc) ? qc : 0,
            SurveyResponses = surveyByDept.TryGetValue(s.DeptCode, out var sc) ? sc : 0,
        }).ToList();
    }

    [HttpGet("SessionDetail/{id:guid}")]
    public async Task<IActionResult> SessionDetail(Guid id)
    {
        var s = await _db.StrategySessions.FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound();

        var dept = await _db.Departments.FirstOrDefaultAsync(d => d.DeptCode == s.DeptCode);
        var members = await _db.SessionMembers.Where(m => m.SessionId == id).ToListAsync();
        var pledges = await _db.ContributionPledges.Where(p => p.SessionId == id).ToListAsync();
        var map = await _db.DepartmentStrategyMaps.FirstOrDefaultAsync(m => m.SessionId == id && m.IsActive);
        var quizAttempts = await _db.QuizAttempts.Where(a => a.SessionId == id)
            .OrderByDescending(a => a.CompletedAt).ToListAsync();

        var timeline = new List<(DateTime When, string Label)>
        {
            (s.StartedAt, "بدء الجلسة"),
        };
        if (s.MembersSubmittedAt != null) timeline.Add((s.MembersSubmittedAt.Value, "تسجيل الفريق"));
        foreach (var p in pledges) timeline.Add((p.CreatedAt, $"مساهمة: {p.ElementType} {p.ElementCode}"));
        if (map?.SignedAt != null) timeline.Add((map.SignedAt.Value, "اعتماد وتوقيع الخريطة"));
        foreach (var a in quizAttempts) timeline.Add((a.CompletedAt, $"اختبار: {a.Score}/{a.Total}"));
        if (s.CompletedAt != null) timeline.Add((s.CompletedAt.Value, "إتمام الجلسة"));

        var vm = new SessionDetailViewModel
        {
            Session = s,
            DeptName = dept?.NameAr ?? s.DeptCode,
            Members = members,
            Pledges = pledges,
            Map = map,
            QuizAttempts = quizAttempts,
            Timeline = timeline.OrderBy(t => t.When).ToList(),
        };
        return View(vm);
    }
}

public class LiveSessionRow
{
    public Guid Id { get; set; }
    public string DeptCode { get; set; } = "";
    public string DeptName { get; set; } = "";
    public string? AccessCode { get; set; }
    public string Status { get; set; } = "";
    public int CurrentStage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public int Members { get; set; }
    public bool MapSigned { get; set; }
    public int QuizAttempts { get; set; }
    public int SurveyResponses { get; set; }
}

public class SessionDetailViewModel
{
    public Domain.Entities.StrategySession Session { get; set; } = null!;
    public string DeptName { get; set; } = "";
    public List<Domain.Entities.SessionMember> Members { get; set; } = new();
    public List<Domain.Entities.ContributionPledge> Pledges { get; set; } = new();
    public Domain.Entities.DepartmentStrategyMap? Map { get; set; }
    public List<Domain.Entities.QuizAttempt> QuizAttempts { get; set; } = new();
    public List<(DateTime When, string Label)> Timeline { get; set; } = new();
}
