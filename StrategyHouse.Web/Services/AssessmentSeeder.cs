using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 4 — seeds the auto-generated quiz bank and one programme survey on startup.
// Runs AFTER the strategy seed so the quiz generator has data to build from.
// Idempotent: quiz skips if bank already full; survey skips if its title exists.
public static class AssessmentSeeder
{
    private const string ProgrammeSurveyTitle = "تقييم برنامج الاستراتيجية 2025-2030";

    // Phase 5: quiz auto-seed removed — production starts with 0 questions until an
    // admin clicks "Regenerate" or adds questions manually. Survey seed remains.
    // Phase 6: seed 5 hand-crafted demo questions so the quiz is never empty.
    public static async Task RunAsync(ApplicationDbContext db, QuizGeneratorService quiz)
    {
        await EnsureDemoQuizAsync(db);
        await SeedProgrammeSurveyAsync(db);
    }

    // Phase 6 — idempotent: only seeds when the bank is completely empty so admin
    // curation is never overwritten. All five are approved + active demo questions.
    // Phase 10.2 — exposed so startup and the admin reseed endpoint share one safety net.
    public static async Task<bool> EnsureDemoQuizAsync(ApplicationDbContext db)
    {
        if (await db.QuizQuestions.AnyAsync()) return false;
        db.QuizQuestions.AddRange(BuildDemoQuestions());
        await db.SaveChangesAsync();
        return true;
    }

    // Phase 10 — full reset: wipe all attempts + questions, then reseed the 5 demo
    // questions so analytics start from zero. Used by the admin reset action and the
    // optional Quiz:ResetOnStartup flag.
    public static async Task ResetQuizAsync(ApplicationDbContext db)
    {
        await db.QuizAttempts.ExecuteDeleteAsync();
        await db.QuizQuestions.ExecuteDeleteAsync();
        db.QuizQuestions.AddRange(BuildDemoQuestions());
        await db.SaveChangesAsync();
    }

    // The five hand-crafted demo questions (approved + active).
    public static List<QuizQuestion> BuildDemoQuestions()
    {
        QuizQuestion Mcq(string q, string[] opts, int correct) => new()
        {
            Scope = "General",
            QuestionType = "MCQ",
            QuestionAr = q,
            OptionsJson = JsonSerializer.Serialize(opts),
            CorrectIndex = correct,
            IsApproved = true,
            IsActive = true,
            Source = "Demo",
        };
        QuizQuestion TrueFalse(string q, bool answer) => new()
        {
            Scope = "General",
            QuestionType = "TrueFalse",
            QuestionAr = q,
            OptionsJson = JsonSerializer.Serialize(new[] { "صحيح", "خطأ" }),
            CorrectIndex = answer ? 0 : 1,
            IsApproved = true,
            IsActive = true,
            Source = "Demo",
        };

        var demo = new List<QuizQuestion>
        {
            Mcq("ما هي رؤية الهيئة العامة للمنافسة؟", new[]
            {
                "بيئة منافسة رائدة عالمياً تسهم في الازدهار الاقتصادي",
                "تحقيق الريادة في الاقتصاد السعودي",
                "رفع كفاءة الأسواق المحلية",
                "تطوير منظومة المنافسة",
            }, 0),
            Mcq("كم عدد ركائز الاستراتيجية في الهيئة العامة للمنافسة؟", new[]
            {
                "3", "4", "5", "6",
            }, 2),
            Mcq("أي من التالي ليس من قيم الهيئة؟", new[]
            {
                "الشفافية", "العدالة", "الابتكار", "الكفاءة",
            }, 3),
            TrueFalse("تعمل الهيئة العامة للمنافسة على تطبيق أحكام نظام المنافسة في المملكة.", true),
            Mcq("أي ركيزة من ركائز الاستراتيجية تختص بحماية الأسواق من الممارسات الاحتكارية؟", new[]
            {
                "تمكين المنافسة", "حماية المنافسة", "الشراكة والتعاون", "الكفاءة المؤسسية",
            }, 1),
        };

        return demo;
    }

    private static async Task SeedProgrammeSurveyAsync(ApplicationDbContext db)
    {
        if (await db.Surveys.AnyAsync(s => s.TitleAr == ProgrammeSurveyTitle)) return;

        var survey = new Survey
        {
            TitleAr = ProgrammeSurveyTitle,
            DescriptionAr = "شاركنا رأيك حول برنامج إطلاق الاستراتيجية المؤسسية ورحلة الإدارات.",
            Audience = "Public",
            IsActive = true,
            PublicToken = await UniqueTokenAsync(db),
        };
        db.Surveys.Add(survey);
        await db.SaveChangesAsync();

        var qs = new List<SurveyQuestion>();
        int order = 1;
        void Likert(string text) => qs.Add(new SurveyQuestion { SurveyId = survey.Id, Order = order++, Type = "Likert5", QuestionAr = text });
        void YesNo(string text) => qs.Add(new SurveyQuestion { SurveyId = survey.Id, Order = order++, Type = "YesNo", QuestionAr = text });
        void Text(string text) => qs.Add(new SurveyQuestion { SurveyId = survey.Id, Order = order++, Type = "Text", QuestionAr = text, IsRequired = false });
        void Mcq(string text, params string[] opts) => qs.Add(new SurveyQuestion { SurveyId = survey.Id, Order = order++, Type = "MCQ", QuestionAr = text, OptionsJson = JsonSerializer.Serialize(opts) });

        Likert("كانت فعالية إطلاق الاستراتيجية منظّمة وواضحة.");
        Likert("ساعدتني الرحلة على فهم الاستراتيجية المؤسسية بشكل أفضل.");
        Likert("كانت خريطة استراتيجية إدارتي معبّرة عن أولوياتنا.");
        Likert("شعرت بأن مشاركتي وتعهداتي ذات أثر حقيقي.");
        Likert("كانت الأدوات الرقمية (الخرائط، الاختبار) سهلة الاستخدام.");
        Mcq("ما القناة التي علمت من خلالها بالفعالية؟", "البريد الإلكتروني", "المدير المباشر", "زميل عمل", "وسائل التواصل الداخلية", "أخرى");
        Mcq("ما أكثر جزء استفدت منه؟", "العرض التعريفي", "ورشة بناء الخريطة", "التعهدات الشخصية", "الاختبار المعرفي", "النقاشات الجماعية");
        YesNo("هل ترغب بالمشاركة في فعاليات مماثلة مستقبلاً؟");
        YesNo("هل تشعر بوضوح دورك في تحقيق الاستراتيجية؟");
        Text("ما أبرز ما أعجبك في البرنامج؟");
        Text("ما الذي تقترح تحسينه في النسخة القادمة؟");
        Text("أي ملاحظات إضافية تود مشاركتها؟");

        db.SurveyQuestions.AddRange(qs);
        await db.SaveChangesAsync();
    }

    private static async Task<string> UniqueTokenAsync(ApplicationDbContext db)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var bytes = RandomNumberGenerator.GetBytes(16);
            var sb = new System.Text.StringBuilder(16);
            foreach (var b in bytes) sb.Append(chars[b % chars.Length]);
            var token = sb.ToString();
            if (!await db.Surveys.AnyAsync(s => s.PublicToken == token)) return token;
        }
        return Guid.NewGuid().ToString("N")[..16];
    }
}
