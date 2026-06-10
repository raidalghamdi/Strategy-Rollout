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
            await _db.Departments.CountAsync(),
            await _db.Sessions.CountAsync(),
            await _db.SessionAttendees.CountAsync(),
            await _db.StrategyMaps.CountAsync(),
            await _db.MapCommitments.CountAsync());
        ViewBag.Stats = stats;
        return View();
    }

    public IActionResult Error() => View();
}

public record HomeStats(int Departments, int Sessions, int Attendees, int Maps, int Commitments);
