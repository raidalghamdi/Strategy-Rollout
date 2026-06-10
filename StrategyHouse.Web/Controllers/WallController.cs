using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Controllers;

/// <summary>
/// The digital Commitment Wall — shows every department's finalized Strategy Map.
/// During the rollout week, only session attendees (validated by session code in
/// query string) and authenticated strategy office users may view. After the
/// rollout, becomes broadcast at the strategy office's command.
/// </summary>
public class WallController : Controller
{
    private readonly ApplicationDbContext _db;
    public WallController(ApplicationDbContext db) { _db = db; }

    [HttpGet("/Wall")]
    public async Task<IActionResult> Index(string? code)
    {
        var hasAccess = User.Identity?.IsAuthenticated == true
            || (!string.IsNullOrWhiteSpace(code) &&
                await _db.Sessions.AnyAsync(s => s.AccessCode == code));

        if (!hasAccess)
            return View("AccessGate");

        var maps = await _db.StrategyMaps
            .Where(m => m.IsFinalized)
            .Include(m => m.Department)
            .Include(m => m.Session)
            .Include(m => m.Commitments).ThenInclude(c => c.LinkedElement)
            .Include(m => m.Signatures)
            .OrderBy(m => m.FinalizedAt)
            .ToListAsync();

        var totalDepts = await _db.Departments.CountAsync(d => d.IsActive);
        ViewBag.TotalDepts = totalDepts;
        ViewBag.IsComplete = maps.Count >= totalDepts;
        return View(maps);
    }
}
