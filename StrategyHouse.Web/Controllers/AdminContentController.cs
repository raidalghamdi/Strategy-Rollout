using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 9 — mini CMS editor. Lists the editable keys with their current values and
// saves edits back to the PageContents table (cache refreshed on save).
[Authorize(Roles = "Admin,Facilitator")]
[Route("Admin/Content")]
public class AdminContentController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly PageContentService _content;

    public AdminContentController(ApplicationDbContext db, PageContentService content)
    {
        _db = db;
        _content = content;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        var items = _content.All()
            .Select(x => new ContentItem { Key = x.Key, Value = x.Value })
            .ToList();
        return View(items);
    }

    [HttpPost("Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string[] keys, string[] values)
    {
        var allowed = PageContentService.Defaults.Select(d => d.Key).ToHashSet();
        var n = Math.Min(keys?.Length ?? 0, values?.Length ?? 0);
        var saved = 0;
        for (var i = 0; i < n; i++)
        {
            var key = keys![i];
            if (!allowed.Contains(key)) continue;
            await _content.SaveAsync(_db, key, values![i] ?? string.Empty);
            saved++;
        }
        TempData["Saved"] = $"تم حفظ {saved} نصاً.";
        return RedirectToAction(nameof(Index));
    }
}

public class ContentItem
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
