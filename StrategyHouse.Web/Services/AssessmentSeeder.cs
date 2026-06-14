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
    public static async Task RunAsync(ApplicationDbContext db, QuizGeneratorService quiz)
    {
        await SeedProgrammeSurveyAsync(db);
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
