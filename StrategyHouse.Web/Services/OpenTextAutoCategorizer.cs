// Phase 20.21 — Auto-categorisation for open-text survey answers.
// Phase 20.35 — keyword rules now come from the DB (SurveyQuestionCategories.KeywordsJson)
//                so analysts can edit them at /Admin/Survey/CategoryManager. The hard-coded
//                dictionaries below remain only as the seed/fallback when DB rows are empty.
//
// Implements the official "آلية قياس نتائج استبيان الموظفين البَعدي" rule sheet:
//   Q4 (التحديات)   → تنظيمية / تشريعية / موارد بشرية / أنظمة وتقنية / ثقافة مؤسسية
//   Q5 (القيم)      → matched to official values (الشفافية، التعاون، التميز، العدالة، الابتكار)
//                     answers outside the official set are bucketed as "قيمة جديدة"
//   Q7 (التطلعات)   → دور وطني / خدمات / بيئة عمل / تشريعات
//
// Strategy: Arabic keyword dictionaries with diacritic stripping + token containment.
// First matching category wins; ties broken by dictionary order (most specific first).
// If no keyword matches, the answer is left UNCATEGORISED (no DB row) so analysts can
// review it in /Admin/Survey/Categorize.
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Domain.Enums;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Infrastructure;

namespace StrategyHouse.Web.Services;

public class OpenTextAutoCategorizer
{
    private readonly ApplicationDbContext _db;
    public OpenTextAutoCategorizer(ApplicationDbContext db) { _db = db; }

    // ---- Public API ----------------------------------------------------------

    /// <summary>
    /// Classify a single answer for a specific question. Returns the chosen category
    /// label (must already exist in the question's category list — see SeedCategories)
    /// or null when no rule matches.
    /// </summary>
    public string? Classify(int questionOrder, string answer)
    {
        // Backward-compatible signature — falls back to the hard-coded rules table.
        // Prefer ClassifyWithRules() with DB-loaded rules wherever possible.
        if (string.IsNullOrWhiteSpace(answer)) return null;
        var rules = GetRules(questionOrder);
        if (rules == null) return null;
        return ClassifyWithRules(answer, rules);
    }

    /// <summary>
    /// Phase 20.35 — stateless classifier that accepts an explicit rule set (e.g., loaded from
    /// the DB). First match wins, in priority order; null if no keyword hits. Empty keyword
    /// arrays are skipped silently.
    /// </summary>
    public string? ClassifyWithRules(string answer, IReadOnlyList<(string Category, string[] Keywords)> rules)
    {
        if (string.IsNullOrWhiteSpace(answer) || rules == null || rules.Count == 0) return null;
        var norm = Normalize(answer);
        foreach (var (category, keywords) in rules)
        {
            if (keywords == null || keywords.Length == 0) continue;
            foreach (var kw in keywords)
            {
                var nkw = Normalize(kw);
                if (string.IsNullOrEmpty(nkw)) continue;
                if (norm.Contains(nkw)) return category;
            }
        }
        return null;
    }

    /// <summary>
    /// Phase 20.35 — build a priority-ordered rule list from a question's DB categories.
    /// Inactive categories are excluded. Categories with zero keywords are excluded so admin
    /// rules don't get diluted by empty buckets. Returns an empty list when there is nothing
    /// usable in the DB; callers should fall back to GetRules() when this happens.
    /// </summary>
    public static List<(string Category, string[] Keywords)> BuildRulesFromCategories(
        IEnumerable<SurveyQuestionCategory> categories)
    {
        var list = new List<(string, string[])>();
        foreach (var c in categories.Where(c => c.IsActive).OrderBy(c => c.Order))
        {
            string[] kws;
            try
            {
                kws = JsonSerializer.Deserialize<string[]>(c.KeywordsJson ?? "[]") ?? Array.Empty<string>();
            }
            catch { kws = Array.Empty<string>(); }
            if (kws.Length == 0) continue;
            list.Add((c.Name, kws));
        }
        return list;
    }

