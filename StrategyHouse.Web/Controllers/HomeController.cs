using Microsoft.AspNetCore.Mvc;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

public class HomeController : Controller
{
    private readonly IStrategyDataSource _source;
    public HomeController(IStrategyDataSource source) { _source = source; }

    public async Task<IActionResult> Index()
    {
        // Phase 19.23 — home tiles read live counts through the unified source
        // (MSSQL mirror → SQLite → empty). No PageContent overrides, no dummy data.
        var counts = await _source.GetCountsAsync(HttpContext.RequestAborted);
        ViewBag.Stats = new HomeStats(
            counts.Pillars, counts.Objectives, counts.Initiatives, counts.Projects, counts.Kpis);
        return View();
    }

    public IActionResult Error() => View();
}

public record HomeStats(int Pillars, int Objectives, int Initiatives, int Projects, int Kpis);
