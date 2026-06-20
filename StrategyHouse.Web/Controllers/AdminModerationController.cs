using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 3 — real ink moderation: thumbnail queue, per-item & bulk actions, audit log,
// and synchronous PDF regeneration of the affected map after each action.
[Authorize(Roles = "Admin")]
[Route("Admin/Moderation")]
public class AdminModerationController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly StrategyMapPdfService _pdf;
    private readonly StrategyContentService _content;
    private readonly IStrategyDataSource _source;

    public AdminModerationController(ApplicationDbContext db, StrategyMapPdfService pdf, StrategyContentService content, IStrategyDataSource source)
    {
        _db = db;
        _pdf = pdf;
        _content = content;
        _source = source;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string status = "Pending")
    {
        ViewBag.Status = status;
        var assets = await _db.MapInkAssets
            .Where(a => a.ModerationStatus == status)
            .OrderByDescending(a => a.CapturedAt)
            .ToListAsync();
        // Group by dept via the owning map.
        var mapIds = assets.Select(a => a.MapId).Distinct().ToList();
        var maps = await _db.DepartmentStrategyMaps.Where(m => mapIds.Contains(m.Id)).ToListAsync();
        ViewBag.MapDept = maps.ToDictionary(m => m.Id, m => m.DeptCode);
        ViewBag.DeptNames = await _db.Departments.ToDictionaryAsync(d => d.DeptCode, d => d.NameAr ?? d.DeptCode);
        ViewBag.Depts = assets.Select(a => maps.FirstOrDefault(m => m.Id == a.MapId)?.DeptCode)
            .Where(c => c != null).Distinct().ToList();
        ViewBag.Kinds = assets.Select(a => a.AssetKind).Distinct().ToList();
        return View(assets);
    }

    // GET /Admin/Moderation/Thumb/{id} — streams the captured PNG.
    [HttpGet("Thumb/{id:guid}")]
    public async Task<IActionResult> Thumb(Guid id)
    {
        var asset = await _db.MapInkAssets.FindAsync(id);
        if (asset?.PngBlob == null) return NotFound();
        return File(asset.PngBlob, "image/png");
    }

    [HttpPost("Approve/{id}")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Approve(Guid id, string? note) => ActAsync(id, "Approve", "Approved", note);

    [HttpPost("Reject/{id}")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Reject(Guid id, string? note) => ActAsync(id, "Reject", "Rejected", note);

    [HttpPost("Hide/{id}")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Hide(Guid id, string? note) => ActAsync(id, "Hide", "Hidden", note);

    [HttpPost("Reactivate/{id}")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Reactivate(Guid id, string? note) => ActAsync(id, "Reactivate", "Approved", note);

    private async Task<IActionResult> ActAsync(Guid id, string action, string newStatus, string? note)
    {
        var asset = await _db.MapInkAssets.FindAsync(id);
        if (asset != null)
        {
            ApplyStatus(asset, action, newStatus, note);
            WriteAudit("MapInkAsset", id, action, note);
            await _db.SaveChangesAsync();
            await RegenerateMapPdfAsync(asset.MapId);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Bulk")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Bulk(Guid[] ids, string action)
    {
        var (newStatus, _) = Resolve(action);
        var assets = await _db.MapInkAssets.Where(a => ids.Contains(a.Id)).ToListAsync();
        foreach (var asset in assets)
        {
            ApplyStatus(asset, action, newStatus, "bulk");
            WriteAudit("MapInkAsset", asset.Id, action, "bulk");
        }
        await _db.SaveChangesAsync();
        foreach (var mapId in assets.Select(a => a.MapId).Distinct())
            await RegenerateMapPdfAsync(mapId);
        return RedirectToAction(nameof(Index));
    }

    // Approve/Reject/Hide every pending asset for a department.
    [HttpPost("BulkDept")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDept(string deptCode, string action, string status = "Pending")
    {
        var mapIds = await _db.DepartmentStrategyMaps.Where(m => m.DeptCode == deptCode).Select(m => m.Id).ToListAsync();
        var assets = await _db.MapInkAssets.Where(a => mapIds.Contains(a.MapId) && a.ModerationStatus == status).ToListAsync();
        await ApplyBulkAsync(assets, action, $"dept:{deptCode}");
        return RedirectToAction(nameof(Index), new { status });
    }

    // Approve/Reject/Hide every pending asset of a kind (e.g. all signatures).
    [HttpPost("BulkKind")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkKind(string kind, string action, string status = "Pending")
    {
        var assets = await _db.MapInkAssets.Where(a => a.AssetKind == kind && a.ModerationStatus == status).ToListAsync();
        await ApplyBulkAsync(assets, action, $"kind:{kind}");
        return RedirectToAction(nameof(Index), new { status });
    }

    private async Task ApplyBulkAsync(List<MapInkAsset> assets, string action, string scope)
    {
        var (newStatus, _) = Resolve(action);
        foreach (var asset in assets)
        {
            ApplyStatus(asset, action, newStatus, scope);
            WriteAudit("MapInkAsset", asset.Id, action, scope);
        }
        await _db.SaveChangesAsync();
        foreach (var mapId in assets.Select(a => a.MapId).Distinct())
            await RegenerateMapPdfAsync(mapId);
    }

    private static (string status, bool active) Resolve(string action) => action switch
    {
        "Approve" => ("Approved", true),
        "Reject" => ("Rejected", false),
        "Hide" => ("Hidden", false),
        "Reactivate" => ("Approved", true),
        _ => ("Pending", true),
    };

    private void ApplyStatus(MapInkAsset asset, string action, string newStatus, string? note)
    {
        asset.ModerationStatus = newStatus;
        asset.ModeratedAt = DateTime.UtcNow;
        asset.ModeratedBy = User.Identity?.Name;
        asset.ModerationNote = note;
        asset.IsActive = newStatus != "Hidden" && newStatus != "Rejected";
    }

    // Synchronously rebuild a signed map's PDF so it reflects current approved ink.
    private async Task RegenerateMapPdfAsync(Guid mapId)
    {
        var map = await _db.DepartmentStrategyMaps.FirstOrDefaultAsync(m => m.Id == mapId);
        if (map?.SignedAt == null) return; // only signed maps carry a PDF

        var session = await _db.StrategySessions.Include(s => s.Members).FirstOrDefaultAsync(s => s.Id == map.SessionId);
        var members = session?.Members.ToList() ?? new List<SessionMember>();
        var dept = await _db.Departments.FindAsync(map.DeptCode) ?? new Department { DeptCode = map.DeptCode };
        var pledges = await _db.ContributionPledges.Where(p => p.SessionId == map.SessionId).ToListAsync();
        // Phase 19.23 — strategy reads route through the unified source (mirror → SQLite → empty).
        var pillars = (await _source.GetPillarsAsync())
            .Select(p => new Pillar { PlrCode = p.Code, PillarName = p.Name }).ToList();
        var kpis = (await _source.GetKpisAsync(map.DeptCode))
            .Select(k => new Kpi { KpiCode = k.Code, KpiName = k.Name, ObjectiveCode = k.ObjectiveCode, KpiType = k.Type }).ToList();
        var projects = (await _source.GetProjectsAsync(map.DeptCode))
            .Select(p => new Project { ProjectCode = p.Code, ProjectName = p.Name, InitiativeCode = p.InitiativeCode }).ToList();
        var assets = await _db.MapInkAssets.Where(a => a.MapId == mapId).ToListAsync();

        try
        {
            map.PdfBlob = _pdf.Generate(map, dept, members, pledges, pillars, kpis, projects, assets);
            await _db.SaveChangesAsync();
        }
        catch
        {
            // Never let PDF regen failure break a moderation action.
        }
    }

    [HttpGet("Map/{mapId}")]
    public async Task<IActionResult> Map(Guid mapId)
    {
        var map = await _db.DepartmentStrategyMaps.FindAsync(mapId);
        if (map == null) return NotFound();
        ViewBag.Assets = await _db.MapInkAssets.Where(a => a.MapId == mapId).ToListAsync();
        return View(map);
    }

    [HttpGet("AuditLog")]
    public async Task<IActionResult> AuditLog(int page = 1)
    {
        const int pageSize = 50;
        var total = await _db.ModerationAuditLogs.CountAsync();
        var logs = await _db.ModerationAuditLogs
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();
        ViewBag.Page = page;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
        return View(logs);
    }

    private void WriteAudit(string targetType, Guid targetId, string action, string? note)
    {
        _db.ModerationAuditLogs.Add(new ModerationAuditLog
        {
            TargetType = targetType,
            TargetId = targetId,
            Action = action,
            ActorUserId = User.Identity?.Name,
            Note = note,
        });
    }
}
