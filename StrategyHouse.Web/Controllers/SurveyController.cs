using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace StrategyHouse.Web.Controllers;

// Top-level /Survey alias so the bare URL never 404s. Public survey forms are served
// at /s/{token}; admins manage surveys under /Admin/Surveys. This forwards admins there.
[Authorize(Roles = "Admin,Facilitator")]
public class SurveyController : Controller
{
    [HttpGet("Survey")]
    public IActionResult Index() => Redirect("/Admin/Surveys");

    // Plan-specified singular alias /Admin/Survey/{id}/Analytics → canonical plural route.
    [HttpGet("Admin/Survey/{id:guid}/Analytics")]
    public IActionResult Analytics(Guid id) => RedirectToAction("Analytics", "AdminSurveys", new { id });
}
