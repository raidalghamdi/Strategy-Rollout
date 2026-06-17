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

        // Phase 19 — reconcile a few home keys whose default text changed this phase.
        // Only rewrite rows that still hold the previous phase's default, so genuine
        // admin edits are preserved. Maps old default → new default per key.
        var reconcile = new (string Key, string Old, string New)[]
        {
            ("home.contact.title", "للتواصل مع مكتب الاستراتيجية", "للتواصل مع إدارة الاستراتيجية والأداء المؤسسي"),
            ("home.cta.title", "هل أنت مستعد لبدء رحلة إدارتك الاستراتيجية؟", "ابدأ رحلة إدارتك الاستراتيجية"),
        };
        foreach (var (key, oldVal, newVal) in reconcile)
        {
            var row = db.PageContents.FirstOrDefault(p => p.Key == key);
            if (row != null && row.ValueAr == oldVal)
            {
                row.ValueAr = newVal;
                row.UpdatedAt = now;
                added = true;
            }
        }

        if (added) await db.SaveChangesAsync();
    }
}
