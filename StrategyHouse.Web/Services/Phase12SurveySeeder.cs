using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Domain.Enums;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 12 — replaces the survey question bank with the 8 official questions from the
// GAC Excel spec, with their measurement metric/formula and predefined open-text
// categories. Idempotent: a stable hash of the 8-question definition set is stored in
// PageContents; on startup, if the live bank's hash != target hash, the old questions,
// choices, responses and category data are wiped and the 8 new questions are inserted.
public static class Phase12SurveySeeder
{
    public const string SurveyTitle = "استبيان الاستراتيجية الجديدة للهيئة العامة للمنافسة";
    private const string HashKey = "survey.seed.hash";

    // ---- Canonical definition of the 8 official questions (authoritative source) ----

    public record QDef(
        int N,
        QuestionType Type,
        string Text,
        string Metric,
        string Formula,
        string[]? Choices = null,
        string[]? Categories = null,
        bool ChoicesFromInitiatives = false);

    public static List<QDef> Definitions() => new()
    {
        new QDef(1, QuestionType.Likert5,
            "بعد البرامج التعريفية وورش العمل، ما مدى وضوح توجهات واستراتيجية الهيئة لديك على المدى الطويل؟",
            "نسبة من أجابوا بدرجتي وضوح عالٍ أو عالٍ جدًا (4 و5)",
            "مطّلع بشكل كامل = 5 · مطّلع بشكل جيد = 4 · مطّلع بشكل لا بأس به = 3 · مطّلع بشكل محدود = 2 · غير مطّلع إطلاقًا = 1\nنسبة الوضوح العالي = COUNTIF(الدرجات,\">=4\") / COUNT(الدرجات)"),

        new QDef(2, QuestionType.MultipleChoice,
            "من وجهة نظرك بعد الاطلاع على الاستراتيجية الجديدة، ما هي الغاية الأساسية من وجود الهيئة العامة للمنافسة؟",
            "نسبة اختيار كل خيار",
            "نسبة الاختيار = COUNTIF(نطاق_الإجابات,\"نص الخيار\") / COUNT(نطاق_الإجابات)",
            Choices: new[]
            {
                "تمكين المنافسة في السوق وحماية المستهلك",
                "منع الممارسات الاحتكارية وضمان عدالة السوق",
                "تنظيم بيئة الأعمال وتعزيز الشفافية",
                "دعم الاقتصاد الوطني ورفع الإنتاجية",
                "تعزيز ثقة الاستثمار وجاذبيته",
            }),

        new QDef(3, QuestionType.MultipleChoice,
            "من وجهة نظرك، ما هي أبرز نقطة قوة للهيئة من بين الآتي؟",
            "نسبة اختيار كل قيمة كنقطة قوة (تُرتَّب تنازليًا)",
            "نسبة الاختيار = COUNTIF(نطاق_الإجابات,\"اسم القيمة\") / COUNT(نطاق_الإجابات)",
            Choices: new[] { "الشفافية", "التعاون", "التميز", "العدالة", "الابتكار" }),

        new QDef(4, QuestionType.OpenText,
            "ما هي أبرز التحديات التي قد تواجهها الهيئة في تحقيق أهداف الاستراتيجية الجديدة؟",
            "أهم محاور التحديات (تنظيمية، تشريعية، موارد بشرية، أنظمة وتقنية، ثقافة مؤسسية…)",
            "تصنيف كل إجابة يدويًا إلى فئة ثم حساب تكرار كل فئة (PivotTable / COUNTIF)",
            Categories: new[] { "تنظيمية", "تشريعية", "موارد بشرية", "أنظمة وتقنية", "ثقافة مؤسسية", "أخرى" }),

        new QDef(5, QuestionType.OpenText,
            "من وجهة نظرك، ما هي أهم القيم التي تعبّر عن الهيئة وتسهم في تحقيق استراتيجيتها؟ (اذكر 3–5 قيم)",
            "القيم الأكثر تكرارًا ومدى تطابقها مع القيم الرسمية المعتمدة",
            "تجميع القيم المتشابهة (الشفافية / الوضوح معًا) وتصنيفها إلى قيمة معتمدة / قيمة جديدة ثم عدّ التكرار",
            Categories: new[] { "قيمة معتمدة", "قيمة جديدة", "شفافية", "عدالة", "تميز", "تعاون", "ابتكار", "أخرى" }),

        new QDef(6, QuestionType.MultipleChoice,
            "من وجهة نظرك، ما هي أهم مبادرة لدعم استراتيجية الهيئة؟",
            "نسبة اختيار كل مبادرة",
            "نسبة الاختيار = COUNTIF(نطاق_الإجابات,\"نص المبادرة\") / COUNT(نطاق_الإجابات)",
            ChoicesFromInitiatives: true),

        new QDef(7, QuestionType.OpenText,
            "ما هي تطلعاتك المستقبلية حول الهيئة في ضوء الاستراتيجية الجديدة؟",
            "أهم التطلعات مصنَّفة إلى محاور (دور وطني، خدمات، بيئة عمل، تشريعات…)",
            "تصنيف كل إجابة إلى فئة مناسبة ثم عدّ تكرار كل فئة (PivotTable)",
            Categories: new[] { "دور وطني", "خدمات", "بيئة عمل", "تشريعات", "أخرى" }),

        new QDef(8, QuestionType.Likert5,
            "إلى أي مدى تشعر بالقدرة على المساهمة فعليًا في تحقيق أهداف الاستراتيجية ضمن نطاق عملك؟",
            "متوسط الشعور بالقدرة على المساهمة + نسبة من يشعرون بقدرة عالية (4 و5)",
            "بدرجة عالية جدًا = 5 · بدرجة عالية = 4 · بدرجة متوسطة = 3 · بدرجة منخفضة = 2 · لا يمكنني المساهمة = 1\nالمتوسط = AVERAGE(الدرجات) · نسبة القدرة العالية = COUNTIF(الدرجات,\">=4\") / COUNT(الدرجات)"),
    };

