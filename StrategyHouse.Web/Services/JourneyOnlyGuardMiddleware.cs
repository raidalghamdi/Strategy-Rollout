using Microsoft.AspNetCore.Identity;
using StrategyHouse.Domain.Entities;

namespace StrategyHouse.Web.Services;

// Phase 20.6 — any signed-in user whose AppUser.JourneyScopeKey is non-null is
// considered a journey-only account (gac.admin, vp.support, vp.economic, vp.legal,
// testing). They must NEVER see the platform UI (dashboards, admin pages, etc.).
// This middleware redirects such users to /Journey/Pick for any request outside the
// minimal allow-list below. Real platform admins (JourneyScopeKey == null) are
// untouched and keep full access.
public class JourneyOnlyGuardMiddleware
{
    private readonly RequestDelegate _next;

    // Path prefixes a journey-only user may visit. Everything else bounces to the
    // executive live dashboard, which is their landing page.
    private static readonly string[] AllowedPrefixes = new[]
    {
        "/Admin/LiveDashboard", // landing executive dashboard (their main view)
        "/Admin/SessionDetail", // session drill-down from the dashboard
        "/Journey",             // start / continue a journey when picked from the dashboard
        "/Quiz",                // Phase 20.10 — quiz access from VP journey complete
        "/api",                 // Phase 20.10 — Sankey + strategy data APIs
        "/Account/Logout",      // sign out
        "/Account/Login",       // re-login if session expires mid-flow
        "/Account/AccessDenied",
        "/Account/Profile",     // self-service profile / change own password
        "/css", "/js", "/lib", "/images", "/img", "/favicon", "/static",
        "/_framework", "/_blazor", "/_vs",
    };

    public JourneyOnlyGuardMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext ctx, UserManager<AppUser> users)
    {
        if (ctx.User?.Identity?.IsAuthenticated == true)
        {
            var path = ctx.Request.Path.Value ?? "/";

            // Fast allow-list pass — most journey requests hit this and skip the DB call.
            var allowed = AllowedPrefixes.Any(p =>
                path.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));

            if (!allowed)
            {
                var u = await users.GetUserAsync(ctx.User);
                if (u != null && !string.IsNullOrEmpty(u.JourneyScopeKey))
                {
                    ctx.Response.Redirect("/Admin/LiveDashboard");
                    return;
                }
            }
        }

        await _next(ctx);
    }
}
