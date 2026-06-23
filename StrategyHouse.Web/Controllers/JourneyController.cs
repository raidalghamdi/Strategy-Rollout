using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;
using Dtos = StrategyHouse.Web.Services.Dtos;

namespace StrategyHouse.Web.Controllers;

// Anonymous, access-code-gated department journey flow.
[AllowAnonymous]
public class JourneyController : Controller
{
    // Phase 20.9 — inserted a read-only GAC Goals stage at position 3 (الأهداف
    // الاستراتيجية للهيئة) and shifted the others. The journey is now 6 stages:
    // 1 BigPicture · 2 StrategyHouse · 3 GacGoals (NEW, read-only) · 4 Contribute
    // · 5 Map (vision journey) · 6 Complete.
    private const int MaxStage = 6;

    private readonly ApplicationDbContext _db;
    private readonly StrategyContentService _content;
    private readonly StrategyMapPdfService _pdf;
    private readonly DepartmentDirectoryService _departments;
    private readonly IStrategyDataProvider _strategyData;
    private readonly IStrategyDataSource _source;
    private readonly IJourneyScopeService _scope;
    private readonly ILogger<JourneyController> _logger;

    public JourneyController(
        ApplicationDbContext db,
        StrategyContentService content,
        StrategyMapPdfService pdf,
        DepartmentDirectoryService departments,
        IStrategyDataProvider strategyData,
        IStrategyDataSource source,
        IJourneyScopeService scope,
        ILogger<JourneyController> logger)
    {
        _db = db;
        _content = content;
        _pdf = pdf;
        _departments = departments;
        _strategyData = strategyData;
        _source = source;
        _scope = scope;
        _logger = logger;
    }

    // Phase 20.7 — Pick removed. Journey accounts land on /Admin/LiveDashboard which
    // already has a department dropdown and links into each department's sessions.

    // Phase 19.23 — map unified-source DTOs back to the entity shapes the journey
    // views, PDF service, and Sankey builder bind to. Only the properties those
    // consumers actually read are populated. Data flows MSSQL mirror → SQLite → empty
    // through IStrategyDataSource; nothing here reads tables or hardcodes data.
    private static List<Pillar> ToPillarEntities(IReadOnlyList<Dtos.StrategyPillarDto> src) =>
        src.Select(p => new Pillar { PlrCode = p.Code, PillarName = p.Name, Budget = p.Budget, Liquidity = p.Liquidity }).ToList();

    private static List<Objective> ToObjectiveEntities(IReadOnlyList<Dtos.StrategyObjectiveDto> src) =>
        src.Select(o => new Objective { ObjectiveCode = o.Code, ObjectiveName = o.Name, PlrCode = o.PillarCode, Budget = o.Budget, Liquidity = o.Liquidity }).ToList();

    private static List<Initiative> ToInitiativeEntities(IReadOnlyList<Dtos.StrategyInitiativeDto> src) =>
        src.Select(i => new Initiative { InitiativeCode = i.Code, InitiativeName = i.Name, ObjectiveCode = i.ObjectiveCode, Owners = i.Owners, Budget = i.Budget, Liquidity = i.Liquidity }).ToList();

    private static List<Kpi> ToKpiEntities(IReadOnlyList<Dtos.StrategyKpiDto> src) =>
        src.Select(k => new Kpi { KpiCode = k.Code, KpiName = k.Name, ObjectiveCode = k.ObjectiveCode, Division = k.Division, KpiType = k.Type, ActivationStatus = k.Active ? "Active" : "Inactive" }).ToList();

    private static List<Project> ToProjectEntities(IReadOnlyList<Dtos.StrategyProjectDto> src) =>
        src.Select(p => new Project { ProjectCode = p.Code, ProjectName = p.Name, InitiativeCode = p.InitiativeCode, Division = p.Division, ProjectType = p.Type, ProjectStatus = p.Status, Budget = p.Budget, Liquidity = p.Liquidity, GacBudget = p.GacBudget }).ToList();

    // GET /Journey — landing page with code entry.
    [HttpGet("Journey")]
    public IActionResult Index(string? code)
    {
        ViewBag.Code = code;
        ViewBag.Content = _content;
        return View();
    }

    // POST /Journey/Start — validate code, create session.
    [HttpPost("Journey/Start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(string code)
    {
        code = (code ?? string.Empty).Trim().ToUpperInvariant();
        var access = await _db.DepartmentAccessCodes
            .FirstOrDefaultAsync(c => c.Code == code && c.IsActive);
        if (access == null)
        {
            TempData["Error"] = "رمز الدخول غير صحيح أو غير مفعّل.";
            return RedirectToAction(nameof(Index));
        }

        access.UsedCount++;
        // Phase 20 — when a signed-in platform user (e.g. the testing account) starts a
        // session, stamp ownership so /Admin/TestResults can scope deletions to them.
        int? ownerId = null;
        var idClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (User.Identity?.IsAuthenticated == true && int.TryParse(idClaim, out var uid))
            ownerId = uid;
        var session = new StrategySession
        {
            DeptCode = access.DeptCode,
            AccessCodeUsed = access.Code,
            OwnerUserId = ownerId,
        };
        _db.StrategySessions.Add(session);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Run), new { sessionId = session.Id });
    }

