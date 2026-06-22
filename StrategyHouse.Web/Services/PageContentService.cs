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
        // Phase 19.23 — home stat tiles now show live counts from the unified data
        // source (mirror → SQLite → empty); the static home.stats.* overrides were removed.
        ("home.objectives.title", "أهداف الرحلة"),
        ("home.objectives.body", "ترسيخ فهم مشترك للاستراتيجية، وربط عمل كل إدارة بالركائز والأهداف المؤسسية، وتحفيز الالتزام الجماعي بتحقيق المستهدفات، وبناء ثقافة تنفيذ موحّدة عبر الهيئة."),
        ("home.cta.title", "ابدأ رحلة إدارتك الاستراتيجية"),
        ("home.cta.button", "الدخول إلى المنصة"),
        ("home.contact.title", "للتواصل مع إدارة الاستراتيجية والأداء المؤسسي"),
        ("home.contact.phone", "011 000 0000"),
        ("home.contact.email", "strategy@gac.gov.sa"),
        ("home.footer.note", "جميع الحقوق محفوظة — الهيئة العامة للمنافسة"),

        // Phase 19 — "كيف تعمل الجلسة" + "المبادئ التشغيلية" home blocks are now CMS-editable.
        ("home.session.title", "كيف تعمل الجلسة (٩٠ دقيقة)"),
        ("home.session.item1.title", "الحركة الأولى (٢٠ دقيقة) — التعريف"),
        ("home.session.item1.body", "قياس مرجعي مجهول، ثم عرض بيت الاستراتيجية (الرؤية، الرسالة، القيم، الركائز، الأهداف)."),
        ("home.session.item2.title", "الحركة الثانية (٤٠ دقيقة) — البناء الجماعي"),
        ("home.session.item2.body", "كل إدارة تبني خريطتها الاستراتيجية على لوحة تفاعلية بالربط بين عناصرها (مشاريع، مؤشرات، أدوار) وبين الأهداف والركائز."),
        ("home.session.item3.title", "الحركة الثالثة (٣٠ دقيقة) — الالتزام"),
        ("home.session.item3.body", "اختيار الالتزامات الجماعية، ربطها بعناصر الإطار، وتوقيع تطوعي على iPad."),
        ("home.session.item4.title", "إغلاق"),
        ("home.session.item4.body", "استبيان قصير ثم رسالة شكر بريدية في اليوم نفسه مع رابط الأسئلة الاختيارية."),
        ("home.principles.title", "المبادئ التشغيلية"),
        ("home.principles.item1", "الإدارات مكتملة، بدون تجميع مختلط بين إدارات."),
        ("home.principles.item2", "أيام التشغيل ٢ إلى ٥ فقط (لا أول ولا آخر يوم)."),
        ("home.principles.item3", "مكتب الاستراتيجية فقط هو المُيسّر؛ رئيس الإدارة راعٍ نشط بدور محدد سلفاً عبر خريطة رحلة رئيس الإدارة."),
        ("home.principles.item4", "التوقيع تطوعي، يُضاف على خريطة الإدارة في منطقة A3."),
        ("home.principles.item5", "جدار الالتزامات الرقمي يبقى مفتوحاً للحضور خلال الأسبوع، ثم يُعمَّم عند الإكمال."),
        ("journey.landing.intro", "أدخل رمز الدخول الخاص بإدارتك لبدء رحلة الاستراتيجية."),
        ("journey.stage1.intro", ""),
        ("journey.stage2.intro", "تعرّف على كيفية ارتباط عمل إدارتك بالرؤية والركائز الاستراتيجية."),
        ("journey.stage3.intro", "استعرض بيت الاستراتيجية: الرؤية والرسالة والقيم والركائز والأهداف."),
        ("journey.stage4.intro", "حدّد العناصر الاستراتيجية التي ترغب إدارتك بالمساهمة في تحقيقها."),
        ("journey.stage5.intro", "ارسم خريطة إدارتك ووقّعها مع فريقك."),
        ("journey.stage6.intro", "تهانينا على إتمام رحلة الاستراتيجية."),
        ("quiz.intro", "اختبار قصير لتعزيز فهم الفريق للاستراتيجية المؤسسية."),
        // Phase 19.15 — end-of-quiz survey link. Admin-editable at /Admin/Content.
        // The value is rendered as a QR code on the thank-you screen shown after
        // the user taps «إنهاء» on the quiz results page, and also surfaced
        // as a tappable link beneath the QR (so mobile users don't have to scan
        // their own phone). Replace the default with the real Microsoft Forms /
        // Google Forms URL from /Admin/Content.
        ("quiz.survey.url", "https://forms.office.com/r/replace-with-real-survey-id"),
        ("quiz.survey.title", "شكراً لمشاركتك"),
        ("quiz.survey.body", "ساعدنا على تحسين الجلسة — امسح الرمز أو اضغط الرابط لتعبئة استبيان قصير (دقيقتان)."),
        // Phase 19.25 — custom QR image upload. If quiz.survey.qr.useCustom == "true"
        // AND quiz.survey.qr.custom contains a non-empty data URI (data:image/png;base64,...
        // or similar), the admin-uploaded image is rendered in place of the auto-generated
        // QR from the URL. Toggling useCustom back to "false" instantly reverts to the
        // generated QR without losing the uploaded image (kept for fast re-enable).
        ("quiz.survey.qr.useCustom", "false"),
        ("quiz.survey.qr.custom", ""),
        // Phase 20.11 — expanded quiz question bank toggle. When "false" (default),
        // the public quiz uses the 5 static curated questions from QuizQuestionsProvider.
        // When "true", the quiz picks 5 random questions from the 200-template dynamic
        // bank generated from live DB data (Pillars/Objectives/Initiatives/Projects/KPIs
        // /Departments). Templates 4 & 5 are filtered by the user's DeptCode.
        ("quiz.bank.useExpanded", "false"),
        ("survey.thank_you", "شكراً لمشاركتك! تم استلام إجابتك بنجاح."),
        ("footer.copyright", "الهيئة العامة للمنافسة · منصة إطلاق استراتيجية الهيئة"),

        // Phase 19.21 (Fix 6) — strategy house core content (Vision/Mission/Values),
        // previously sourced only from appsettings StrategyContent and therefore not
        // editable without a redeploy. These keys are now admin-editable at
        // /Admin/Content; StrategyContentService overlays any stored value on top of the
        // appsettings default, so the journey "بيت الاستراتيجية" stages pick up edits
        // immediately with no per-view changes. The seeded defaults below mirror the
        // current appsettings copy verbatim.
        ("strategy.vision.ar", "بيئة منافسة رائدة عالمياً تسهم في الازدهار الاقتصادي"),
        ("strategy.mission.ar", "تمكين المنافسة العادلة من خلال تطبيق أحكام النظام بفعالية ودعم السياسات ورفع مستويات الوعي والامتثال بما يسهم في تحسين كفاءة الأسواق وتعزيز مصلحة المستهلك"),
        ("strategy.values.ar", "الشفافية، التعاون، التميز، العدالة، الابتكار"),
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
            var rows = db.PageContents.AsNoTracking().ToList();
            _cache.Clear();
            foreach (var row in rows)
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

        // Invalidate the in-memory cache under the same gate used by EnsureLoaded so
        // the next read repopulates fresh from the database. Without this the Singleton
        // served stale values until an app restart. We keep the in-memory caching
        // behaviour: reads still hit the cache; only writes force a one-time reload.
        lock (_gate)
        {
            _cache[key] = value;
            _loaded = false;
        }
    }
}