    /// <summary>
    /// Run auto-categorisation across every open-text answer of the active survey.
    /// Skips answers that already have an assignment (manual analyst overrides win).
    /// Returns (autoTagged, skipped, totalOpenAnswers).
    /// </summary>
    public async Task<(int AutoTagged, int Skipped, int Total)> CategorizeActiveSurveyAsync(CancellationToken ct = default)
    {
        var survey = await _db.Surveys
            .FirstOrDefaultAsync(s => s.TitleAr == Phase12SurveySeeder.SurveyTitle, ct);
        if (survey == null) return (0, 0, 0);

        // Phase 20.35 — load open-text questions with categories (for KeywordsJson rules).
        var openQs = await _db.SurveyQuestions
            .Include(q => q.Categories)
            .Where(q => q.SurveyId == survey.Id && q.QuestionType == QuestionType.OpenText)
            .OrderBy(q => q.Order)
            .ToListAsync(ct);
        if (openQs.Count == 0) return (0, 0, 0);

        var responses = await _db.SurveyResponses
            .Where(r => r.SurveyId == survey.Id)
            .ToListAsync(ct);

        var existing = await _db.OpenTextCategoryAssignments
            .Where(a => openQs.Select(q => q.Id).Contains(a.SurveyQuestionId))
            .ToListAsync(ct);
        var existingKey = existing
            .Select(a => (a.SurveyResponseId, a.SurveyQuestionId))
            .ToHashSet();

        // Phase 20.35 — pre-build per-question rule sets from DB, with hard-coded fallback.
        var perQuestionRules = new Dictionary<Guid, List<(string Category, string[] Keywords)>>();
        foreach (var q in openQs)
        {
            var dbRules = BuildRulesFromCategories(q.Categories);
            if (dbRules.Count == 0)
            {
                var hard = GetRules(q.Order);
                if (hard != null) dbRules = hard;
            }
            perQuestionRules[q.Id] = dbRules;
        }

        int auto = 0, skipped = 0, total = 0;
        foreach (var resp in responses)
        {
            var answers = ParseAnswers(resp.AnswersJson);
            foreach (var q in openQs)
            {
                if (!answers.TryGetValue(q.Id.ToString(), out var val)) continue;
                if (string.IsNullOrWhiteSpace(val)) continue;
                total++;
                if (existingKey.Contains((resp.Id, q.Id))) { skipped++; continue; }
                var cat = ClassifyWithRules(val, perQuestionRules[q.Id]);
                if (cat == null) continue;
                _db.OpenTextCategoryAssignments.Add(new OpenTextCategoryAssignment
                {
                    SurveyQuestionId = q.Id,
                    SurveyResponseId = resp.Id,
                    Category = cat,
                    AssignedByUserId = null, // null = auto-assigned
                });
                auto++;
            }
        }
        await _db.SaveChangesAsync(ct);
        return (auto, skipped, total);
    }

    // ---- Normalization -------------------------------------------------------

