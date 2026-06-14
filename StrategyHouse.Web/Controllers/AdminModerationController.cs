using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Controllers;

// Phase 3 will populate the ink queue. Phase 1 ships the skeleton + audit logging.
[Authorize(Roles = "Admin")]
[Route("Admin/Moderation")]
public class AdminModerationController : Controller
{
    private readonly ApplicationDbContext _db;

    public AdminModerationController(ApplicationDbContext db) { _db = db; }

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
        return View(assets);
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
            asset.ModerationStatus = newStatus;
            asset.ModeratedAt = DateTime.UtcNow;
            asset.ModeratedBy = User.Identity?.Name;
            asset.ModerationNote = note;
            asset.IsActive = newStatus != "Hidden" && newStatus != "Rejected";
            WriteAudit("MapInkAsset", id, action, note);
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Bulk")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Bulk(Guid[] ids, string action)
    {
        var (newStatus, active) = action switch
        {
            "Approve" => ("Approved", true),
            "Reject" => ("Rejected", false),
            "Hide" => ("Hidden", false),
            "Reactivate" => ("Approved", true),
            _ => ("Pending", true),
        };
        var assets = await _db.MapInkAssets.Where(a => ids.Contains(a.Id)).ToListAsync();
        foreach (var asset in assets)
        {
            asset.ModerationStatus = newStatus;
            asset.ModeratedAt = DateTime.UtcNow;
            asset.ModeratedBy = User.Identity?.Name;
            asset.IsActive = active;
            WriteAudit("MapInkAsset", asset.Id, action, "bulk");
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
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
