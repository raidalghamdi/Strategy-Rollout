using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _db;
    public HomeController(ApplicationDbContext db) { _db = db; }

    public async Task<IActionResult> Index()
    {
        // Phase 19.20 (Fix 2) — count DISTINCT strategy elements. Seed/import data can
        // contain duplicate rows (same code appearing more than once); the home tiles
        // should reflect the real number of unique pillars/objectives/initiatives, not
        // the raw row count. Dedup by code, falling back to the Arabic name when the
        // code is blank.
        var pillars = await _db.Pillars.AsNoTracking().ToListAsync();
        var objectives = await _db.Objectives.AsNoTracking().ToListAsync();
        var initiatives = await _db.Initiatives.AsNoTracking().ToListAsync();

        var stats = new HomeStats(
            StrategyDedup.ByPillarCode(pillars).Count,
            StrategyDedup.ByObjectiveCode(objectives).Count,
            StrategyDedup.ByInitiativeCode(initiatives).Count,
            await _db.Projects.CountAsync(),
            await _db.Kpis.CountAsync());
        ViewBag.Stats = stats;
        return View();
    }

    public IActionResult Error() => View();
}

public record HomeStats(int Pillars, int Objectives, int Initiatives, int Projects, int Kpis);
