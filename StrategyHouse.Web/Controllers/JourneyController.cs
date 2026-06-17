using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Anonymous, access-code-gated department journey flow.
[AllowAnonymous]
public class JourneyController : Controller
{
    // Phase 13 — the Members stage was removed; the journey is now 5 stages:
    // 1 BigPicture · 2 StrategyHouse · 3 Contribute · 4 Map (group signature + attendee count) · 5 Complete.
    private const int MaxStage = 5;

    private readonly ApplicationDbContext _db;
    private readonly StrategyContentService _content;
    private readonly StrategyMapPdfService _pdf;
    private readonly PillarsService _pillarsSvc;
    private readonly ObjectivesService _objectivesSvc;
    private readonly InitiativesService _initiativesSvc;
    private readonly ProjectsService _projectsSvc;
    private readonly DepartmentDirectoryService _departments;

    public JourneyController(
        ApplicationDbContext db,
        StrategyContentService content,
        StrategyMapPdfService pdf,
        PillarsService pillarsSvc,
        ObjectivesService objectivesSvc,
        InitiativesService initiativesSvc,
        ProjectsService projectsSvc,
        DepartmentDirectoryService departments)
    {
        _db = db;
        _content = content;
        _pdf = pdf;
        _pillarsSvc = pillarsSvc;
        _objectivesSvc = objectivesSvc;
        _initiativesSvc = initiativesSvc;
        _projectsSvc = projectsSvc;
        _departments = departments;
    }

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
        var session = new StrategySession
        {
            DeptCode = access.DeptCode,
            AccessCodeUsed = access.Code,
        };
        _db.StrategySessions.Add(session);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Run), new { sessionId = session.Id });
    }

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
        return View("RunStage", vm);
    }

    // Shared builder for the journey view model (all stage partials read from it).
    private async Task<JourneyRunViewModel?> BuildRunViewModelAsync(Guid sessionId)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null) return null;

        var dept = await _db.Departments.FindAsync(session.DeptCode);
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

        return new JourneyRunViewModel
        {
            Session = session,
            Dept = dept,
            Content = _content,
            Sankey = await BuildSankeyAsync(session.DeptCode),
            Pillars = await _db.Pillars.OrderBy(p => p.PlrCode).ToListAsync(),
            Objectives = await _db.Objectives.OrderBy(o => o.ObjectiveCode).ToListAsync(),
            DeptObjectiveCodes = await _db.Kpis.Where(k => k.DepartmentCode == session.DeptCode)
                .Select(k => k.ObjectiveCode).Distinct().ToListAsync(),
            Initiatives = await _db.Initiatives.OrderBy(i => i.InitiativeCode).Take(40).ToListAsync(),
            Roster = await _db.DepartmentRoster
                .Where(r => r.DeptCode == session.DeptCode && r.IsActive)
                .OrderBy(r => r.NameAr).ToListAsync(),
            Pledges = await _db.ContributionPledges.Where(p => p.SessionId == sessionId).ToListAsync(),
            Kpis = await _db.Kpis.Where(k => k.DepartmentCode == session.DeptCode).ToListAsync(),
            Projects = await _db.Projects.Where(p => p.DepartmentCode == session.DeptCode).ToListAsync(),
            Map = map,
            InkAssets = map == null
                ? new List<MapInkAsset>()
                : await _db.MapInkAssets.Where(a => a.MapId == map.Id && a.IsActive).ToListAsync(),
            SelectedTeamValue = await _db.TeamValueSelections
                .Where(t => t.SessionId == sessionId)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => t.SelectedValueText)
                .FirstOrDefaultAsync(),

            // Phase 18
            StrategyDataLive = _pillarsSvc.Available,
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

    // Phase 18 — vision → pillars → objectives for the strategy-house stage.
    // External (Phase 17) when UseExternalDb is on; otherwise a 3×3 preview so the
    // dev/demo screen is never empty. The view shows the "data on connect" notice
    // whenever StrategyDataLive is false.
    private async Task<List<StrategyPillarVm>> BuildHousePillarsAsync()
    {
        if (_pillarsSvc.Available)
        {
            var pillars = await _pillarsSvc.GetAllAsync();
            var objectives = await _objectivesSvc.GetAllAsync();
            return pillars.Select(p => new StrategyPillarVm
            {
                Code = p.PlrCode,
                Name = p.PillarName ?? p.PlrCode,
                Objectives = objectives
                    .Where(o => o.PlrCode == p.PlrCode)
                    .Select(o => new StrategyObjectiveVm { Code = o.ObjectiveCode, Name = o.ObjectiveName ?? o.ObjectiveCode })
                    .ToList(),
            }).ToList();
        }

        return BuildPreviewPillars();
    }

    // Dev preview: 3 pillars × 3 objectives, used when the warehouse is not wired.
    private static List<StrategyPillarVm> BuildPreviewPillars()
    {
        var names = new[] { "تمكين المنافسة", "حماية المنافسة", "التميز المؤسسي" };
        var list = new List<StrategyPillarVm>();
        for (var i = 0; i < names.Length; i++)
        {
            var objs = new List<StrategyObjectiveVm>();
            for (var j = 1; j <= 3; j++)
                objs.Add(new StrategyObjectiveVm { Code = "OBJ" + (i + 1) + j, Name = "هدف توضيحي " + (i + 1) + "." + j });
            list.Add(new StrategyPillarVm { Code = "PLR" + (i + 1), Name = names[i], Objectives = objs });
        }
        return list;
    }

    // Phase 18 — resolve a chosen initiative to its objective + pillar so the
    // linkage chain (stages 3 and 4) can be drawn. External when live; otherwise
    // resolved against the preview pillars by best-effort name lookup.
    private async Task<StrategyInitiativeVm?> ResolveInitiativeAsync(string initiativeCode)
    {
        if (_initiativesSvc.Available)
        {
            var inits = await _initiativesSvc.GetAllAsync();
            var init = inits.FirstOrDefault(i => i.InitiativeCode == initiativeCode);
            if (init == null) return new StrategyInitiativeVm { Code = initiativeCode, Name = initiativeCode };

            string? pillarCode = null, pillarName = null, objName = init.ObjectiveName;
            if (!string.IsNullOrEmpty(init.ObjectiveCode))
            {
                var obj = await _objectivesSvc.GetByCodeAsync(init.ObjectiveCode);
                if (obj != null)
                {
                    objName ??= obj.ObjectiveName;
                    pillarCode = obj.PlrCode;
                    if (!string.IsNullOrEmpty(obj.PlrCode))
                        pillarName = (await _pillarsSvc.GetByCodeAsync(obj.PlrCode))?.PillarName;
                }
            }
            return new StrategyInitiativeVm
            {
                Code = init.InitiativeCode,
                Name = init.InitiativeName ?? init.InitiativeCode,
                ObjectiveCode = init.ObjectiveCode,
                ObjectiveName = objName,
                PillarCode = pillarCode,
                PillarName = pillarName,
            };
        }

        // Preview fallback: the code is the preview initiative name itself.
        var preview = BuildPreviewPillars().FirstOrDefault();
        var firstObj = preview?.Objectives.FirstOrDefault();
        return new StrategyInitiativeVm
        {
            Code = initiativeCode,
            Name = initiativeCode,
            ObjectiveCode = firstObj?.Code,
            ObjectiveName = firstObj?.Name,
            PillarCode = preview?.Code,
            PillarName = preview?.Name,
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

    // GET /Journey/Contribute/{sessionId} — deep-link alias → Stage 3.
    [HttpGet("Journey/Contribute/{sessionId:guid}")]
    public IActionResult Contribute(Guid sessionId) => RedirectToRun(sessionId, 3);

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
        if (division.Length == 0) return Json(new { ok = true, live = _initiativesSvc.Available, initiatives = Array.Empty<object>() });

        if (_initiativesSvc.Available)
        {
            var inits = await _initiativesSvc.GetAllAsync();
            var projects = await _projectsSvc.GetByDivisionAsync(division);

            // An initiative belongs to a department if it lists the division as an owner
            // OR if one of the department's projects rolls up to it.
            var projInitCodes = projects.Where(p => !string.IsNullOrEmpty(p.InitiativeCode))
                .Select(p => p.InitiativeCode!).ToHashSet();
            var matched = inits
                .Where(i => (i.Owners != null && i.Owners.Contains(division))
                            || projInitCodes.Contains(i.InitiativeCode))
                .ToList();

            var result = new List<object>();
            foreach (var i in matched)
            {
                var vm = await ResolveInitiativeAsync(i.InitiativeCode);
                result.Add(new
                {
                    code = vm!.Code,
                    name = vm.Name,
                    objectiveName = vm.ObjectiveName,
                    pillarName = vm.PillarName,
                });
            }
            return Json(new { ok = true, live = true, initiatives = result });
        }

        // Preview fallback — three illustrative initiatives mapped to the preview house.
        var preview = BuildPreviewPillars();
        var p0 = preview[0];
        var sample = new[]
        {
            new { code = "INIT-DEMO-1", name = "مبادرة توضيحية — رفع الوعي بالمنافسة", objectiveName = p0.Objectives[0].Name, pillarName = p0.Name },
            new { code = "INIT-DEMO-2", name = "مبادرة توضيحية — تطوير الأنظمة", objectiveName = p0.Objectives[1].Name, pillarName = p0.Name },
            new { code = "INIT-DEMO-3", name = "مبادرة توضيحية — تمكين الشراكات", objectiveName = p0.Objectives[2].Name, pillarName = p0.Name },
        };
        return Json(new { ok = true, live = false, initiatives = sample });
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

    // GET /Journey/Map/{sessionId} — deep-link alias → Stage 4.
    [HttpGet("Journey/Map/{sessionId:guid}")]
    public IActionResult Map(Guid sessionId) => RedirectToRun(sessionId, 4);

    // POST /Journey/SaveAttendeeCount — Phase 18: the optional attendee count moved to
    // stage 5 (الأثر). One value per session. Redirects back to stage 5.
    [HttpPost("Journey/SaveAttendeeCount/{sessionId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAttendeeCount(Guid sessionId, int attendeeCount)
    {
        var session = await _db.StrategySessions.FindAsync(sessionId);
        if (session == null) return NotFound();
        if (attendeeCount < 1 || attendeeCount > 500)
        {
            TempData["Error"] = "أدخل عدداً صحيحاً بين 1 و500.";
            return RedirectToRun(sessionId, 5);
        }
        session.AttendeeCount = attendeeCount;
        session.LastActivityAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Saved"] = "تم حفظ عدد الحاضرين.";
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

    // GET /Journey/Ink/{assetId} — streams a captured ink/signature PNG (for in-journey preview).
    [HttpGet("Journey/Ink/{assetId:guid}")]
    public async Task<IActionResult> Ink(Guid assetId)
    {
        var asset = await _db.MapInkAssets.FindAsync(assetId);
        if (asset?.PngBlob == null) return NotFound();
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
        var pillars = await _db.Pillars.OrderBy(p => p.PlrCode).ToListAsync();
        var kpis = await _db.Kpis.Where(k => k.DepartmentCode == session.DeptCode).ToListAsync();
        var projects = await _db.Projects.Where(p => p.DepartmentCode == session.DeptCode).ToListAsync();
        var inkAssets = await _db.MapInkAssets.Where(a => a.MapId == map.Id).ToListAsync();
        var objectives = await _db.Objectives.ToListAsync();
        var initiatives = await _db.Initiatives.ToListAsync();
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
        var kpis = await _db.Kpis.Where(k => k.DepartmentCode == deptCode).ToListAsync();
        var projects = await _db.Projects.Where(p => p.DepartmentCode == deptCode).ToListAsync();
        var objectives = await _db.Objectives.ToListAsync();
        var pillars = await _db.Pillars.ToListAsync();
        var initiatives = await _db.Initiatives.ToListAsync();

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
}

public class StrategyInitiativeVm
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ObjectiveCode { get; set; }
    public string? ObjectiveName { get; set; }
    public string? PillarCode { get; set; }
    public string? PillarName { get; set; }
}

public class PledgeDto
{
    public Guid SessionId { get; set; }
    public string ElementType { get; set; } = string.Empty;
    public string ElementCode { get; set; } = string.Empty;
    public string? ContributionKind { get; set; }
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
    public string? ValueKey { get; set; }
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
    public string? ReflectionText { get; set; }
}

public class RoleContributionDto
{
    public Guid SessionId { get; set; }
    public string? DepartmentCode { get; set; }
    public string? SelectedInitiativeCode { get; set; }
    public string? PerceivedImpact { get; set; }
}
