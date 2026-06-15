using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace StrategyHouse.Web.Services;

// Phase 9 — view helper for the mini CMS. Usage: @Html.Content("home.hero.title", "default")
public static class HtmlContentExtensions
{
    public static string Content(this IHtmlHelper html, string key, string? fallback = null)
    {
        var svc = html.ViewContext.HttpContext.RequestServices.GetRequiredService<PageContentService>();
        return svc.Get(key, fallback);
    }
}
