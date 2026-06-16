using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 14 — seed the editable home-page CMS keys (home.*) into PageContents so the
// admin editor at /Admin/Content shows persisted rows out of the box. Idempotent:
// only inserts keys that are missing; never overwrites an admin's edits.
public static class Phase14HomeContentSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db)
    {
        var existing = db.PageContents.Select(p => p.Key).ToHashSet();
        var now = DateTime.UtcNow;
        var added = false;

        foreach (var (key, def) in PageContentService.Defaults)
        {
            if (!key.StartsWith("home.", StringComparison.Ordinal)) continue;
            if (existing.Contains(key)) continue;
            db.PageContents.Add(new PageContent { Key = key, ValueAr = def, UpdatedAt = now });
            added = true;
        }

        if (added) await db.SaveChangesAsync();
    }
}
