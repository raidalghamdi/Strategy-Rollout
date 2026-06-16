using StrategyHouse.Domain.Enums;

namespace StrategyHouse.Web.Services;

// Phase 17 — the official survey's 8 questions are now hard-coded in the assembly,
// the same pattern as QuizQuestionsProvider. This is the single authoritative source
// for the question texts, types, choices and open-text categories. The questions are
// NOT editable from the admin UI (AddQuestion/EditQuestion/DeleteQuestion are disabled)
// and are NOT read from an admin-managed bank.
//
// Phase12SurveySeeder still materialises these definitions into the local SQLite
// Survey/SurveyQuestion rows on startup (idempotent, hash-guarded) so that responses,
// analytics and the final report — all of which key off stable question rows — keep
// working unchanged. The content, however, is owned here in source control.
public static class SurveyQuestionsProvider
{
    public const string SurveyTitle = "استبيان الاستراتيجية الجديدة للهيئة العامة للمنافسة";
    public const string SurveyDescription = "استبيان رسمي لقياس وضوح الاستراتيجية الجديدة ومدى الجاهزية للمساهمة فيها.";

    // A single question definition. Question 6's choices are resolved from the live
    // Initiatives at seed time (ChoicesFromInitiatives), so they are not listed here.
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

    // Map the official QuestionType to the legacy string Type the public form + analytics
    // still understand (Likert5 / MCQ / Text).
    public static string LegacyType(QuestionType t) => t switch
    {
        QuestionType.Likert5 => "Likert5",
        QuestionType.MultipleChoice => "MCQ",
        QuestionType.OpenText => "Text",
        _ => "Text",
    };

    // Generic fallback choices for Q6 when the Initiatives source is empty.
    public static string[] FallbackInitiativeChoices() => new[]
    {
        "تطوير الأنظمة واللوائح",
        "رفع الوعي بثقافة المنافسة",
        "تمكين الكفاءات المؤسسية",
        "تعزيز التحول الرقمي",
        "تطوير الشراكات الاستراتيجية",
    };
}