    // GET /Journey/Sector — Phase 20.9 sector-wide journey launcher for VP accounts.
    // Resolves the signed-in VP's JourneyScopeKey (e.g. SECTOR:CORP_SUPPORT) and
    // either resumes the latest sector session (if one is in progress) or creates
    // a new one anchored on a synthetic SectorDeptCode so the existing journey
    // flow can render it without a real Department row.
    [HttpGet("Journey/Sector")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> Sector(
        [FromServices] Microsoft.AspNetCore.Identity.UserManager<StrategyHouse.Domain.Entities.AppUser> userManager,
        string? sector = null)
    {
        var appUser = await userManager.GetUserAsync(User);
        if (appUser == null) return Challenge();
        var scopeKey = appUser.JourneyScopeKey;

        // Phase 20.15 — the original implementation required scopeKey to start
        // with "SECTOR:" so only VP accounts could launch a sector journey.
        // gac.admin (scope = GLOBAL, role JourneySuper) and platform Admins now
        // get access too — either by picking a sector from a chooser when no
        // ?sector=… is provided, or by passing the desired sector key directly
        // (e.g. /Journey/Sector?sector=SECTOR:CORP_SUPPORT). This lets executive
        // accounts walk the exact same Stage 1–6 flow VPs see for QA.
        bool isGlobal = User.IsInRole("Admin")
                        || scopeKey == "GLOBAL"
                        || scopeKey == "TEST";

        string? effectiveScope = scopeKey;
        if (isGlobal)
        {
            // Phase 20.16 — by default executive / GLOBAL accounts walk ONE unified
            // journey that aggregates all sectors. Passing ?sector=SECTOR:* is still
            // supported (e.g. for QA-ing a single sector) and overrides the default.
            if (string.IsNullOrEmpty(sector))
            {
                effectiveScope = "SECTOR:ALL"; // → SEC_ALL synthetic dept code
            }
            else if (!sector.StartsWith("SECTOR:", StringComparison.Ordinal)
                     || SyntheticSectorCode(sector) == "SEC_UNKNOWN")
            {
                TempData["Error"] = "القطاع غير صالح.";
                return Redirect("/Admin/LiveDashboard");
            }
            else
            {
                effectiveScope = sector;
            }
        }
        else if (string.IsNullOrEmpty(scopeKey) || !scopeKey.StartsWith("SECTOR:", StringComparison.Ordinal))
        {
            TempData["Error"] = "حسابك غير مرتبط بقطاع. تواصل مع المسؤول.";
            return Redirect("/Admin/LiveDashboard");
        }

        // Synthetic dept code so the existing session-keyed pipeline works without
        // creating real Department rows. The code fits the MaxLength(15) on
        // StrategySession.DeptCode (e.g. "SEC_CORP_SUPP").
        var sectorCode = SyntheticSectorCode(effectiveScope!);

        // Resume the latest in-progress sector session for this user if any.
        var idClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        int? ownerId = int.TryParse(idClaim, out var uid) ? uid : (int?)null;
        var existing = await _db.StrategySessions
            .Where(s => s.DeptCode == sectorCode
                        && s.Status == "InProgress"
                        && (s.OwnerUserId == ownerId || s.OwnerUserId == null))
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync();
        if (existing != null)
            return RedirectToAction(nameof(Run), new { sessionId = existing.Id });

        var session = new StrategySession
        {
            DeptCode = sectorCode,
            AccessCodeUsed = effectiveScope!,
            OwnerUserId = ownerId,
        };
        _db.StrategySessions.Add(session);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Run), new { sessionId = session.Id });
    }

    // Phase 20.9 — maps a SECTOR:* JourneyScopeKey to a stable, 15-char synthetic
    // DeptCode that the StrategySession.DeptCode column can hold.
    private static string SyntheticSectorCode(string scopeKey) => scopeKey switch
    {
        "SECTOR:CORP_SUPPORT" => "SEC_CORP_SUPP",
        "SECTOR:ECONOMIC" => "SEC_ECONOMIC",
        "SECTOR:LEGAL" => "SEC_LEGAL",
        // Phase 20.16 — unified journey for executive / GLOBAL accounts (gac.admin):
        // a single synthetic DeptCode that aggregates ALL sectors / ALL departments.
        "GLOBAL" => "SEC_ALL",
        "TEST" => "SEC_ALL",
        "SECTOR:ALL" => "SEC_ALL",
        _ => "SEC_UNKNOWN",
    };

    // Phase 20.9 — reverse map: synthetic sector dept code → Arabic display name.
    // Used by BuildRunViewModelAsync so the journey header shows "رحلة قطاع ...".
    // Phase 20.16 — SEC_ALL is the unified executive scope (all sectors combined).
    private static string? SectorDisplayName(string deptCode) => deptCode switch
    {
        "SEC_CORP_SUPP" => "قطاع الدعم المؤسسي",
        "SEC_ECONOMIC" => "قطاع الشؤون الاقتصادية",
        "SEC_LEGAL" => "قطاع الشؤون القانونية",
        // Phase 20.17 — the unified executive scope (gac.admin) reframes from
        // "all sectors" to "the agency as a whole". Header becomes
        // "رحلة الهيئة الشاملة".
        "SEC_ALL" => "الهيئة الشاملة",
        _ => null,
    };

    // GET /Journey/Run/{sessionId} — entry point. Redirects to the multi-page stage flow
    // at the furthest stage the team has reached.
    [HttpGet("Journey/Run/{sessionId:guid}")]
    public async Task<IActionResult> Run(Guid sessionId)
    {
        var session = await _db.StrategySessions.FindAsync(sessionId);
        if (session == null) return NotFound();
        var stage = Math.Clamp(session.CurrentStage <= 0 ? 1 : session.CurrentStage, 1, MaxStage);
        return RedirectToAction(nameof(RunStage), new { sessionId, stage });
    }

    // GET /Journey/Run/{sessionId}/{stage} — Phase 9 multi-page journey: one stage per page.
    // Anti-skip: a requested stage is clamped to CurrentStage + 1.
    [HttpGet("Journey/Run/{sessionId:guid}/{stage:int}")]
    public async Task<IActionResult> RunStage(Guid sessionId, int stage)
    {
        var tracked = await _db.StrategySessions.FindAsync(sessionId);
        if (tracked == null) return NotFound();

        // Clamp the requested stage so users can't jump ahead of where they've reached.
        var maxAllowed = Math.Clamp((tracked.CurrentStage <= 0 ? 1 : tracked.CurrentStage) + 1, 1, MaxStage);
        stage = Math.Clamp(stage, 1, MaxStage);
        if (stage > maxAllowed)
            return RedirectToAction(nameof(RunStage), new { sessionId, stage = maxAllowed });

        // Phase 18 — the redesigned stage 4 (الرحلة نحو الرؤية) takes no required input,
        // so advancing to stage 5 (الأثر) is no longer gated. The attendee count moved
        // to stage 5 as an optional field.

        // Record progress: bump the furthest stage reached + activity timestamp.
        if (stage > tracked.CurrentStage)
        {
            tracked.CurrentStage = stage;
            tracked.LastActivityAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        var vm = await BuildRunViewModelAsync(sessionId);
        if (vm == null) return NotFound();
        ViewBag.Stage = stage;
        // Phase 20.13 — iOS Safari was treating URLs ending in a bare digit
        // (e.g. /Journey/Run/{guid}/4) as downloadable files and showing
        // "Do you want to download '4'" when the user tapped «التالي». Force an
        // explicit HTML content type + inline disposition so the browser
        // always renders the page instead of offering a download.
        Response.ContentType = "text/html; charset=utf-8";
        Response.Headers["Content-Disposition"] = "inline";
        return View("RunStage", vm);
    }

    // Shared builder for the journey view model (all stage partials read from it).
    private async Task<JourneyRunViewModel?> BuildRunViewModelAsync(Guid sessionId)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null) return null;

        var dept = await _db.Departments.FindAsync(session.DeptCode);
        // Phase 20.9 — if this is a synthetic sector session (no matching
        // Departments row) build an in-memory pseudo-Department so the rest of
        // the journey UI can render "رحلة قطاع ..." without errors.
        if (dept == null)
        {
            var sectorName = SectorDisplayName(session.DeptCode);
            if (sectorName != null)
            {
                dept = new Department
                {
                    DeptCode = session.DeptCode,
                    NameAr = sectorName,
                    NameEn = sectorName,
                    ParentSector = sectorName,
                    IsActive = true,
                };
            }
        }
        var map = await _db.DepartmentStrategyMaps.FirstOrDefaultAsync(m => m.SessionId == sessionId);

        var housePillars = await BuildHousePillarsAsync();
        var departmentNames = (await _departments.GetDepartmentsAsync())
            .Select(d => d.NameAr)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        var role = await _db.RoleContributions
            .Where(r => r.SessionId == sessionId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        // Phase 19.23 — all strategy reads route through the unified source
        // (MSSQL mirror → SQLite → empty). DTOs are mapped to entity shapes for the
        // views; dedup is preserved at this read layer.
        var ct = HttpContext.RequestAborted;
        List<Kpi> deptKpis;
        List<Project> deptProjects;
        // Phase 20.22 — collect the user's reachable dept names so stage 5
        // pickers can be scoped just like stage 4 (employee = own dept,
        // VP = sector aggregate, gac.admin/SEC_ALL = every active dept).
        List<string> userScopedDeptNames;
        var sectorDisplay = SectorDisplayName(session.DeptCode);
        if (sectorDisplay != null)
        {
            // Phase 20.10 (Fix 6) — for a synthetic sector session, aggregate KPIs +
            // Projects from ALL departments whose ParentSector matches the sector's
            // Arabic display name. The unified source is queried per department and
            // results are deduped by code so the journey shows the full sector view.
            // Phase 20.16 — SEC_ALL (executive / gac.admin unified journey) aggregates
            // across EVERY active department regardless of ParentSector.
            var sectorDeptsQuery = _db.Departments.Where(d => d.IsActive);
            if (session.DeptCode != "SEC_ALL")
            {
                sectorDeptsQuery = sectorDeptsQuery.Where(d => d.ParentSector == sectorDisplay);
            }
            var sectorDepts = await sectorDeptsQuery
                .Select(d => d.NameAr)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct()
                .ToListAsync();
            userScopedDeptNames = sectorDepts.ToList();

            var aggKpis = new List<Kpi>();
            var aggProjects = new List<Project>();
            foreach (var deptName in sectorDepts)
            {
                aggKpis.AddRange(ToKpiEntities(await _source.GetKpisAsync(deptName, ct)));
                aggProjects.AddRange(ToProjectEntities(await _source.GetProjectsAsync(deptName, ct)));
            }
            deptKpis = aggKpis
                .GroupBy(k => string.IsNullOrWhiteSpace(k.KpiCode) ? k.KpiName : k.KpiCode, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            deptProjects = aggProjects
                .GroupBy(p => string.IsNullOrWhiteSpace(p.ProjectCode) ? p.ProjectName : p.ProjectCode, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }
        else
        {
            deptKpis = ToKpiEntities(await _source.GetKpisAsync(session.DeptCode, ct));
            deptProjects = ToProjectEntities(await _source.GetProjectsAsync(session.DeptCode, ct));
            // Phase 20.22 — regular employee: their only reachable dept is their own.
            userScopedDeptNames = new List<string>();
            if (!string.IsNullOrWhiteSpace(dept?.NameAr)) userScopedDeptNames.Add(dept!.NameAr!);
        }

        // Phase 20.22 — Stage 3 full unfiltered lists (no Take cap, no dept filter).
        // Used by _StageGacGoals.cshtml so every user sees every initiative + KPI.
        var allInitiativesFull = StrategyDedup
            .ByInitiativeCode(ToInitiativeEntities(await _source.GetInitiativesAsync(null, ct)))
            .ToList();
        var allKpisFull = ToKpiEntities(await _source.GetKpisAsync(null, ct))
            .GroupBy(k => string.IsNullOrWhiteSpace(k.KpiCode) ? k.KpiName : k.KpiCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // Phase 20.23 — Stage 3 per-user filtering. SEC_ALL (admin / ChiefStrategy /
        // الهيئة الشاملة) sees every objective. Everyone else (VP sector aggregate,
        // GM, regular employee) sees only the objectives their دائرة/قطاع actually
        // touches — derived from (a) KPIs already loaded for their scope and
        // (b) initiatives whose Owners contains a reachable dept name.
        List<Initiative> stage3Initiatives;
        List<Kpi> stage3Kpis;
        HashSet<string> stage3ObjectiveCodes;
        bool stage3SeesAll = session.DeptCode == "SEC_ALL"
            || string.Equals(dept?.NameAr, "الهيئة الشاملة", StringComparison.Ordinal);
        if (stage3SeesAll)
        {
            stage3Initiatives = allInitiativesFull;
            stage3Kpis = allKpisFull;
            stage3ObjectiveCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // sentinel: empty set + flag means "show every objective" downstream.
        }
        else
        {
            var ownerNeedles = userScopedDeptNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.ToLowerInvariant())
                .ToHashSet();
            stage3Initiatives = allInitiativesFull
                .Where(i => !string.IsNullOrEmpty(i.Owners)
                            && ownerNeedles.Any(o => i.Owners!.ToLowerInvariant().Contains(o)))
                .ToList();
            stage3Kpis = deptKpis;
            stage3ObjectiveCodes = new HashSet<string>(
                stage3Kpis.Select(k => k.ObjectiveCode ?? string.Empty)
                    .Concat(stage3Initiatives.Select(i => i.ObjectiveCode ?? string.Empty))
                    .Where(c => !string.IsNullOrWhiteSpace(c)),
                StringComparer.OrdinalIgnoreCase);
        }

        // Phase 20.22 — Stage 5 scoped lists. gac.admin/SEC_ALL gets every project.
        List<Project> scopedProjects;
        List<Initiative> scopedInitiatives;
        if (session.DeptCode == "SEC_ALL" || string.Equals(dept?.NameAr, "الهيئة الشاملة", StringComparison.Ordinal))
        {
            scopedProjects = ToProjectEntities(await _source.GetProjectsAsync(null, ct))
                .GroupBy(p => string.IsNullOrWhiteSpace(p.ProjectCode) ? p.ProjectName : p.ProjectCode, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            scopedInitiatives = allInitiativesFull;
        }
        else
        {
            var sp = new List<Project>();
            foreach (var name in userScopedDeptNames)
            {
                sp.AddRange(ToProjectEntities(await _source.GetProjectsAsync(name, ct)));
            }
            scopedProjects = sp
                .GroupBy(p => string.IsNullOrWhiteSpace(p.ProjectCode) ? p.ProjectName : p.ProjectCode, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            // Initiative is in-scope when its Owners contains any reachable dept name
            // OR a scoped project rolls up to it via InitiativeCode (same logic
            // already used in /Journey/InitiativesForDepartment).
            var ownerCandidates = userScopedDeptNames
                .Select(n => n.ToLowerInvariant())
                .ToHashSet();
            var scopedInitCodes = scopedProjects
                .Where(p => !string.IsNullOrEmpty(p.InitiativeCode))
                .Select(p => p.InitiativeCode!)
                .ToHashSet();
            scopedInitiatives = allInitiativesFull
                .Where(i =>
                    (!string.IsNullOrEmpty(i.Owners) && ownerCandidates.Any(o => i.Owners!.ToLowerInvariant().Contains(o)))
                    || (!string.IsNullOrEmpty(i.InitiativeCode) && scopedInitCodes.Contains(i.InitiativeCode))
                )
                .ToList();
        }

        return new JourneyRunViewModel
        {
            Session = session,
            Dept = dept,
            Content = _content,
            Sankey = await BuildSankeyAsync(session.DeptCode),
            // Phase 19.20 (Fix 2) — dedup strategy elements at the read layer so the
            // journey views never show the same pillar/objective/initiative twice.
            Pillars = StrategyDedup.ByPillarCode(ToPillarEntities(await _source.GetPillarsAsync(ct))),
            Objectives = StrategyDedup.ByObjectiveCode(ToObjectiveEntities(await _source.GetObjectivesAsync(ct))),
            DeptObjectiveCodes = deptKpis.Select(k => k.ObjectiveCode).Distinct().ToList(),
            // Phase 20.22 — Initiatives now uses the already-fetched full list,
            // and the arbitrary Take(40) cap is removed.
            Initiatives = allInitiativesFull,
            Roster = await _db.DepartmentRoster
                .Where(r => r.DeptCode == session.DeptCode && r.IsActive)
                .OrderBy(r => r.NameAr).ToListAsync(),
            Pledges = await _db.ContributionPledges.Where(p => p.SessionId == sessionId).ToListAsync(),
            Kpis = deptKpis,
            Projects = deptProjects,
            // Phase 20.11 — full lists (all departments) for the stage-5 initiative picker.
            AllProjects = ToProjectEntities(await _source.GetProjectsAsync(null, ct))
                .GroupBy(p => string.IsNullOrWhiteSpace(p.ProjectCode) ? p.ProjectName : p.ProjectCode, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList(),
            AllInitiatives = allInitiativesFull,
            // Phase 20.22 — Stage 3 (universal overview) + Stage 5 (scoped).
            AllInitiativesForStage3 = allInitiativesFull,
            AllKpisForStage3 = allKpisFull,
            // Phase 20.23 — Stage 3 per-user scope (admin/SEC_ALL → all, else dept/sector).
            Stage3SeesAllObjectives = stage3SeesAll,
            Stage3ObjectiveCodes = stage3ObjectiveCodes.ToList(),
            ScopedInitiativesForStage3 = stage3Initiatives,
            ScopedKpisForStage3 = stage3Kpis,
            ScopedProjects = scopedProjects,
            ScopedInitiatives = scopedInitiatives,
            Map = map,
            InkAssets = map == null
                ? new List<MapInkAsset>()
                : await _db.MapInkAssets.Where(a => a.MapId == map.Id && a.IsActive).ToListAsync(),
            SelectedTeamValue = await _db.TeamValueSelections
                .Where(t => t.SessionId == sessionId)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => t.SelectedValueText)
                .FirstOrDefaultAsync(),

            // Phase 18/19.23 — "live" now means the unified source returned real
            // strategy data (mirror or SQLite), not whether the external DB is wired.
            StrategyDataLive = housePillars.Count > 0,
            HousePillars = housePillars,
            DepartmentNames = departmentNames,
            OpeningReflectionText = await _db.OpeningReflections
                .Where(r => r.SessionId == sessionId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => r.ReflectionText)
                .FirstOrDefaultAsync(),
            RoleContribution = role,
            SelectedInitiative = role?.SelectedInitiativeCode is { Length: > 0 } code
                ? await ResolveInitiativeAsync(code)
                : null,
        };
    }

    // Phase 18/19.23 — vision → pillars → objectives for the strategy-house stage.
    // Sourced through the unified data source (MSSQL mirror → SQLite → empty); never
    // hardcoded. When empty, the view shows the "no strategy data" notice.
    // Phase 20.11 — Tree displays Pillar → Objective only (no initiatives/projects).
    private async Task<List<StrategyPillarVm>> BuildHousePillarsAsync()
    {
        var ct = HttpContext.RequestAborted;
        // Phase 19.20 (Fix 2) — dedup pillars/objectives by code before projecting so
        // the strategy house never renders the same pillar or objective twice.
        var pillars = StrategyDedup.ByPillarCode(ToPillarEntities(await _source.GetPillarsAsync(ct)))
            .OrderBy(p => p.PlrCode, StringComparer.Ordinal)
            .ToList();
        var objectives = StrategyDedup.ByObjectiveCode(ToObjectiveEntities(await _source.GetObjectivesAsync(ct)))
            .OrderBy(o => o.ObjectiveCode, StringComparer.Ordinal)
            .ToList();

        return pillars.Select(p => new StrategyPillarVm
        {
            Code = p.PlrCode,
            Name = p.PillarName ?? p.PlrCode,
            Objectives = objectives
                .Where(o => o.PlrCode == p.PlrCode)
                .Select(o => new StrategyObjectiveVm
                {
                    Code = o.ObjectiveCode,
                    Name = o.ObjectiveName ?? o.ObjectiveCode,
                })
                .ToList(),
        }).ToList();
    }

    // Phase 18/19.23 — resolve a chosen initiative to its objective + pillar so the
    // linkage chain (stages 3 and 4) can be drawn. Resolved against the unified source.
    private async Task<StrategyInitiativeVm?> ResolveInitiativeAsync(string initiativeCode)
    {
        var ct = HttpContext.RequestAborted;
        var inits = await _source.GetInitiativesAsync(null, ct);
        var init = inits.FirstOrDefault(i => i.Code == initiativeCode);
        // Phase 19.20 (Fix 6) — if the code can't be matched, don't echo the raw
        // code as the name; show a generic Arabic label instead.
        if (init == null) return new StrategyInitiativeVm { Code = initiativeCode, Name = "مبادرتك" };

        string? pillarCode = null, pillarName = null;
        string? objName = null;
        if (!string.IsNullOrEmpty(init.ObjectiveCode))
        {
            var objectives = await _source.GetObjectivesAsync(ct);
            var obj = objectives.FirstOrDefault(o => o.Code == init.ObjectiveCode);
            if (obj != null)
            {
                objName = obj.Name;
                pillarCode = obj.PillarCode;
                if (!string.IsNullOrEmpty(obj.PillarCode))
                {
                    var pillars = await _source.GetPillarsAsync(ct);
                    pillarName = pillars.FirstOrDefault(p => p.Code == obj.PillarCode)?.Name;
                }
            }
        }
        // Phase 20.2 — also collect the projects + KPIs that roll up to this
        // initiative so the contribution card (stage 3) and the journey chain
        // (stage 4) can render an expandable child list. Projects link via
        // InitiativeCode; KPIs link via the initiative's ObjectiveCode.
        var projects = (await _source.GetProjectsAsync(null, ct))
            .Where(p => !string.IsNullOrEmpty(p.InitiativeCode)
                        && string.Equals(p.InitiativeCode, init.Code, StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Code) ? p.Name : p.Code, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(p => new StrategyChildItemVm
            {
                Code = p.Code ?? string.Empty,
                Name = p.Name ?? p.Code ?? string.Empty,
                // Phase 20.11 — hide project status; show only the executing division/department.
                Meta = p.Division,
            })
            .OrderBy(p => p.Code, StringComparer.Ordinal)
            .ToList();

        var kpis = new List<StrategyChildItemVm>();
        if (!string.IsNullOrEmpty(init.ObjectiveCode))
        {
            kpis = (await _source.GetKpisAsync(null, ct))
                .Where(k => string.Equals(k.ObjectiveCode, init.ObjectiveCode, StringComparison.OrdinalIgnoreCase))
                .GroupBy(k => string.IsNullOrWhiteSpace(k.Code) ? k.Name : k.Code, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select(k => new StrategyChildItemVm
                {
                    Code = k.Code ?? string.Empty,
                    Name = k.Name ?? k.Code ?? string.Empty,
                    Meta = string.IsNullOrEmpty(k.Type) ? k.Division : ($"{k.Type}" + (string.IsNullOrEmpty(k.Division) ? "" : $" · {k.Division}")),
                })
                .OrderBy(k => k.Code, StringComparer.Ordinal)
                .ToList();
        }

        return new StrategyInitiativeVm
        {
            Code = init.Code,
            Name = init.Name,
            ObjectiveCode = init.ObjectiveCode,
            ObjectiveName = objName,
            PillarCode = pillarCode,
            PillarName = pillarName,
            Projects = projects,
            Kpis = kpis,
        };
    }

    // GET /Journey/Members/{sessionId} — Phase 13: Members stage removed → redirect to Stage 1 (BigPicture).
    [HttpGet("Journey/Members/{sessionId:guid}")]
    public IActionResult Members(Guid sessionId) => RedirectToRun(sessionId, 1);

    private IActionResult RedirectToRun(Guid sessionId, int stage) =>
        RedirectToAction(nameof(RunStage), new { sessionId, stage });

    // POST /Journey/AddMembers/{sessionId} — Phase 13: the Members stage is gone. The route
    // is kept so stale clients don't 404; it no longer writes members and simply sends the
    // team to Stage 1 (BigPicture).
    [HttpPost("Journey/AddMembers/{sessionId:guid}")]
    [ValidateAntiForgeryToken]
    public IActionResult AddMembers(Guid sessionId) => RedirectToRun(sessionId, 1);

    // POST /Journey/SkipMembers/{sessionId} — Phase 13: kept for stale clients → Stage 1.
    [HttpPost("Journey/SkipMembers/{sessionId:guid}")]
    [ValidateAntiForgeryToken]
    public IActionResult SkipMembers(Guid sessionId) => RedirectToRun(sessionId, 1);

    // GET /Journey/BigPicture/{sessionId} — deep-link alias → Stage 1.
    [HttpGet("Journey/BigPicture/{sessionId:guid}")]
    public IActionResult BigPicture(Guid sessionId) => RedirectToRun(sessionId, 1);

    // GET /Journey/StrategyHouse/{sessionId} — deep-link alias → Stage 2.
    [HttpGet("Journey/StrategyHouse/{sessionId:guid}")]
    public IActionResult StrategyHouse(Guid sessionId) => RedirectToRun(sessionId, 2);

    // GET /Journey/Contribute/{sessionId} — Phase 20.9 deep-link alias → Stage 4
    // (Contribute shifted from 3 to 4 when the new GAC Goals stage was inserted).
    [HttpGet("Journey/Contribute/{sessionId:guid}")]
    public IActionResult Contribute(Guid sessionId) => RedirectToRun(sessionId, 4);

    // GET /Journey/GacGoals/{sessionId} — Phase 20.9 deep-link alias for the new
    // read-only Stage 3 (الأهداف الاستراتيجية للهيئة).
    [HttpGet("Journey/GacGoals/{sessionId:guid}")]
    public IActionResult GacGoals(Guid sessionId) => RedirectToRun(sessionId, 3);

    // POST /Journey/SavePledge — JSON endpoint.
    [HttpPost("Journey/SavePledge")]
    public async Task<IActionResult> SavePledge([FromBody] PledgeDto dto)
    {
        var session = await _db.StrategySessions.FindAsync(dto.SessionId);
        if (session == null) return NotFound();
        var pledge = new ContributionPledge
        {
            SessionId = dto.SessionId,
            DeptCode = session.DeptCode,
            ElementType = dto.ElementType,
            ElementCode = dto.ElementCode,
            ContributionKind = dto.ContributionKind,
            Notes = dto.Notes,
        };
        _db.ContributionPledges.Add(pledge);
        await _db.SaveChangesAsync();
        return Json(new { ok = true, id = pledge.Id });
    }

    // POST /Journey/RemovePledge — JSON endpoint; removes a pledge by element or id.
    [HttpPost("Journey/RemovePledge")]
    public async Task<IActionResult> RemovePledge([FromBody] RemovePledgeDto dto)
    {
        IQueryable<ContributionPledge> q = _db.ContributionPledges.Where(p => p.SessionId == dto.SessionId);
        if (dto.Id is Guid id) q = q.Where(p => p.Id == id);
        else q = q.Where(p => p.ElementType == dto.ElementType && p.ElementCode == dto.ElementCode);

        var matches = await q.ToListAsync();
        if (matches.Count == 0) return Json(new { ok = true, removed = 0 });
        _db.ContributionPledges.RemoveRange(matches);
        await _db.SaveChangesAsync();
        return Json(new { ok = true, removed = matches.Count });
    }

    // POST /Journey/SaveTeamValue — Phase 16: the team's chosen Big Picture value.
    // Optional; one active selection per session (latest wins). JSON endpoint.
    [HttpPost("Journey/SaveTeamValue")]
    public async Task<IActionResult> SaveTeamValue([FromBody] TeamValueDto dto)
    {
        var session = await _db.StrategySessions.FindAsync(dto.SessionId);
        if (session == null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.ValueText)) return BadRequest();

        var existing = await _db.TeamValueSelections
            .Where(t => t.SessionId == dto.SessionId)
            .ToListAsync();
        if (existing.Count > 0) _db.TeamValueSelections.RemoveRange(existing);

        var selection = new TeamValueSelection
        {
            SessionId = dto.SessionId,
            JourneyCode = session.DeptCode,
            SelectedValueKey = string.IsNullOrWhiteSpace(dto.ValueKey) ? dto.ValueText.Trim() : dto.ValueKey.Trim(),
            SelectedValueText = dto.ValueText.Trim(),
        };
        _db.TeamValueSelections.Add(selection);
        await _db.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // POST /Journey/SaveReflection — Phase 18 stage 1 opening reflection (optional free text).
    // Latest answer per session wins; empty text clears the stored answer. JSON endpoint.
    [HttpPost("Journey/SaveReflection")]
    public async Task<IActionResult> SaveReflection([FromBody] ReflectionDto dto)
    {
        var session = await _db.StrategySessions.FindAsync(dto.SessionId);
        if (session == null) return NotFound();

        var existing = await _db.OpeningReflections.Where(r => r.SessionId == dto.SessionId).ToListAsync();
        if (existing.Count > 0) _db.OpeningReflections.RemoveRange(existing);

        var text = string.IsNullOrWhiteSpace(dto.ReflectionText) ? null : dto.ReflectionText.Trim();
        if (text != null && text.Length > 4000) text = text[..4000];
        if (text != null)
        {
            _db.OpeningReflections.Add(new OpeningReflection
            {
                SessionId = dto.SessionId,
                JourneyCode = session.DeptCode,
                DepartmentCode = session.DeptCode,
                ReflectionText = text,
            });
        }
        await _db.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // GET /Journey/InitiativesForDepartment?division=... — Phase 18 stage 3.
    // Returns the initiatives owned by / running in a department, with their resolved
    // objective + pillar so the client can render the linkage chain. External when
    // live; otherwise a small preview set so the dev screen is never empty.
    [HttpGet("Journey/InitiativesForDepartment")]
    public async Task<IActionResult> InitiativesForDepartment(string? division)
    {
        division = (division ?? string.Empty).Trim();
        // Phase 19.20 (Fix 4) — an empty department is a valid "no selection" case:
        // return an empty list (HTTP 200), never an error.
        if (division.Length == 0) return Json(new { ok = true, live = true, initiatives = Array.Empty<object>() });

        // Phase 19.20 (Fix 4) / 19.23 — initiatives + projects come from the unified
        // source (mirror → SQLite → empty). The whole resolution is wrapped so a data
        // failure returns an empty list + a friendly flag with HTTP 200 instead of a red
        // error message.
        try
        {
            var ct = HttpContext.RequestAborted;
            var inits = await _source.GetInitiativesAsync(null, ct);

            // Phase 20.22 — unified path for ALL user types. "الهيئة الشاملة" is
            // handled in-line (owner candidates expand to every active dept) so the
            // new A/B classification + owned-project filtering apply uniformly.
            //   employee → { their dept name }
            //   VP       → sector name + all child dept names
            //   admin    → every active department name + literal "الهيئة الشاملة"
            List<string> ownerDeptNames;
            bool isAuthorityWide = string.Equals(division, "الهيئة الشاملة", StringComparison.Ordinal);
            if (isAuthorityWide)
            {
                ownerDeptNames = await _db.Departments
                    .Where(d => d.IsActive)
                    .Select(d => d.NameAr)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!)
                    .Distinct()
                    .ToListAsync();
            }
            else
            {
                var sectorDeptNames = await _db.Departments
                    .Where(d => d.ParentSector == division && d.IsActive)
                    .Select(d => d.NameAr)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!)
                    .Distinct()
                    .ToListAsync();
                ownerDeptNames = sectorDeptNames.Count > 0
                    ? sectorDeptNames
                    : new List<string> { division };
            }

            // Aggregate the projects reachable by this user across all their dept
            // names; this is the single in-scope project set used for both the
            // initiative match AND the per-card project filter below.
            var scopeProjects = new List<StrategyHouse.Web.Services.Dtos.StrategyProjectDto>();
            foreach (var name in ownerDeptNames)
                scopeProjects.AddRange(await _source.GetProjectsAsync(name, ct));
            var scopeProjectCodes = scopeProjects
                .Where(p => !string.IsNullOrEmpty(p.Code))
                .Select(p => p.Code!.ToLowerInvariant())
                .ToHashSet();
            var scopeProjectInitCodes = scopeProjects
                .Where(p => !string.IsNullOrEmpty(p.InitiativeCode))
                .Select(p => p.InitiativeCode!)
                .ToHashSet();

            var ownerCandidates = ownerDeptNames
                .Concat(isAuthorityWide ? new[] { "الهيئة الشاملة" } : new[] { division })
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.ToLowerInvariant())
                .ToHashSet();

            // Match initiatives + classify each as "owner" (A) or "project" (B).
            //   owner   → Owners string contains any of the user's reachable dept names
            //   project → not an owner, but at least one user-scoped project rolls up to it
            var matched = inits
                .Select(i => new
                {
                    init = i,
                    ownerMatch = !string.IsNullOrEmpty(i.Owners)
                                 && ownerCandidates.Any(o => i.Owners!.ToLowerInvariant().Contains(o)),
                    projectMatch = !string.IsNullOrEmpty(i.Code)
                                   && scopeProjectInitCodes.Contains(i.Code),
                })
                .Where(x => x.ownerMatch || x.projectMatch)
                .GroupBy(x => string.IsNullOrWhiteSpace(x.init.Code) ? x.init.Name : x.init.Code,
                         StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.init.Code, StringComparer.Ordinal)
                .ToList();

            var result = new List<object>();
            foreach (var m in matched)
            {
                var vm = await ResolveInitiativeAsync(m.init.Code);
                if (vm == null) continue;
                // Phase 20.22 — per-card project list is now filtered to the user's
                // own scope. Counter shows scoped count, NOT full initiative count.
                var ownedProjects = vm.Projects
                    .Where(p => !string.IsNullOrEmpty(p.Code)
                                && scopeProjectCodes.Contains(p.Code.ToLowerInvariant()))
                    .ToList();
                var matchKind = m.ownerMatch ? "owner" : "project";
                result.Add(new
                {
                    code = vm.Code,
                    name = vm.Name,
                    objectiveName = vm.ObjectiveName,
                    pillarName = vm.PillarName,
                    // Phase 20.22 — new fields: A/B classification + owned-project count.
                    matchKind,
                    ownedProjectCount = ownedProjects.Count,
                    // Phase 20.22 — only show projects the user actually owns.
                    projects = ownedProjects.Select(p => new { code = p.Code, name = p.Name, meta = p.Meta }).ToList(),
                    kpis = vm.Kpis.Select(k => new { code = k.Code, name = k.Name, meta = k.Meta }).ToList(),
                });
            }
            return Json(new { ok = true, live = true, initiatives = result });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "InitiativesForDepartment failed for division {Division}; returning empty list.", division);
            return Json(new { ok = true, live = false, initiatives = Array.Empty<object>(), error = "تعذّر تحميل المبادرات." });
        }
    }

    // POST /Journey/SaveRoleContribution — Phase 18 stage 3. Stores the chosen
    // initiative + the employee's perceived impact (free text). Latest per session wins.
    [HttpPost("Journey/SaveRoleContribution")]
    public async Task<IActionResult> SaveRoleContribution([FromBody] RoleContributionDto dto)
    {
        var session = await _db.StrategySessions.FindAsync(dto.SessionId);
        if (session == null) return NotFound();

        var existing = await _db.RoleContributions.Where(r => r.SessionId == dto.SessionId).ToListAsync();
        if (existing.Count > 0) _db.RoleContributions.RemoveRange(existing);

        var impact = string.IsNullOrWhiteSpace(dto.PerceivedImpact) ? null : dto.PerceivedImpact.Trim();
        if (impact != null && impact.Length > 4000) impact = impact[..4000];
        var initCode = string.IsNullOrWhiteSpace(dto.SelectedInitiativeCode) ? null : dto.SelectedInitiativeCode.Trim();

        _db.RoleContributions.Add(new RoleContribution
        {
            SessionId = dto.SessionId,
            JourneyCode = session.DeptCode,
            DepartmentCode = string.IsNullOrWhiteSpace(dto.DepartmentCode) ? session.DeptCode : dto.DepartmentCode.Trim(),
            SelectedInitiativeCode = initCode,
            PerceivedImpact = impact,
        });
        await _db.SaveChangesAsync();

        var vm = initCode != null ? await ResolveInitiativeAsync(initCode) : null;
        return Json(new
        {
            ok = true,
            initiativeName = vm?.Name,
            objectiveName = vm?.ObjectiveName,
            pillarName = vm?.PillarName,
        });
    }

    // GET /api/strategy/sankey — Phase 19. Strategy flow for the interactive Sankey
    // diagram (stage 2, "تدفق تفاعلي" tab): Pillars → Objectives → Initiatives →
    // Projects. Sourced from the external MSSQL warehouse (Phase 17) when
    // UseExternalDb is on; otherwise a 3×3×3×3 dummy graph so the preview is never
    // empty. Each node carries a category so the client can colour the layers.
    [HttpGet("api/strategy/sankey")]
    public async Task<IActionResult> StrategySankey()
    {
        // Phase 19.20 (Fix 3) — never let this endpoint 500. On any failure (bad data,
        // dropped DB connection, etc.) return a valid empty graph with HTTP 200 so the
        // client can distinguish "no data to show" from an actual transport error.
        try
        {
            return Json(await _strategyData.GetSankeyDataAsync(HttpContext.RequestAborted));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sankey endpoint failed; returning empty graph.");
            return Json(new { ok = true, live = false, source = "error", empty = false, warning = (string?)null, nodes = Array.Empty<object>(), links = Array.Empty<object>() });
        }
    }

    // GET /Journey/Map/{sessionId} — Phase 20.9 deep-link alias → Stage 5
    // (Map shifted from 4 to 5 when the new GAC Goals stage was inserted).
    [HttpGet("Journey/Map/{sessionId:guid}")]
    public IActionResult Map(Guid sessionId) => RedirectToRun(sessionId, 5);

    // POST /Journey/SaveAttendeeCount — Phase 18: the optional attendee count moved to
    // stage 5 (الأثر). One value per session. Redirects back to stage 5.
    [HttpPost("Journey/SaveAttendeeCount/{sessionId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAttendeeCount(Guid sessionId, int? attendeeCount)
    {
        var session = await _db.StrategySessions.FindAsync(sessionId);
        if (session == null) return NotFound();
        // Phase 19.17 — the attendee count is truly optional. An empty submission
        // (null) or an out-of-range value is a gentle no-op: no red error, no
        // blocked navigation. Only a valid 1..500 value is persisted.
        if (attendeeCount.HasValue && attendeeCount.Value >= 1 && attendeeCount.Value <= 500)
        {
            session.AttendeeCount = attendeeCount.Value;
            session.LastActivityAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Saved"] = "تم حفظ عدد الحاضرين.";
        }
        else
        {
            TempData["Saved"] = "تخطّيت حفظ العدد (حقل اختياري).";
        }
        return RedirectToRun(sessionId, 5);
    }

    // POST /Journey/SaveGroupSignature — Phase 13: one group signature per session,
    // replacing the per-member signature pads. Accepts a hand-drawn PNG and/or a typed
    // comment (at least one required). Stored as a MapInkAsset with MemberId = NULL.
    // A previous active group signature for this session's map is deactivated first.
    [HttpPost("Journey/SaveGroupSignature")]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> SaveGroupSignature([FromBody] GroupSignatureDto dto)
    {
        var session = await _db.StrategySessions.FindAsync(dto.SessionId);
        if (session == null) return NotFound();

        var map = await _db.DepartmentStrategyMaps.FirstOrDefaultAsync(m => m.SessionId == dto.SessionId);
        if (map == null)
        {
            map = new DepartmentStrategyMap { SessionId = dto.SessionId, DeptCode = session.DeptCode };
            _db.DepartmentStrategyMaps.Add(map);
            await _db.SaveChangesAsync();
        }
        if (map.SignedAt != null) return BadRequest(new { ok = false, error = "locked" });

        var png = DecodePng(dto.PngBase64);
        var typedText = string.IsNullOrWhiteSpace(dto.TypedText) ? null : dto.TypedText.Trim();
        if (typedText != null && typedText.Length > 2000) typedText = typedText[..2000];
        // A group signature counts if it has ink OR a typed comment.
        if (png == null && typedText == null) return BadRequest(new { ok = false, error = "empty" });

        // Deactivate any prior active group signature (MemberId == null) for this map.
        var prior = await _db.MapInkAssets
            .Where(a => a.MapId == map.Id && a.AssetKind == "signature" && a.MemberId == null && a.IsActive)
            .ToListAsync();
        foreach (var p in prior) p.IsActive = false;

        var now = DateTime.UtcNow;
        var asset = new MapInkAsset
        {
            MapId = map.Id,
            AssetKind = "signature",
            PngBlob = png,
            StrokesJson = dto.StrokesJson,
            TypedText = typedText,
            AuthorName = "الفريق",
            MemberId = null, // Phase 13 — group signature is not tied to an individual member.
            ModerationStatus = "Approved",
            ModeratedAt = now,
            ModeratedBy = "system:group-sig",
            IsActive = true,
        };
        _db.MapInkAssets.Add(asset);
        session.LastActivityAt = now;
        _db.ModerationAuditLogs.Add(new ModerationAuditLog
        {
            TargetType = "MapInkAsset",
            TargetId = asset.Id,
            Action = "Approve",
            ActorUserId = "system:group-sig",
            Note = "auto-approved group signature on capture",
        });
        await _db.SaveChangesAsync();
        return Json(new { ok = true, assetId = asset.Id });
    }

    // GET /Journey/Ink/{assetId}?sessionId=... — streams a captured ink/signature PNG (for in-journey preview).
    // Phase 20.8 — the asset's parent map must belong to the supplied session, otherwise we 403.
    [HttpGet("Journey/Ink/{assetId:guid}")]
    public async Task<IActionResult> Ink(Guid assetId, [FromQuery] Guid sessionId)
    {
        var asset = await _db.MapInkAssets.FindAsync(assetId);
        if (asset?.PngBlob == null) return NotFound();
        var map = await _db.DepartmentStrategyMaps.FirstOrDefaultAsync(m => m.Id == asset.MapId);
        if (map == null || map.SessionId != sessionId) return Forbid();
        return File(asset.PngBlob, "image/png");
    }

    // POST /Journey/SaveMap — saves layout JSON + texts.
    [HttpPost("Journey/SaveMap/{sessionId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveMap(Guid sessionId, string? mapLayoutJson, string? opinions, string? commitments)
    {
        var session = await _db.StrategySessions.FindAsync(sessionId);
        if (session == null) return NotFound();
        var map = await _db.DepartmentStrategyMaps.FirstOrDefaultAsync(m => m.SessionId == sessionId);
        if (map == null)
        {
            map = new DepartmentStrategyMap { SessionId = sessionId, DeptCode = session.DeptCode };
            _db.DepartmentStrategyMaps.Add(map);
        }
        if (map.SignedAt != null)
        {
            TempData["Error"] = "الخريطة موقّعة ومقفلة.";
            return RedirectToRun(sessionId, 4);
        }
        map.MapLayoutJson = mapLayoutJson;
        map.OpinionsText = opinions;
        map.CommitmentsText = commitments;
        await _db.SaveChangesAsync();
        TempData["Saved"] = "تم حفظ الخريطة.";
        return RedirectToRun(sessionId, 4);
    }

    // POST /Journey/SignMap — lock map, generate PDF.
    [HttpPost("Journey/SignMap/{sessionId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SignMap(Guid sessionId, string[] memberIds, string[] signatures)
    {
        var session = await _db.StrategySessions
            .Include(s => s.Members)
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null) return NotFound();

        var map = await _db.DepartmentStrategyMaps.FirstOrDefaultAsync(m => m.SessionId == sessionId);
        if (map == null)
        {
            map = new DepartmentStrategyMap { SessionId = sessionId, DeptCode = session.DeptCode };
            _db.DepartmentStrategyMaps.Add(map);
        }

        var now = DateTime.UtcNow;
        for (var i = 0; i < memberIds.Length; i++)
        {
            if (!Guid.TryParse(memberIds[i], out var mid)) continue;
            var member = session.Members.FirstOrDefault(m => m.Id == mid);
            if (member == null || i >= signatures.Length || string.IsNullOrWhiteSpace(signatures[i])) continue;
            member.TypedSignature = signatures[i].Trim();
            member.SignedAt = now;
        }

        map.SignedAt = now;
        session.SignedAt = now;
        session.Status = "Signed";
        await _db.SaveChangesAsync();

        // Generate PDF.
        var dept = await _db.Departments.FindAsync(session.DeptCode) ?? new Department { DeptCode = session.DeptCode };
        var pledges = await _db.ContributionPledges.Where(p => p.SessionId == sessionId).ToListAsync();
        // Phase 19.20 (Fix 2/6) — dedup the strategy lists so the PDF resolves codes to a
        // single, correct Arabic name and never duplicates entries.
        // Phase 19.23 — strategy lists come from the unified source (mirror → SQLite → empty).
        var ct = HttpContext.RequestAborted;
        var pillars = StrategyDedup.ByPillarCode(ToPillarEntities(await _source.GetPillarsAsync(ct)));
        var kpis = ToKpiEntities(await _source.GetKpisAsync(session.DeptCode, ct));
        var projects = ToProjectEntities(await _source.GetProjectsAsync(session.DeptCode, ct));
        var inkAssets = await _db.MapInkAssets.Where(a => a.MapId == map.Id).ToListAsync();
        var objectives = StrategyDedup.ByObjectiveCode(ToObjectiveEntities(await _source.GetObjectivesAsync(ct)));
        var initiatives = StrategyDedup.ByInitiativeCode(ToInitiativeEntities(await _source.GetInitiativesAsync(null, ct)));
        try
        {
            map.PdfBlob = _pdf.Generate(map, dept, session.Members.ToList(), pledges, pillars, kpis, projects, inkAssets, objectives, initiatives);
            await _db.SaveChangesAsync();
        }
        catch
        {
            // PDF generation failure must not block the journey; map stays signed.
        }

        return RedirectToAction(nameof(Complete), new { sessionId });
    }

    // POST /Journey/SaveInk — Phase 3 handwriting capture for a map text section.
    [HttpPost("Journey/SaveInk")]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> SaveInk([FromBody] InkDto dto)
    {
        var session = await _db.StrategySessions.FindAsync(dto.SessionId);
        if (session == null) return NotFound();

        var map = await _db.DepartmentStrategyMaps.FirstOrDefaultAsync(m => m.SessionId == dto.SessionId);
        if (map == null)
        {
            map = new DepartmentStrategyMap { SessionId = dto.SessionId, DeptCode = session.DeptCode };
            _db.DepartmentStrategyMaps.Add(map);
            await _db.SaveChangesAsync();
        }
        if (map.SignedAt != null) return BadRequest(new { ok = false, error = "locked" });

        var kind = (dto.AssetKind ?? "").Trim().ToLowerInvariant();
        if (kind is not ("opinion" or "commitment"))
            return BadRequest(new { ok = false, error = "kind" });

        var png = DecodePng(dto.PngBase64);
        if (png == null) return BadRequest(new { ok = false, error = "png" });

        var asset = new MapInkAsset
        {
            MapId = map.Id,
            AssetKind = kind,
            PngBlob = png,
            StrokesJson = dto.StrokesJson,
            ModerationStatus = "Pending",
        };
        _db.MapInkAssets.Add(asset);
        await _db.SaveChangesAsync();
        return Json(new { ok = true, assetId = asset.Id });
    }

    // POST /Journey/SaveSignature — Phase 3 pencil signature for a session member.
    [HttpPost("Journey/SaveSignature")]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> SaveSignature([FromBody] SignatureDto dto)
    {
        var member = await _db.SessionMembers.FindAsync(dto.MemberId);
        if (member == null) return NotFound();
        var session = await _db.StrategySessions.FindAsync(member.SessionId);
        if (session == null) return NotFound();

        var map = await _db.DepartmentStrategyMaps.FirstOrDefaultAsync(m => m.SessionId == member.SessionId);
        if (map == null)
        {
            map = new DepartmentStrategyMap { SessionId = member.SessionId, DeptCode = session.DeptCode };
            _db.DepartmentStrategyMaps.Add(map);
            await _db.SaveChangesAsync();
        }
        if (map.SignedAt != null) return BadRequest(new { ok = false, error = "locked" });

        var png = DecodePng(dto.PngBase64);
        var typedText = string.IsNullOrWhiteSpace(dto.TypedText) ? null : dto.TypedText.Trim();
        // A row counts as signed if it has ink OR typed text (Phase 10.1 — dual signing).
        if (png == null && typedText == null) return BadRequest(new { ok = false, error = "empty" });

        var now = DateTime.UtcNow;
        var asset = new MapInkAsset
        {
            MapId = map.Id,
            AssetKind = "signature",
            PngBlob = png,
            StrokesJson = dto.StrokesJson,
            TypedText = typedText,
            AuthorName = member.NameAr,
            MemberId = member.Id,
            // Signatures are the member's own work — auto-approve so they appear in the PDF.
            ModerationStatus = "Approved",
            ModeratedAt = now,
            ModeratedBy = "system:auto-sig",
            IsActive = true,
        };
        _db.MapInkAssets.Add(asset);
        member.SignedAt = now;
        if (typedText != null) member.TypedSignature = typedText;
        _db.ModerationAuditLogs.Add(new ModerationAuditLog
        {
            TargetType = "MapInkAsset",
            TargetId = asset.Id,
            Action = "Approve",
            ActorUserId = "system:auto-sig",
            Note = "auto-approved signature on capture",
        });
        await _db.SaveChangesAsync();
        return Json(new { ok = true, assetId = asset.Id });
    }

    private static byte[]? DecodePng(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl)) return null;
        var idx = dataUrl.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        var b64 = idx >= 0 ? dataUrl[(idx + 7)..] : dataUrl;
        try { return Convert.FromBase64String(b64); }
        catch { return null; }
    }

    // GET /Journey/Complete/{sessionId}
    [HttpGet("Journey/Complete/{sessionId:guid}")]
    public async Task<IActionResult> Complete(Guid sessionId)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null) return NotFound();
        if (session.CompletedAt == null)
        {
            var tracked = await _db.StrategySessions.FindAsync(sessionId);
            tracked!.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        ViewBag.Dept = await _db.Departments.FindAsync(session.DeptCode);
        ViewBag.Map = await _db.DepartmentStrategyMaps.FirstOrDefaultAsync(m => m.SessionId == sessionId);
        return View(session);
    }

    // GET /Journey/Pdf/{sessionId} — download the generated map PDF.
    [HttpGet("Journey/Pdf/{sessionId:guid}")]
    public async Task<IActionResult> Pdf(Guid sessionId)
    {
        var map = await _db.DepartmentStrategyMaps.FirstOrDefaultAsync(m => m.SessionId == sessionId);
        if (map?.PdfBlob == null) return NotFound();
        return File(map.PdfBlob, "application/pdf", $"strategy-map-{map.DeptCode}.pdf");
    }

    private async Task<StrategySession?> LoadSessionAsync(Guid id) =>
        await _db.StrategySessions.AsNoTracking().Include(s => s.Members)
            .FirstOrDefaultAsync(s => s.Id == id);

    // Prefixes a KPI label with its type in Arabic; unknown types render the name as-is.
    public static string KpiLabel(Kpi k)
    {
        var name = k.KpiName ?? k.KpiCode;
        var type = (k.KpiType ?? string.Empty).Trim();
        if (type is "استراتيجي" or "Strategic")
            return "مؤشر استراتيجي: " + name;
        if (type is "تشغيلي" or "Operational")
            return "مؤشر تشغيلي: " + name;
        return name;
    }

    // Builds nodes/links for the dept's contribution chain:
    // KPIs/Projects → Objectives → Pillars → Vision.
    private async Task<object> BuildSankeyAsync(string deptCode)
    {
        // Phase 19.23 — Sankey reads route through the unified source (mirror → SQLite → empty).
        // Phase 20.16 — for synthetic sector sessions (SEC_CORP_SUPP / SEC_ECONOMIC /
        // SEC_LEGAL / SEC_ALL) aggregate across the constituent departments so the
        // Sankey shows the full sector / executive view, not zero nodes.
        var ct = HttpContext.RequestAborted;
        List<Kpi> kpis;
        List<Project> projects;
        var sectorDisplay = SectorDisplayName(deptCode);
        if (sectorDisplay != null)
        {
            var sectorDeptsQuery = _db.Departments.Where(d => d.IsActive);
            if (deptCode != "SEC_ALL")
            {
                sectorDeptsQuery = sectorDeptsQuery.Where(d => d.ParentSector == sectorDisplay);
            }
            var sectorDepts = await sectorDeptsQuery
                .Select(d => d.NameAr)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct()
                .ToListAsync();
            var aggK = new List<Kpi>();
            var aggP = new List<Project>();
            foreach (var dn in sectorDepts)
            {
                aggK.AddRange(ToKpiEntities(await _source.GetKpisAsync(dn, ct)));
                aggP.AddRange(ToProjectEntities(await _source.GetProjectsAsync(dn, ct)));
            }
            kpis = aggK
                .GroupBy(k => string.IsNullOrWhiteSpace(k.KpiCode) ? k.KpiName : k.KpiCode, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()).ToList();
            projects = aggP
                .GroupBy(p => string.IsNullOrWhiteSpace(p.ProjectCode) ? p.ProjectName : p.ProjectCode, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()).ToList();
        }
        else
        {
            kpis = ToKpiEntities(await _source.GetKpisAsync(deptCode, ct));
            projects = ToProjectEntities(await _source.GetProjectsAsync(deptCode, ct));
        }
        // Phase 19.20 (Fix 2) — dedup so the flow chart doesn't draw duplicate nodes.
        var objectives = StrategyDedup.ByObjectiveCode(ToObjectiveEntities(await _source.GetObjectivesAsync(ct)));
        var pillars = StrategyDedup.ByPillarCode(ToPillarEntities(await _source.GetPillarsAsync(ct)));
        var initiatives = StrategyDedup.ByInitiativeCode(ToInitiativeEntities(await _source.GetInitiativesAsync(null, ct)));

        var nodes = new List<object>();
        var nodeIndex = new Dictionary<string, int>();
        var links = new List<object>();

        int AddNode(string key, string label, string layer)
        {
            if (nodeIndex.TryGetValue(key, out var existing)) return existing;
            var idx = nodes.Count;
            nodeIndex[key] = idx;
            nodes.Add(new { name = label, layer });
            return idx;
        }

        var visionIdx = AddNode("VISION", "الرؤية", "vision");

        // Pillars → Vision
        foreach (var p in pillars)
        {
            var pi = AddNode("P:" + p.PlrCode, p.PillarName ?? p.PlrCode, "pillar");
            links.Add(new { source = pi, target = visionIdx, value = 1 });
        }

        // KPIs → Objective → Pillar
        foreach (var k in kpis.Take(15))
        {
            var ki = AddNode("K:" + k.KpiCode, KpiLabel(k), "kpi");
            var obj = objectives.FirstOrDefault(o => o.ObjectiveCode == k.ObjectiveCode);
            if (obj != null)
            {
                var oi = AddNode("O:" + obj.ObjectiveCode, obj.ObjectiveName ?? obj.ObjectiveCode, "objective");
                links.Add(new { source = ki, target = oi, value = 1 });
                if (obj.PlrCode != null && nodeIndex.TryGetValue("P:" + obj.PlrCode, out var pidx))
                    links.Add(new { source = oi, target = pidx, value = 1 });
            }
            else if (k.PlrCode != null && nodeIndex.TryGetValue("P:" + k.PlrCode, out var pidx))
            {
                links.Add(new { source = ki, target = pidx, value = 1 });
            }
        }

        // Projects → Initiative → Objective → Pillar
        foreach (var pr in projects.Take(15))
        {
            var pri = AddNode("PR:" + pr.ProjectCode, pr.ProjectName ?? pr.ProjectCode, "project");
            var init = initiatives.FirstOrDefault(i => i.InitiativeCode == pr.InitiativeCode);
            if (init != null)
            {
                var ii = AddNode("I:" + init.InitiativeCode, init.InitiativeName ?? init.InitiativeCode, "initiative");
                links.Add(new { source = pri, target = ii, value = 1 });
                var obj = objectives.FirstOrDefault(o => o.ObjectiveCode == init.ObjectiveCode);
                if (obj != null)
                {
                    var oi = AddNode("O:" + obj.ObjectiveCode, obj.ObjectiveName ?? obj.ObjectiveCode, "objective");
                    links.Add(new { source = ii, target = oi, value = 1 });
                    if (obj.PlrCode != null && nodeIndex.TryGetValue("P:" + obj.PlrCode, out var pidx))
                        links.Add(new { source = oi, target = pidx, value = 1 });
                }
            }
            else if (pr.PlrCode != null && nodeIndex.TryGetValue("P:" + pr.PlrCode, out var pidx))
            {
                links.Add(new { source = pri, target = pidx, value = 1 });
            }
        }

        return new { nodes, links };
    }
}

// Named view model for the single-page journey (Razor dynamic boundary rejects anonymous types).
public class JourneyRunViewModel
{
    public StrategySession Session { get; set; } = null!;
    public Department? Dept { get; set; }
    public StrategyContentService Content { get; set; } = null!;
    public object Sankey { get; set; } = new { };
    public List<Pillar> Pillars { get; set; } = new();
    public List<Objective> Objectives { get; set; } = new();
    public List<string?> DeptObjectiveCodes { get; set; } = new();
    public List<Initiative> Initiatives { get; set; } = new();
    public List<DepartmentRoster> Roster { get; set; } = new();
    public List<ContributionPledge> Pledges { get; set; } = new();
    public List<Kpi> Kpis { get; set; } = new();
    public List<Project> Projects { get; set; } = new();
    // Phase 20.11 — stage 5 picker needs the full project + initiative lists
    // (across all departments) so a chosen initiative can show every linked
    // project regardless of the current user's department.
    public List<Project> AllProjects { get; set; } = new();
    public List<Initiative> AllInitiatives { get; set; } = new();

    // Phase 20.22 — Stage 3 ONLY: full unfiltered lists kept for backwards
    // compatibility / admin views. Phase 20.23 introduces per-user filtering on
    // top of these via Stage3SeesAllObjectives + Stage3ObjectiveCodes below.
    public List<Initiative> AllInitiativesForStage3 { get; set; } = new();
    public List<Kpi> AllKpisForStage3 { get; set; } = new();

    // Phase 20.23 — Stage 3 per-user scope. SEC_ALL (admin/ChiefStrategy/
    // الهيئة الشاملة) sees every objective; everyone else only sees the
    // objectives their dept/sector touches via KPIs or initiatives.
    public bool Stage3SeesAllObjectives { get; set; }
    public List<string> Stage3ObjectiveCodes { get; set; } = new();
    public List<Initiative> ScopedInitiativesForStage3 { get; set; } = new();
    public List<Kpi> ScopedKpisForStage3 { get; set; } = new();

    // Phase 20.22 — Stage 5: project + initiative lists scoped to the user's
    // actual reach (employee = dept, VP = sector aggregate, gac.admin = all).
    // Replaces AllProjects/AllInitiatives for the stage-5 pickers so a regular
    // user only sees their own initiatives + projects in the journey diagram.
    public List<Project> ScopedProjects { get; set; } = new();
    public List<Initiative> ScopedInitiatives { get; set; } = new();
    public DepartmentStrategyMap? Map { get; set; }
    public List<MapInkAsset> InkAssets { get; set; } = new();
    public string? SelectedTeamValue { get; set; } // Phase 16 — the team's chosen Big Picture value (Ar text)

    // Phase 18 — redesigned-journey data.
    // True when the external MSSQL warehouse is the source for the strategy house
    // (stage 2) and role data (stage 3). False → the views show preview/dummy data
    // and the "البيانات الاستراتيجية ستتوفر عند الربط" notice.
    public bool StrategyDataLive { get; set; }
    public List<StrategyPillarVm> HousePillars { get; set; } = new(); // stage 2 — vision → pillars → objectives
    public List<string> DepartmentNames { get; set; } = new();        // stage 3 — dropdown options
    public string? OpeningReflectionText { get; set; }                // stage 1 — saved reflection (if any)
    public RoleContribution? RoleContribution { get; set; }           // stage 3/4 — saved role contribution (if any)
    public StrategyInitiativeVm? SelectedInitiative { get; set; }     // stage 4 — resolved linkage for the chosen initiative
}

// Phase 18 — flattened strategy element shapes the journey views bind to. They are
// populated either from the external warehouse (Phase 17 services) or from the
// dev preview/dummy data, so the views never touch the External* entity types.
public class StrategyPillarVm
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<StrategyObjectiveVm> Objectives { get; set; } = new();
}

public class StrategyObjectiveVm
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    // Phase 20.10 — expandable tree: initiatives + projects nested per objective
    public List<StrategyObjectiveInitiativeVm> Initiatives { get; set; } = new();
}

public class StrategyObjectiveInitiativeVm
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<StrategyChildItemVm> Projects { get; set; } = new();
}

public class StrategyInitiativeVm
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ObjectiveCode { get; set; }
    public string? ObjectiveName { get; set; }
    public string? PillarCode { get; set; }
    public string? PillarName { get; set; }
    // Phase 20.2 — expose projects + KPIs tied to this initiative so the
    // contribution card (stage 3) and journey chain (stage 4) can render them
    // under an expandable section.
    public List<StrategyChildItemVm> Projects { get; set; } = new();
    public List<StrategyChildItemVm> Kpis { get; set; } = new();
}