    // Map the official QuestionType to the legacy string Type the public form + old
    // analytics still understand (Likert5 / MCQ / Text).
    public static string LegacyType(QuestionType t) => t switch
    {
        QuestionType.Likert5 => "Likert5",
        QuestionType.MultipleChoice => "MCQ",
        QuestionType.OpenText => "Text",
        _ => "Text",
    };

    public static async Task SeedAsync(ApplicationDbContext db)
    {
        var defs = Definitions();
        var initiativeChoices = await ResolveInitiativeChoicesAsync(db);
        var target = ComputeHash(defs, initiativeChoices);

        var current = await db.PageContents.FirstOrDefaultAsync(p => p.Key == HashKey);
        var survey = await db.Surveys.FirstOrDefaultAsync(s => s.TitleAr == SurveyTitle);

        // Already current and the survey exists → nothing to do.
        if (survey != null && current?.ValueAr == target) return;

        await ApplyAsync(db, defs, initiativeChoices, target);
    }

    // Forced reseed (admin "إعادة بذر الاستبيان" button): wipe + reinsert unconditionally.
    public static async Task ReseedAsync(ApplicationDbContext db)
    {
        var defs = Definitions();
        var initiativeChoices = await ResolveInitiativeChoicesAsync(db);
        var target = ComputeHash(defs, initiativeChoices);
        await ApplyAsync(db, defs, initiativeChoices, target);
    }

