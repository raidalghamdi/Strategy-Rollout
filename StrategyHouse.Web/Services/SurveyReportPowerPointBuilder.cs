using StrategyHouse.Domain.Enums;
using StrategyHouse.Web.Models;
using static StrategyHouse.Web.Services.PptxDeck;

namespace StrategyHouse.Web.Services;

// Phase 14 — branded .pptx export of the official survey final report, rebuilt on
// ShapeCrawler. Title slide, overview, one slide per question (Q1..Q8) with the question
// text + metric/breakdown, and a conclusions slide. GAC navy/gold, RTL, Cairo latin font.
public class SurveyReportPowerPointBuilder
{
    public byte[] Build(FinalReportViewModel m)
    {
        var deck = new PptxDeck();
        var period = m.DateFrom != null ? $"{m.DateFrom:yyyy-MM-dd} ← {m.DateTo:yyyy-MM-dd}" : "—";

        deck.TitleSlide("التقرير النهائي للاستبيان الرسمي", new[]
        {
            L(m.SurveyTitle, 18, false, Gold),
            L("الهيئة العامة للمنافسة", 16, false, PaleBlue),
            L("فترة الجمع: " + period, 13, false, PaleBlue),
        });

        OverviewSlide(deck, m);
        foreach (var c in m.Cards.OrderBy(x => x.Order))
            QuestionSlide(deck, c, m);
        ConclusionsSlide(deck, m);

        return deck.ToBytes();
    }

    private static void OverviewSlide(PptxDeck deck, FinalReportViewModel m)
    {
        var q1 = m.Cards.FirstOrDefault(c => c.Order == 1)?.Likert;
        var q8 = m.Cards.FirstOrDefault(c => c.Order == 8)?.Likert;
        var lines = new List<Line>
        {
            L($"إجمالي الردود: {m.TotalResponses}", 20, true, Gold),
            L($"عدد الأسئلة: {m.Cards.Count}", 14, false, White),
        };
        if (q1 is { Total: > 0 })
            lines.Add(L($"وضوح الاستراتيجية (س1): متوسط {q1.Mean:0.##}/5 · العالية {q1.PctHigh:0.#}%", 14, false, White));
        if (q8 is { Total: > 0 })
            lines.Add(L($"القدرة على المساهمة (س8): متوسط {q8.Mean:0.##}/5 · العالية {q8.PctHigh:0.#}%", 14, false, White));
        foreach (var t in m.Takeaways.Take(4))
            lines.Add(L($"• {t}", 12, false, White));
        deck.ContentSlide("النظرة العامة", lines);
    }

    private static void QuestionSlide(PptxDeck deck, QuestionCard c, FinalReportViewModel m)
    {
        var lines = new List<Line> { L(c.QuestionAr, 16, true, Gold) };
        switch (c.Type)
        {
            case QuestionType.Likert5:
                var l = c.Likert;
                if (l == null || l.Total == 0) { lines.Add(L("لا توجد إجابات بعد.", 14, false, White)); break; }
                lines.Add(L($"المتوسط {l.Mean:0.##}/5 · الوسيط {l.Median:0.#} · العالية {l.PctHigh:0.#}% · العدد {l.Total}", 14, false, White));
                for (int s = 1; s <= 5; s++)
                {
                    int cnt = l.Distribution[s - 1];
                    double pct = l.Total > 0 ? 100.0 * cnt / l.Total : 0;
                    lines.Add(L($"الدرجة {s}: {cnt} ({pct:0.#}%)", 13, false, White));
                }
                break;
            case QuestionType.MultipleChoice:
                var ch = c.Choices;
                if (ch == null || ch.Count == 0) { lines.Add(L("لا توجد إجابات بعد.", 14, false, White)); break; }
                foreach (var x in ch.Take(8))
                    lines.Add(L($"{x.ChoiceText}: {x.Count} ({x.Percent:0.#}%)", 13, false, White));
                break;
            case QuestionType.OpenText:
                var o = c.OpenText;
                if (o == null || o.TotalResponses == 0) { lines.Add(L("لا توجد إجابات نصية بعد.", 14, false, White)); break; }
                lines.Add(L($"إجمالي الإجابات: {o.TotalResponses} · غير مصنّف: {o.UncategorizedCount}", 14, false, White));
                foreach (var cat in o.Categories.Take(6))
                    lines.Add(L($"{cat.Category}: {cat.Count} ({cat.Percent:0.#}%)", 13, false, White));
                break;
        }
        if (m.Interpretations.TryGetValue(c.QuestionId, out var interp) && !string.IsNullOrEmpty(interp))
            lines.Add(L($"التفسير: {interp}", 12, false, PaleBlue));
        deck.ContentSlide($"السؤال {c.Order}", lines);
    }

    private static void ConclusionsSlide(PptxDeck deck, FinalReportViewModel m)
    {
        var lines = new List<Line>();
        if (m.Insights.Count == 0)
            lines.Add(L("لا توجد رؤى كافية بعد.", 14, false, White));
        foreach (var ins in m.Insights)
            lines.Add(L($"• {ins}", 13, false, White));
        lines.Add(L("شكراً لجميع المشاركين في الاستبيان الرسمي.", 14, true, Gold));
        deck.ContentSlide("الخلاصة والرؤى", lines);
    }
}
