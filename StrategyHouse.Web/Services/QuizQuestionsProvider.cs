using System.Text.Json;
using StrategyHouse.Domain.Entities;

namespace StrategyHouse.Web.Services;

// Phase 16 — quiz questions are now hard-coded in the assembly instead of being
// read from the database. This removes the dependency on an admin approving a
// seeded bank (the previous root cause of the empty /Quiz/Start page) and keeps
// the question texts under source control. The texts below are carried over
// verbatim from the previous approved demo bank (AssessmentSeeder.BuildDemoQuestions).
public static class QuizQuestionsProvider
{
    private static readonly List<QuizQuestion> _questions = Build();

    // Stable list of approved, active General questions used by the public quiz.
    public static IReadOnlyList<QuizQuestion> All => _questions;

    // Returns up to {count} questions in a fresh random order for each attempt.
    public static List<QuizQuestion> GetRandom(int count)
    {
        var rnd = new Random(Random.Shared.Next());
        return _questions.OrderBy(_ => rnd.Next()).Take(count).ToList();
    }

    private static List<QuizQuestion> Build()
    {
        QuizQuestion Mcq(string q, string[] opts, int correct, string? expl = null) => new()
        {
            Id = Guid.NewGuid(),
            Scope = "General",
            QuestionType = "MCQ",
            QuestionAr = q,
            OptionsJson = JsonSerializer.Serialize(opts),
            CorrectIndex = correct,
            ExplanationAr = expl,
            IsApproved = true,
            IsActive = true,
            Source = "Static",
        };
        QuizQuestion TrueFalse(string q, bool answer, string? expl = null) => new()
        {
            Id = Guid.NewGuid(),
            Scope = "General",
            QuestionType = "TrueFalse",
            QuestionAr = q,
            OptionsJson = JsonSerializer.Serialize(new[] { "صحيح", "خطأ" }),
            CorrectIndex = answer ? 0 : 1,
            ExplanationAr = expl,
            IsApproved = true,
            IsActive = true,
            Source = "Static",
        };

        return new List<QuizQuestion>
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
    }
}
