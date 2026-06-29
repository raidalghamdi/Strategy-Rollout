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

        // Phase 20.26 — KPI strip now honors the active filters (sector + dept).
        // Previously KPIs were always counted against the full scope (allDepts), so
        // selecting a single department did not update the cards. The denominator is
        // now the set of departments matching the current filter selection.
        var deptsInScope = allDepts.AsEnumerable();
        if (!string.IsNullOrEmpty(dept) && dept != "All")
            deptsInScope = deptsInScope.Where(d => d.DeptCode == dept);
        if (sector != "All")
            deptsInScope = deptsInScope.Where(d => (d.ParentSector ?? "بدون قطاع") == sector);
        var filteredDepts = deptsInScope.ToList();
        var filteredDeptCodes = filteredDepts.Select(d => d.DeptCode).ToHashSet();

        // "rows" already reflects the sector filter (line 108-109). For the dept filter
        // we already applied it earlier in the EF query (sessionsQ).
        // Phase 20.36 — cards rewritten: total attendees, completed journeys (pre-quiz finished),
        // quizzes done, and survey participants (linked via DepartmentRoster.EmailNormalized).
        var kpiRows = rows.Where(r => filteredDeptCodes.Contains(r.DeptCode)).ToList();
        var completedJourneys = kpiRows.Count(r => r.CompletedAt != null);
        var kpiSessionIds = kpiRows.Select(r => r.Id).ToList();

        var totalAttendees = await _db.SessionMembers
            .Where(m => kpiSessionIds.Contains(m.SessionId))
            .CountAsync();

        var quizzesDone = await _db.QuizAttempts
            .Where(q => q.DeptCode != null && filteredDeptCodes.Contains(q.DeptCode))
            .CountAsync();

        // Survey participants — join SurveyResponses to DepartmentRoster via EmailNormalized
        var responses = await _db.SurveyResponses
            .Select(r => new { r.Id, r.DeptCode, r.RespondentName })
            .ToListAsync();
        var rosterEntries = await _db.DepartmentRoster
            .Where(m => m.IsActive && m.EmailNormalized != null)
            .Select(m => new { m.EmailNormalized, m.DeptCode })
            .ToListAsync();
        var emailToDept = rosterEntries
            .Where(m => !string.IsNullOrEmpty(m.EmailNormalized))
            .GroupBy(m => m.EmailNormalized!)
            .ToDictionary(g => g.Key, g => g.First().DeptCode);

        int surveyParticipants = 0;
        foreach (var r in responses)
        {
            if (!string.IsNullOrEmpty(r.DeptCode) && filteredDeptCodes.Contains(r.DeptCode))
            {
                surveyParticipants++;
                continue;
            }
            var email = (r.RespondentName ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(email) && emailToDept.TryGetValue(email, out var dc)
                && filteredDeptCodes.Contains(dc))
            {
                surveyParticipants++;
            }
        }

        // Per-department attendee breakdown (replaces the map)
        var attendeesByDept = (await _db.SessionMembers
            .Where(m => kpiSessionIds.Contains(m.SessionId))
            .Join(_db.StrategySessions, m => m.SessionId, s => s.Id, (m, s) => new { s.DeptCode })
            .ToListAsync())
            .GroupBy(x => x.DeptCode)
            .ToDictionary(g => g.Key, g => g.Count());

        var deptAttendance = filteredDepts
            .Select(d => new DeptAttendeeRow
            {
                DeptCode = d.DeptCode,
                DeptName = d.NameAr ?? d.DeptCode,
                Sector = string.IsNullOrEmpty(d.ParentSector) ? "الإدارات الأخرى" : d.ParentSector,
                Attendees = attendeesByDept.TryGetValue(d.DeptCode, out var c) ? c : 0,
            })
            .OrderByDescending(x => x.Attendees)
            .ThenBy(x => x.DeptCode)
            .ToList();

        var kpis = new LiveKpis
        {
            TotalDepartments = filteredDepts.Count,
            TotalAttendees = totalAttendees,
            CompletedJourneys = completedJourneys,
            QuizzesDone = quizzesDone,
            SurveyParticipants = surveyParticipants,
        };

        // Phase 20.26 — sector filter is limited to the user's actual scope.
        // Admin / GLOBAL / TEST sees all three sectors + "بدون قطاع".
        // A VP only sees their own sector (and a single-sector dropdown is hidden
        // by the view because there is nothing to choose).
        List<string> sectorFilterOptions;
        if (isGlobal)
        {
            sectorFilterOptions = sectorOrder.ToList();
            sectorFilterOptions.Add("بدون قطاع");
        }
        else
        {
            // Resolve VP's own sector via the visible departments.
            var visibleSectors = allDepts
                .Select(d => d.ParentSector)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList()!;
            sectorFilterOptions = visibleSectors!;
        }

        return new LiveDashboardViewModel
        {
            Status = status,
            Sector = sector,
            Dept = dept ?? "All",
            IsGlobal = isGlobal,
            Kpis = kpis,
            Groups = groups,
            DeptAttendance = deptAttendance,
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
    // Phase 20.36 — relabelled cards.
    public int TotalDepartments { get; set; }
    public int TotalAttendees { get; set; }       // replaces نسبة الإنجاز
    public int CompletedJourneys { get; set; }    // replaces مكتملة
    public int QuizzesDone { get; set; }          // replaces قيد التنفيذ
    public int SurveyParticipants { get; set; }   // replaces لم تبدأ
}

public class DeptAttendeeRow
{
    public string DeptCode { get; set; } = "";
    public string DeptName { get; set; } = "";
    public string Sector { get; set; } = "";
    public int Attendees { get; set; }
}

public class LiveDashboardViewModel
{
    public string Status { get; set; } = "All";
    public string Sector { get; set; } = "All";
    public string Dept { get; set; } = "All";
    public bool IsGlobal { get; set; }
    public LiveKpis Kpis { get; set; } = new();
    public List<LiveSectorGroup> Groups { get; set; } = new();
    public List<DeptAttendeeRow> DeptAttendance { get; set; } = new();
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
