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
        return RedirectToAction(nameof(Members), new { sessionId = session.Id });
    }

    // GET /Journey/Members/{sessionId}
    [HttpGet("Journey/Members/{sessionId:guid}")]
    public async Task<IActionResult> Members(Guid sessionId)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null) return NotFound();
        ViewBag.Dept = await _db.Departments.FindAsync(session.DeptCode);
        return View(session);
    }

    [HttpPost("Journey/AddMembers/{sessionId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMembers(Guid sessionId, string[] names, string[] roles)
    {
        var session = await _db.StrategySessions.FindAsync(sessionId);
        if (session == null) return NotFound();
        for (var i = 0; i < names.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(names[i])) continue;
            _db.SessionMembers.Add(new SessionMember
            {
                SessionId = sessionId,
                NameAr = names[i].Trim(),
                Role = i < roles.Length ? roles[i]?.Trim() : null,
            });
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(BigPicture), new { sessionId });
    }

    // GET /Journey/BigPicture/{sessionId} — Stage 1: D3 sankey.
    [HttpGet("Journey/BigPicture/{sessionId:guid}")]
    public async Task<IActionResult> BigPicture(Guid sessionId)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null) return NotFound();
        ViewBag.Dept = await _db.Departments.FindAsync(session.DeptCode);
        ViewBag.Content = _content;
        ViewBag.Sankey = await BuildSankeyAsync(session.DeptCode);
        return View(session);
    }

    // GET /Journey/StrategyHouse/{sessionId} — Stage 2: top-down house.
    [HttpGet("Journey/StrategyHouse/{sessionId:guid}")]
    public async Task<IActionResult> StrategyHouse(Guid sessionId)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null) return NotFound();
        ViewBag.Dept = await _db.Departments.FindAsync(session.DeptCode);
        ViewBag.Content = _content;
        ViewBag.Pillars = await _db.Pillars.OrderBy(p => p.PlrCode).ToListAsync();
        ViewBag.Objectives = await _db.Objectives.OrderBy(o => o.ObjectiveCode).ToListAsync();
        var deptKpiObj = await _db.Kpis.Where(k => k.DepartmentCode == session.DeptCode)
            .Select(k => k.ObjectiveCode).Distinct().ToListAsync();
        ViewBag.DeptObjectiveCodes = deptKpiObj;
        return View(session);
    }

    // GET /Journey/Contribute/{sessionId} — Stage 3: drag/drop pledges.
    [HttpGet("Journey/Contribute/{sessionId:guid}")]
    public async Task<IActionResult> Contribute(Guid sessionId)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null) return NotFound();
        ViewBag.Dept = await _db.Departments.FindAsync(session.DeptCode);
        ViewBag.Objectives = await _db.Objectives.OrderBy(o => o.ObjectiveCode).ToListAsync();
        ViewBag.Initiatives = await _db.Initiatives.OrderBy(i => i.InitiativeCode).Take(40).ToListAsync();
        ViewBag.Pledges = await _db.ContributionPledges.Where(p => p.SessionId == sessionId).ToListAsync();
        return View(session);
    }

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

    // GET /Journey/Map/{sessionId} — Stage 4: map + textareas + sign panel.
    [HttpGet("Journey/Map/{sessionId:guid}")]
    public async Task<IActionResult> Map(Guid sessionId)
    {
        var session = await LoadSessionAsync(sessionId);
        if (session == null) return NotFound();
        ViewBag.Dept = await _db.Departments.FindAsync(session.DeptCode);
        ViewBag.Content = _content;
        ViewBag.Kpis = await _db.Kpis.Where(k => k.DepartmentCode == session.DeptCode).ToListAsync();
        ViewBag.Projects = await _db.Projects.Where(p => p.DepartmentCode == session.DeptCode).ToListAsync();
        ViewBag.Pledges = await _db.ContributionPledges.Where(p => p.SessionId == sessionId).ToListAsync();
        ViewBag.Map = await _db.DepartmentStrategyMaps.FirstOrDefaultAsync(m => m.SessionId == sessionId);
        return View(session);
    }

    // POST /Journey/SaveMap — saves layout JSON + texts.
    [HttpPost("Journey/SaveMap/{sessionId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveMap(Guid sessionId, string? mapLayoutJson, string? opinions, string? wishes, string? commitments)
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
            return RedirectToAction(nameof(Map), new { sessionId });
        }
        map.MapLayoutJson = mapLayoutJson;
        map.OpinionsText = opinions;
        map.WishesText = wishes;
        map.CommitmentsText = commitments;
        await _db.SaveChangesAsync();
        TempData["Saved"] = "تم حفظ الخريطة.";
        return RedirectToAction(nameof(Map), new { sessionId });
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
        try
        {
            map.PdfBlob = _pdf.Generate(map, dept, session.Members.ToList(), pledges, pillars, kpis, projects);
            await _db.SaveChangesAsync();
        }
        catch
        {
            // PDF generation failure must not block the journey; map stays signed.
        }

        return RedirectToAction(nameof(Complete), new { sessionId });
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

public class PledgeDto
{
    public Guid SessionId { get; set; }
    public string ElementType { get; set; } = string.Empty;
    public string ElementCode { get; set; } = string.Empty;
    public string? ContributionKind { get; set; }
    public string? Notes { get; set; }
}
