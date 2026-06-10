using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly DashboardService _svc;
    public DashboardController(DashboardService svc) { _svc = svc; }

    public async Task<IActionResult> Index(DateTime? day)
    {
        var d = day ?? DateTime.UtcNow.Date;
        var summary = await _svc.BuildAsync(d);
        return View(summary);
    }
}
