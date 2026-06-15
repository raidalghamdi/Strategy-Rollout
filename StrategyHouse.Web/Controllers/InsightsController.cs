using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace StrategyHouse.Web.Controllers;

// Top-level /Insights alias so the bare URL never 404s. The real analytics live
// under /Admin/Insights (auth-gated); this just forwards there.
[Authorize(Roles = "Admin,Facilitator")]
public class InsightsController : Controller
{
    [HttpGet("Insights")]
    public IActionResult Index() => Redirect("/Admin/Insights");
}
