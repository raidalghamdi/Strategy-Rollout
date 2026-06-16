using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 9 — mini CMS. Loads admin-editable strings from the PageContents table into an
// in-memory cache (the table is tiny and rarely changes). Views read via the
// @Html.Content("key", default) helper; admins edit at /Admin/Content.
public class PageContentService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<string, string> _cache = new();
    private volatile bool _loaded;
    private readonly object _gate = new();

    public PageContentService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    // The canonical list of editable keys with their seeded default Arabic text.
    public static readonly IReadOnlyList<(string Key, string Default)> Defaults = new (string, string)[]
    {
        ("home.hero.title", "منصة استراتيجية الهيئة العامة للمنافسة"),
        ("home.hero.subtitle", "رحلة إطلاق الاستراتيجية المؤسسية — من الرؤية إلى التنفيذ على مستوى كل إدارة."),
        ("home.hero.cta", "ابدأ رحلة إدارتك"),
        ("home.about.title", "عن المنصة"),
        ("home.about.body", "منصة رقمية تفاعلية لإطلاق الاستراتيجية المؤسسية على مستوى الإدارات الثماني عشرة، تُعرّف الموظفين بالرؤية والرسالة والقيم، وتبني الخريطة الاستراتيجية لكل إدارة بشكل جماعي، وتُسجّل الالتزامات الطوعية على الجدار الرقمي للالتزامات."),
        ("home.objectives.title", "أهداف الرحلة"),
        ("home.objectives.body", "ترسيخ فهم مشترك للاستراتيجية، وربط عمل كل إدارة بالركائز والأهداف المؤسسية، وتحفيز الالتزام الجماعي بتحقيق المستهدفات، وبناء ثقافة تنفيذ موحّدة عبر الهيئة."),
        ("home.cta.title", "هل أنت مستعد لبدء رحلة إدارتك الاستراتيجية؟"),
        ("home.cta.button", "الدخول إلى المنصة"),
        ("home.contact.title", "للتواصل مع مكتب الاستراتيجية"),
        ("home.contact.phone", "011 000 0000"),
        ("home.contact.email", "strategy@gac.gov.sa"),
        ("home.footer.note", "جميع الحقوق محفوظة — الهيئة العامة للمنافسة"),
        ("journey.landing.intro", "أدخل رمز الدخول الخاص بإدارتك لبدء رحلة الاستراتيجية."),
        ("journey.stage1.intro", "اختر أعضاء فريق الإدارة المشاركين في هذه الرحلة."),
        ("journey.stage2.intro", "تعرّف على كيفية ارتباط عمل إدارتك بالرؤية والركائز الاستراتيجية."),
        ("journey.stage3.intro", "استعرض بيت الاستراتيجية: الرؤية والرسالة والقيم والركائز والأهداف."),
        ("journey.stage4.intro", "حدّد العناصر الاستراتيجية التي ترغب إدارتك بالمساهمة في تحقيقها."),
        ("journey.stage5.intro", "ارسم خريطة إدارتك ووقّعها مع فريقك."),
        ("journey.stage6.intro", "تهانينا على إتمام رحلة الاستراتيجية."),
        ("quiz.intro", "اختبار قصير لتعزيز فهم الفريق للاستراتيجية المؤسسية."),
        ("survey.thank_you", "شكراً لمشاركتك! تم استلام إجابتك بنجاح."),
        ("footer.copyright", "الهيئة العامة للمنافسة · منصة إطلاق الاستراتيجية"),
    };

    private static readonly Dictionary<string, string> DefaultMap =
        Defaults.ToDictionary(d => d.Key, d => d.Default);

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_gate)
        {
            if (_loaded) return;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var row in db.PageContents.AsNoTracking().ToList())
                _cache[row.Key] = row.ValueAr;
            _loaded = true;
        }
    }

    // Returns the stored value, or the provided default, or the seeded default.
    public string Get(string key, string? fallback = null)
    {
        EnsureLoaded();
        if (_cache.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
        if (fallback != null) return fallback;
        return DefaultMap.TryGetValue(key, out var d) ? d : key;
    }

    // All keys with current effective values (stored value or seeded default), for the admin editor.
    public IEnumerable<(string Key, string Value)> All()
    {
        EnsureLoaded();
        return Defaults.Select(d => (d.Key, Get(d.Key)));
    }

    public async Task SaveAsync(ApplicationDbContext db, string key, string value)
    {
        var row = await db.PageContents.FindAsync(key);
        if (row == null)
        {
            row = new PageContent { Key = key, ValueAr = value, UpdatedAt = DateTime.UtcNow };
            db.PageContents.Add(row);
        }
        else
        {
            row.ValueAr = value;
            row.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        _cache[key] = value; // invalidate/refresh the single key
    }
}