public class StrategyChildItemVm
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Meta { get; set; }
}

public class PledgeDto
{
    public Guid SessionId { get; set; }
    public string ElementType { get; set; } = string.Empty;
    public string ElementCode { get; set; } = string.Empty;
    public string? ContributionKind { get; set; }
    [MaxLength(1000)]
    public string? Notes { get; set; }
}

public class RemovePledgeDto
{
    public Guid SessionId { get; set; }
    public Guid? Id { get; set; }
    public string? ElementType { get; set; }
    public string? ElementCode { get; set; }
}

public class TeamValueDto
{
    public Guid SessionId { get; set; }
    [MaxLength(200)]
    public string? ValueKey { get; set; }
    [MaxLength(200)]
    public string ValueText { get; set; } = string.Empty;
}

public class InkDto
{
    public Guid SessionId { get; set; }
    public Guid MapId { get; set; }
    public string? AssetKind { get; set; }
    public string? PngBase64 { get; set; }
    public string? StrokesJson { get; set; }
}

public class SignatureDto
{
    public Guid MemberId { get; set; }
    public string? PngBase64 { get; set; }
    public string? StrokesJson { get; set; }
    public string? TypedText { get; set; }
}

// Phase 13 — one group signature per session (hand drawing and/or typed comment).
public class GroupSignatureDto
{
    public Guid SessionId { get; set; }
    public string? PngBase64 { get; set; }
    public string? StrokesJson { get; set; }
    public string? TypedText { get; set; }
}

// Phase 18 — opening reflection (stage 1) and role contribution (stage 3).
public class ReflectionDto
{
    public Guid SessionId { get; set; }
    [MaxLength(2000)]
    public string? ReflectionText { get; set; }
}

public class RoleContributionDto
{
    public Guid SessionId { get; set; }
    public string? DepartmentCode { get; set; }
    public string? SelectedInitiativeCode { get; set; }
    public string? PerceivedImpact { get; set; }
}
