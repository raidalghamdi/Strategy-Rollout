using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _db;
    public HomeController(ApplicationDbContext db) { _db = db; }

    public async Task<IActionResult> Index()
    {
        var stats = new HomeStats(
            await _db.Pillars.CountAsync(),
            await _db.Objectives.CountAsync(),
            await _db.Initiatives.CountAsync(),
            await _db.Projects.CountAsync(),
            await _db.Kpis.CountAsync());
        ViewBag.Stats = stats;
        return View();
    }

    public IActionResult Error() => View();
}

public record HomeStats(int Pillars, int Objectives, int Initiatives, int Projects, int Kpis);
