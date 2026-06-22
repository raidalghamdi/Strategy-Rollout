using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 9 / Phase 20 — live operations dashboard. Shows every department session in the
// current user's scope, grouped by sector, with a KPI strip and 5-minute auto-refresh.
// Phase 20.7 — this page is the landing page for journey accounts after sign-in. We drop
// the role gate so journey-only accounts (JourneyScopeKey set) can view it; the scope
// service already filters every query to only the departments their key permits.
[Authorize]
[Route("Admin")]
public class AdminLiveController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IJourneyScopeService _scope;

    public AdminLiveController(ApplicationDbContext db, IJourneyScopeService scope)
    {
        _db = db;
        _scope = scope;
    }

    [HttpGet("LiveDashboard")]
    public async Task<IActionResult> LiveDashboard(string status = "All", string sector = "All", string? dept = null)
    {
        var vm = await BuildDashboardAsync(status, sector, dept);
        return View(vm);
    }

    // Partial body for the 5-minute auto-refresh (returns just the grouped table + KPIs).
    [HttpGet("LiveDashboard/Rows")]
    public async Task<IActionResult> Rows(string status = "All", string sector = "All", string? dept = null)
    {
        var vm = await BuildDashboardAsync(status, sector, dept);
        return PartialView("_LiveRows", vm);
    }

    private async Task<LiveDashboardViewModel> BuildDashboardAsync(string status, string sector, string? dept)
    {
        // Phase 20 — scope: a journey user only ever sees departments in their sector;
        // GLOBAL/TEST/Admin see all. The dropdown can further narrow to a single dept.
        var visibleDepts = await _scope.GetVisibleDeptCodesAsync(User);
        var isGlobal = await _scope.IsGlobalAsync(User);
        var visibleSet = visibleDepts.ToHashSet();

        var allDepts = await _db.Departments
            .Where(d => visibleSet.Contains(d.DeptCode))
            .OrderBy(d => d.DeptCode)
            .ToListAsync();

        var sessionsQ = _db.StrategySessions
            .Where(s => visibleSet.Contains(s.DeptCode));

        if (!string.IsNullOrEmpty(dept) && dept != "All")
            sessionsQ = sessionsQ.Where(s => s.DeptCode == dept);

        if (status == "InProgress")
            sessionsQ = sessionsQ.Where(s => s.Status == "InProgress" && s.CompletedAt == null);
        else if (status == "Completed")
            sessionsQ = sessionsQ.Where(s => s.CompletedAt != null);
        else if (status == "NotStarted")
            sessionsQ = sessionsQ.Where(s => s.CurrentStage <= 1 && s.CompletedAt == null);

        var sessions = await sessionsQ
            .OrderByDescending(s => s.LastActivityAt ?? s.StartedAt)
            .Take(500)
            .ToListAsync();

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var deptNames = allDepts.ToDictionary(d => d.DeptCode, d => d.NameAr ?? d.DeptCode);
        var deptSectors = allDepts.ToDictionary(d => d.DeptCode, d => d.ParentSector);

        var memberCounts = await _db.SessionMembers
            .Where(m => sessionIds.Contains(m.SessionId))
            .GroupBy(m => m.SessionId)
            .Select(g => new { g.Key, C = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.C);

        var signedSet = (await _db.DepartmentStrategyMaps
            .Where(m => sessionIds.Contains(m.SessionId) && m.SignedAt != null)
            .Select(m => m.SessionId)
            .ToListAsync()).ToHashSet();

        var rows = sessions.Select(s =>
        {
            var sec = deptSectors.TryGetValue(s.DeptCode, out var ps) ? ps : null;
            return new LiveSessionRow
            {
                Id = s.Id,
                DeptCode = s.DeptCode,
                DeptName = deptNames.TryGetValue(s.DeptCode, out var n) ? n : s.DeptCode,
                Sector = string.IsNullOrEmpty(sec) ? "بدون قطاع" : sec,
                AccessCode = s.AccessCodeUsed,
                Status = s.Status,
                CurrentStage = Math.Clamp(s.CurrentStage, 0, 5),
                CompletedAt = s.CompletedAt,
                StartedAt = s.StartedAt,
                LastActivityAt = s.LastActivityAt,
                Members = memberCounts.TryGetValue(s.Id, out var mc) ? mc : 0,
                MapSigned = signedSet.Contains(s.Id),
            };
        }).ToList();

        if (sector != "All")
            rows = rows.Where(r => r.Sector == sector).ToList();

        // Sector sections. The "بدون قطاع" section is only shown to GLOBAL/TEST/Admin.
        var sectorOrder = new[] { "قطاع الدعم المؤسسي", "قطاع الشؤون الاقتصادية", "قطاع الشؤون القانونية" };
        var groups = new List<LiveSectorGroup>();
        foreach (var sec in sectorOrder)
        {
            var secRows = rows.Where(r => r.Sector == sec).OrderBy(r => r.DeptCode).ToList();
            if (secRows.Count > 0) groups.Add(new LiveSectorGroup { Sector = sec, Rows = secRows });
        }
        if (isGlobal)
        {
            var noneRows = rows.Where(r => r.Sector == "بدون قطاع").OrderBy(r => r.DeptCode).ToList();
            if (noneRows.Count > 0) groups.Add(new LiveSectorGroup { Sector = "بدون قطاع", Rows = noneRows });
        }

        // KPI strip — counted against the visible department set, not just listed rows.
        var completed = rows.Count(r => r.CompletedAt != null);
        var inProgress = rows.Count(r => r.CompletedAt == null && r.CurrentStage > 1);
        var notStarted = allDepts.Count - rows.Where(r => r.CompletedAt != null || r.CurrentStage > 1)
                                               .Select(r => r.DeptCode).Distinct().Count();

        var kpis = new LiveKpis
        {
            TotalDepartments = allDepts.Count,
            SessionsCompleted = completed,
            SessionsInProgress = inProgress,
            NotStarted = Math.Max(0, notStarted),
            OverallPercent = allDepts.Count == 0 ? 0
                : (int)Math.Round(100.0 * completed / allDepts.Count),
        };

        var sectorFilterOptions = sectorOrder.ToList();
        if (isGlobal) sectorFilterOptions.Add("بدون قطاع");

        return new LiveDashboardViewModel
        {
            Status = status,
            Sector = sector,
            Dept = dept ?? "All",
            IsGlobal = isGlobal,
            Kpis = kpis,
            Groups = groups,
            DeptOptions = allDepts.Select(d => (d.DeptCode, d.NameAr ?? d.DeptCode)).ToList(),
            SectorOptions = sectorFilterOptions,
        };
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
    public string Sector { get; set; } = "";
    public string? AccessCode { get; set; }
    public string Status { get; set; } = "";
    public int CurrentStage { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public int Members { get; set; }
    public bool MapSigned { get; set; }

    // Status bucket for colour coding: green / yellow / gray.
    public string StatusKey =>
        CompletedAt != null && CurrentStage >= 5 ? "Completed"
        : CurrentStage > 1 ? "InProgress"
        : "NotStarted";

    public int ProgressPercent => (int)Math.Round(100.0 * Math.Clamp(CurrentStage, 0, 5) / 5);
}

public class LiveSectorGroup
{
    public string Sector { get; set; } = "";
    public List<LiveSessionRow> Rows { get; set; } = new();
}

public class LiveKpis
{
    public int TotalDepartments { get; set; }
    public int SessionsCompleted { get; set; }
    public int SessionsInProgress { get; set; }
    public int NotStarted { get; set; }
    public int OverallPercent { get; set; }
}

public class LiveDashboardViewModel
{
    public string Status { get; set; } = "All";
    public string Sector { get; set; } = "All";
    public string Dept { get; set; } = "All";
    public bool IsGlobal { get; set; }
    public LiveKpis Kpis { get; set; } = new();
    public List<LiveSectorGroup> Groups { get; set; } = new();
    public List<(string Code, string Name)> DeptOptions { get; set; } = new();
    public List<string> SectorOptions { get; set; } = new();
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
