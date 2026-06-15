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
    private readonly ApplicationDbContext _db;
    private readonly StrategyContentService _content;
    private readonly StrategyMapPdfService _pdf;

    public JourneyController(ApplicationDbContext db, StrategyContentService content, StrategyMapPdfService pdf)
    {
        _db = db;
        _content = content;
        _pdf = pdf;
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
        var stage = Math.Clamp(session.CurrentStage <= 0 ? 1 : session.CurrentStage, 1, 6);
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
        var maxAllowed = Math.Clamp((tracked.CurrentStage <= 0 ? 1 : tracked.CurrentStage) + 1, 1, 6);
        stage = Math.Clamp(stage, 1, 6);
        if (stage > maxAllowed)
            return RedirectToAction(nameof(RunStage), new { sessionId, stage = maxAllowed });

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
        };
    }

    // GET /Journey/Members/{sessionId} — deep-link alias → multi-page stage.
    [HttpGet("Journey/Members/{sessionId:guid}")]
    public IActionResult Members(Guid sessionId) => RedirectToRun(sessionId, 1);

    private IActionResult RedirectToRun(Guid sessionId, int stage) =>
        RedirectToAction(nameof(RunStage), new { sessionId, stage });

    [HttpPost("Journey/AddMembers/{sessionId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMembers(Guid sessionId, string[]? rosterIds, string[]? names, string[]? roles)
    {
        var session = await _db.StrategySessions
            .Include(s => s.Members)
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session == null) return NotFound();

        var existing = session.Members.Select(m => m.NameAr).ToHashSet();

        // Checked roster members (Phase 6) — resolve names from the roster table.
        var ids = (rosterIds ?? Array.Empty<string>())
            .Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty).ToList();
        if (ids.Count > 0)
        {
            var roster = await _db.DepartmentRoster
                .Where(r => ids.Contains(r.MemberId) && r.DeptCode == session.DeptCode)
                .ToListAsync();
            foreach (var r in roster)
            {
                if (!existing.Add(r.NameAr)) continue;
                _db.SessionMembers.Add(new SessionMember { SessionId = sessionId, NameAr = r.NameAr, Role = r.Role });
            }
        }

        // Manually typed extra members.
        var nm = names ?? Array.Empty<string>();
        var rl = roles ?? Array.Empty<string>();
        for (var i = 0; i < nm.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(nm[i])) continue;
            var name = nm[i].Trim();
            if (!existing.Add(name)) continue;
            _db.SessionMembers.Add(new SessionMember
            {
                SessionId = sessionId,
                NameAr = name,
                Role = i < rl.Length ? rl[i]?.Trim() : null,
            });
        }

        // Phase 7 — stamp the first time a team roster is saved so the journey
        // can surface the quiz launch on Stage 1.
        if (session.MembersSubmittedAt == null && (session.Members.Count > 0 || existing.Count > 0))
            session.MembersSubmittedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return RedirectToRun(sessionId, 2);
    }

    // GET /Journey/BigPicture/{sessionId} — deep-link alias → single page section.
    [HttpGet("Journey/BigPicture/{sessionId:guid}")]
    public IActionResult BigPicture(Guid sessionId) => RedirectToRun(sessionId, 2);

    // GET /Journey/StrategyHouse/{sessionId} — deep-link alias → single page section.
    [HttpGet("Journey/StrategyHouse/{sessionId:guid}")]
    public IActionResult StrategyHouse(Guid sessionId) => RedirectToRun(sessionId, 3);

    // GET /Journey/Contribute/{sessionId} — deep-link alias → single page section.
    [HttpGet("Journey/Contribute/{sessionId:guid}")]
    public IActionResult Contribute(Guid sessionId) => RedirectToRun(sessionId, 4);

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

    // GET /Journey/Map/{sessionId} — deep-link alias → single page section.
    [HttpGet("Journey/Map/{sessionId:guid}")]
    public IActionResult Map(Guid sessionId) => RedirectToRun(sessionId, 5);

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
            return RedirectToRun(sessionId, 5);
        }
        map.MapLayoutJson = mapLayoutJson;
        map.OpinionsText = opinions;
        map.CommitmentsText = commitments;
        await _db.SaveChangesAsync();
        TempData["Saved"] = "تم حفظ الخريطة.";
        return RedirectToRun(sessionId, 5);
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
        try
        {
            map.PdfBlob = _pdf.Generate(map, dept, session.Members.ToList(), pledges, pillars, kpis, projects, inkAssets);
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
        if (png == null) return BadRequest(new { ok = false, error = "png" });

        var now = DateTime.UtcNow;
        var asset = new MapInkAsset
        {
            MapId = map.Id,
            AssetKind = "signature",
            PngBlob = png,
            StrokesJson = dto.StrokesJson,
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
            var ki = AddNode("K:" + k.KpiCode, k.KpiName ?? k.KpiCode, "kpi");
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
}