    /// <summary>
    /// Strip Arabic diacritics + tatweel, normalise alef/ya/ta-marbouta, lowercase,
    /// collapse whitespace. Used both on the answer and on every keyword.
    /// </summary>
    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            // Strip Arabic harakat (U+064B..U+0652) + tatweel U+0640
            if (ch >= '\u064B' && ch <= '\u0652') continue;
            if (ch == '\u0640') continue;
            char c = ch;
            // alef variants → bare alef
            if (c == 'أ' || c == 'إ' || c == 'آ' || c == 'ٱ') c = 'ا';
            // ya variants → ya
            if (c == 'ى' || c == 'ئ') c = 'ي';
            // ta marbouta → ha
            if (c == 'ة') c = 'ه';
            // hamza on waw
            if (c == 'ؤ') c = 'و';
            sb.Append(char.ToLowerInvariant(c));
        }
        var raw = sb.ToString();
        // collapse whitespace
        return string.Join(' ', raw.Split(new[] { ' ', '\t', '\n', '\r', '_' }, StringSplitOptions.RemoveEmptyEntries));
    }

    // ---- Rule sheet ----------------------------------------------------------

    /// <summary>
    /// Returns the ordered list of (category, keywords[]) for a given question Order,
    /// or null if the question is not auto-categorisable. Keywords are already
    /// normalised — must match output of Normalize().
    /// Order in the list = priority (most specific first).
    /// </summary>
    // Phase 20.33 (Comment 12) — made public so Analytics view can display keyword rules
    public static List<(string Category, string[] Keywords)>? GetRules(int order)
    {
        return order switch
        {
            4 => Q4_Challenges,
            5 => Q5_Values,
            7 => Q7_Aspirations,
            _ => null,
        };
    }

    // === Q4 — التحديات (5 محاور رسمية من ورقة الآلية) ============================
    // Original sheet: "تنظيمية، تشريعية، موارد بشرية، أنظمة وتقنية، ثقافة مؤسسية"
    // Each keyword is pre-normalised (no harakat, alef/ya/ta-marbouta unified).
    private static readonly List<(string, string[])> Q4_Challenges = new()
    {
        // أنظمة وتقنية — قبل "تنظيمية" لأن كلمة "النظام" قد تظهر في كليهما
        ("أنظمة وتقنية", new[] {
            "تقني", "تقنيه", "تقنيا", "تكنولوجي", "رقمي", "رقمنه",
            "نظام", "انظمه", "النظم", "اتمته", "اتمتت", "اتمتة",
            "منصه", "منصات", "برامج", "تطبيق", "البنيه التقنيه", "بنيه تقنيه",
            "البنيه التحتيه", "بنيه تحتيه", "البنيه التحتي", "بنيه تحتي",
            "البنيه التحتيه الالكترونيه", "الاسواق الرقميه", "اسواق رقميه",
            "ذكاء اصطناعي", "ai", "بيانات", "قاعده بيانات",
        }),
        // ثقافة مؤسسية
        ("ثقافة مؤسسية", new[] {
            "ثقافه", "ثقافه مؤسسيه", "ثقافيه", "وعي", "الوعي", "ادراك",
            "وعي المستفيد", "زياده وعي", "زيادة وعي",
            "الاستراتيجيه حاضره", "عكس الاستراتيجيه", "السلوك", "سلوك",
            "فعاليات", "في جميع الفعاليات",
            "مقاومه التغيير", "مقاومه التغير", "ممانعه", "روح الفريق",
            "بيئه العمل", "بيئه عمل", "تعاون داخلي", "تواصل داخلي",
            "ولاء", "انتماء",
        }),
        // موارد بشرية
        ("موارد بشرية", new[] {
            "موارد بشريه", "موارد البشريه", "بشري", "كوادر", "كفاءات",
            "تدريب", "تطوير الموظف", "موظف", "موظفين", "استقطاب",
            "عدد الموظفين", "نقص الموظفين", "نقص الكوادر",
            "خبرات", "مهارات", "رواتب", "حوافز", "تأهيل",
        }),
        // تشريعية
        ("تشريعية", new[] {
            "تشريع", "تشريعات", "تشريعي", "قانون", "قوانين", "قانوني",
            "لائحه", "لوائح", "نظام المنافسه", "العقوبات", "صلاحيات قانونيه",
        }),
        // تنظيمية (catch-all)
        ("تنظيمية", new[] {
            "تنظيمي", "تنظيميه", "اجراءات", "اجراء", "بيروقراطيه", "بيروقراطي",
            "هيكل", "هيكله", "هيكليه", "تنسيق", "تكامل", "حوكمه",
            "صلاحيات", "تفويض", "ادارات", "ادارة العمل",
            "تعاون مع القطاع", "القطاع الخاص", "الجهات الخارجيه",
            "شراكات", "تنسيق مع", "تكامل مع",
        }),
        // أخرى (last-resort bucket for off-axis but valid answers)
        ("أخرى", new[] {
            "ميزانيه", "ميازنيه", "ميازنه", "المياازنيه", "الماليه",
            "تمويل", "تمويلي", "موارد ماليه",
            "حملات تسويقيه", "تسويق", "تسويقي", "اعلامي", "اعلام",
            "المنشات", "الاسواق", "رقابه الاسواق", "رقابه",
            "الاحتكار", "احتكار", "المنافسه عادله", "منافسه عادله",
        }),
    };

    // === Q5 — القيم: تطابق مع القيم الرسمية المعتمدة =============================
    // Five official values: الشفافية، التعاون، التميز، العدالة، الابتكار
    // Anything outside is labelled "قيمة جديدة" so the gap is visible.
    private static readonly List<(string, string[])> Q5_Values = new()
    {
        ("الشفافية", new[] { "شفافيه", "شفافيت", "شفاف", "وضوح", "افصاح", "مصداقيه" }),
        ("التعاون",   new[] { "تعاون", "تعاوني", "شراكه", "عمل جماعي", "فريق واحد", "تكاتف" }),
        ("التميز",    new[] { "تميز", "تميُّز", "التميز", "اتقان", "جوده", "كفاءه", "تطوير" }),
        ("العدالة",   new[] { "عداله", "عادل", "مساواه", "انصاف", "نزاهه" }),
        ("الابتكار",  new[] { "ابتكار", "ابداع", "ابداعي", "تجديد", "ابتكاري", "تطوير مبتكر" }),
        // Fallback: anything not matching the five official values
        ("قيمة جديدة", new[] {
            "مرونه", "احترافيه", "اخلاص", "مسؤوليه", "مسؤوليه مجتمعيه",
            "التزام", "امانه", "احترام", "رياده", "ريادي", "ولاء",
            "انجاز", "تمكين", "ثقه", "كرامه", "قياده",
        }),
    };

    // === Q7 — التطلعات (4 محاور رسمية) ==========================================
    // Original sheet: "دور وطني، خدمات، بيئة عمل، تشريعات"
    private static readonly List<(string, string[])> Q7_Aspirations = new()
    {
        // تشريعات — قبل "دور وطني" لأن "نظام/قانون" قد يلتقطها الأول
        ("تشريعات", new[] {
            "تشريع", "تشريعات", "قانون", "قوانين", "لائحه", "لوائح",
            "نظام المنافسه", "العقوبات", "تحديث لائحه",
            "نطاق تطبيق نظام", "نطاق تطبيق", "برنامج الامتثال", "الامتثال",
            "منع الاحتكار", "تجريم الاحتكار", "مكافحه الاحتكار",
        }),
        // خدمات
        ("خدمات", new[] {
            "خدمه", "خدمات", "خدمات الكترونيه", "منصه الكترونيه",
            "قنوات خدمه", "تجربه المستفيد", "مستفيد", "مستهلك",
            "بلاغات", "شكاوى", "بت في البلاغات",
            "اتمته الاجراءات", "اتمته جميع الاجراءات", "تسهيل الاجراءات",
            "القضاء على البيروقراطيه", "بيروقراطيه",
        }),
        // بيئة عمل
        ("بيئه عمل", new[] {
            "بيئه عمل", "بيئه العمل", "تطوير الموظفين", "تطوير الموظف",
            "تدريب", "كوادر", "كفاءات", "حوافز", "رفاهيه", "سعاده",
            "تمكين الموظف", "ثقافه مؤسسيه",
            "رفع قيمه الموظفين", "تقدير الموظفين", "تقديرهم",
            "عدد الموظفين", "نمو الهيئه", "الاختصاصات",
        }),
        // دور وطني (catch-all last — أوسع المحاور)
        ("دور وطني", new[] {
            "دور وطني", "ريادي", "قياده", "تأثير", "مرجعيه", "اقليمي", "عالمي",
            "رؤيه 2030", "رؤية 2030", "رؤيه ٢٠٣٠", "اقتصاد وطني",
            "تنافسيه", "تنافسيه السوق", "حمايه المستهلك", "حمايه المنافسه",
            "جذب الاستثمار", "ناتج محلي", "تنميه", "استراتيجي",
            "تحقيق الاهداف", "تحقيق الهدف", "العمل الاستباقي", "استباقي",
            "نقله نوعيه", "نقله نوعيه في العمليات", "تطور نوعي",
            "رائده", "رائده في", "رائده في مجال المنافسه",
            "المنطقه العربيه", "عداله في الاسواق", "عداله الاسواق",
            "نشر الوعي", "الوعي بسياسات المنافسه", "حملات تسويقيه",
            "تحقيق اهدافها", "اهدافها الطموحه", "اهدافها",
            "انخفاض عدد المخالفات", "المخالفات", "منتجات متعدده", "اسعار مختلفه",
        }),
        // أخرى — last-resort bucket for off-axis but valid aspirations
        ("أخرى", new[] {
            "متفائل", "تفاؤل", "امل", "التوفيق", "نتمنى التوفيق",
            "التميز والابداع", "تميز وابداع", "تميز", "ابداع",
            "الاحترافيه", "احترافيه", "جوده", "تطوير",
        }),
    };

    // ---- AnswersJson parsing -------------------------------------------------

    private sealed class _Item { public string? qid { get; set; } public string? value { get; set; } public string? notes { get; set; } }

    private static Dictionary<string, string> ParseAnswers(string json)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return dict;
        try
        {
            var items = System.Text.Json.JsonSerializer.Deserialize<List<_Item>>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (items == null) return dict;
            foreach (var it in items)
            {
                if (string.IsNullOrWhiteSpace(it?.qid)) continue;
                var v = !string.IsNullOrWhiteSpace(it.value) ? it.value!
                      : !string.IsNullOrWhiteSpace(it.notes) ? it.notes!
                      : null;
                if (v == null) continue;
                dict[it.qid!] = v;
            }
        }
        catch { /* ignore malformed entries */ }
        return dict;
    }
}