    private static async Task ApplyAsync(ApplicationDbContext db, List<QDef> defs, string[] initiativeChoices, string targetHash)
    {
        // Phase 12 replaces the survey: retire any other active survey so respondents
        // only see the official 8-question bank.
        await db.Surveys.Where(s => s.TitleAr != SurveyTitle && s.IsActive)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.IsActive, false));

        // Find or create the official survey.
        var survey = await db.Surveys.FirstOrDefaultAsync(s => s.TitleAr == SurveyTitle);
        if (survey == null)
        {
            survey = new Survey
            {
                TitleAr = SurveyTitle,
                DescriptionAr = "استبيان رسمي لقياس وضوح الاستراتيجية الجديدة ومدى الجاهزية للمساهمة فيها.",
                Audience = "Public",
                IsActive = true,
                PublicToken = await UniqueTokenAsync(db),
            };
            db.Surveys.Add(survey);
            await db.SaveChangesAsync();
        }

        // Wipe old questions of this survey + their categories + this survey's responses
        // and any category assignments. Responses are deleted (acceptable per plan) since
        // they answer a now-removed question bank.
        var oldQuestionIds = await db.SurveyQuestions.Where(q => q.SurveyId == survey.Id).Select(q => q.Id).ToListAsync();
        if (oldQuestionIds.Count > 0)
            await db.OpenTextCategoryAssignments.Where(a => oldQuestionIds.Contains(a.SurveyQuestionId)).ExecuteDeleteAsync();
        await db.SurveyResponses.Where(r => r.SurveyId == survey.Id).ExecuteDeleteAsync();
        // SurveyQuestionCategories cascade from the question delete.
        await db.SurveyQuestions.Where(q => q.SurveyId == survey.Id).ExecuteDeleteAsync();

        // Insert the 8 new questions.
        foreach (var d in defs)
        {
            var choices = d.ChoicesFromInitiatives ? initiativeChoices : d.Choices;
            var q = new SurveyQuestion
            {
                SurveyId = survey.Id,
                Order = d.N,
                QuestionType = d.Type,
                Type = LegacyType(d.Type),
                QuestionAr = d.Text,
                MeasurementMetric = d.Metric,
                MeasurementFormula = d.Formula,
                IsRequired = d.Type != QuestionType.OpenText,
                OptionsJson = choices is { Length: > 0 } ? JsonSerializer.Serialize(choices) : null,
            };
            db.SurveyQuestions.Add(q);
            await db.SaveChangesAsync();

            if (d.Categories is { Length: > 0 })
            {
                int order = 1;
                foreach (var cat in d.Categories)
                    db.SurveyQuestionCategories.Add(new SurveyQuestionCategory { SurveyQuestionId = q.Id, Name = cat, Order = order++ });
            }
        }
        await db.SaveChangesAsync();

        // Record the hash.
        var row = await db.PageContents.FirstOrDefaultAsync(p => p.Key == HashKey);
        if (row == null)
            db.PageContents.Add(new PageContent { Key = HashKey, ValueAr = targetHash, UpdatedAt = DateTime.UtcNow });
        else { row.ValueAr = targetHash; row.UpdatedAt = DateTime.UtcNow; }
        await db.SaveChangesAsync();
    }

    // Top strategic initiatives by code order, for Q6 choices. Falls back to a generic
    // set when the Initiative table is empty (fresh DB before strategy seed).
    private static async Task<string[]> ResolveInitiativeChoicesAsync(ApplicationDbContext db)
    {
        var names = await db.Initiatives
            .Where(i => i.InitiativeName != null && i.InitiativeName != "")
            .OrderBy(i => i.InitiativeCode)
            .Select(i => i.InitiativeName!)
            .Take(8)
            .ToListAsync();

        if (names.Count >= 2) return names.ToArray();

        return new[]
        {
            "تطوير الأنظمة واللوائح",
            "رفع الوعي بثقافة المنافسة",
            "تمكين الكفاءات المؤسسية",
            "تعزيز التحول الرقمي",
            "تطوير الشراكات الاستراتيجية",
        };
    }

    private static string ComputeHash(List<QDef> defs, string[] initiativeChoices)
    {
        var sb = new StringBuilder();
        sb.Append("v1|").Append(SurveyTitle).Append('|');
        foreach (var d in defs)
        {
            sb.Append(d.N).Append(':').Append((int)d.Type).Append(':').Append(d.Text).Append('|');
            var choices = d.ChoicesFromInitiatives ? initiativeChoices : d.Choices;
            if (choices != null) sb.Append("c=").Append(string.Join(",", choices)).Append('|');
            if (d.Categories != null) sb.Append("k=").Append(string.Join(",", d.Categories)).Append('|');
        }
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }

    private static async Task<string> UniqueTokenAsync(ApplicationDbContext db)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var bytes = RandomNumberGenerator.GetBytes(16);
            var sb = new StringBuilder(16);
            foreach (var b in bytes) sb.Append(chars[b % chars.Length]);
            var token = sb.ToString();
            if (!await db.Surveys.AnyAsync(s => s.PublicToken == token)) return token;
        }
        return Guid.NewGuid().ToString("N")[..16];
    }
}
