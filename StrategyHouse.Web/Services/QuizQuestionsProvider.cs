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

        // Phase 18 — refreshed question set tied to the redesigned journey theme:
        // how every employee's role connects, through initiatives → objectives →
        // pillars, up to the Authority's vision. All five are gradeable.
        return new List<QuizQuestion>
        {
            Mcq("ما العنصر الأعلى في البيت الاستراتيجي الذي تتصل به كل المبادرات في النهاية؟", new[]
            {
                "الرؤية", "المشروع", "المؤشر", "الميزانية",
            }, 0, "كل مبادرة ترتبط بهدف وركيزة وصولاً إلى الرؤية."),
            Mcq("ما الترتيب الصحيح لسلسلة الربط من دورك حتى الرؤية؟", new[]
            {
                "المبادرة ← الهدف ← الركيزة ← الرؤية",
                "الرؤية ← المبادرة ← الهدف ← الركيزة",
                "الهدف ← الرؤية ← الركيزة ← المبادرة",
                "الركيزة ← المبادرة ← الرؤية ← الهدف",
            }, 0, "تبدأ السلسلة من المبادرة وتنتهي عند الرؤية."),
            Mcq("ما الذي يربط عمل إدارتك اليومي بالاستراتيجية المؤسسية؟", new[]
            {
                "المبادرات والأهداف", "عدد الاجتماعات", "حجم الميزانية فقط", "الموقع الجغرافي",
            }, 0, "المبادرات والأهداف هي جسر الربط بين العمل اليومي والاستراتيجية."),
            TrueFalse("كل موظف في الهيئة يسهم — من خلال دوره — في تحقيق الرؤية.", true,
                "نعم؛ تتصل أدوار الجميع بالرؤية عبر سلسلة الربط."),
            Mcq("أي ركيزة من ركائز الاستراتيجية تختص بحماية الأسواق من الممارسات الاحتكارية؟", new[]
            {
                "تمكين المنافسة", "حماية المنافسة", "الشراكة والتعاون", "الكفاءة المؤسسية",
            }, 1, "ركيزة حماية المنافسة تُعنى بمنع الممارسات الاحتكارية."),
        };
    }
}
