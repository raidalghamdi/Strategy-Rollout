using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using StrategyHouse.Domain.Enums;
using StrategyHouse.Web.Models;
using static StrategyHouse.Web.Services.PptxSlideHelper;
using P = DocumentFormat.OpenXml.Presentation;

namespace StrategyHouse.Web.Services;

// Phase 13.1 — branded .pptx export of the official survey final report. Title slide,
// overview, one slide per question (Q1..Q8) with the question text + metric/breakdown,
// and a conclusions slide. GAC navy/gold, white text on navy, RTL, no embedded charts.
public class SurveyReportPowerPointBuilder
{
    private PptxSlideHelper _h = null!;

    public byte[] Build(FinalReportViewModel m)
    {
        _h = new PptxSlideHelper();
        using var ms = new MemoryStream();
        using (var doc = PresentationDocument.Create(ms, DocumentFormat.OpenXml.PresentationDocumentType.Presentation))
        {
            var (pres, layout) = Init(doc);

            TitleSlide(pres, layout, m);
            OverviewSlide(pres, layout, m);
            foreach (var c in m.Cards.OrderBy(x => x.Order))
                QuestionSlide(pres, layout, c, m);
            ConclusionsSlide(pres, layout, m);

            pres.Presentation!.Save();
        }
        return ms.ToArray();
    }

    private void TitleSlide(PresentationPart pres, SlideLayoutPart layout, FinalReportViewModel m)
    {
        var period = m.DateFrom != null ? $"{m.DateFrom:yyyy-MM-dd} ← {m.DateTo:yyyy-MM-dd}" : "—";
        var shapes = new List<OpenXmlElement>
        {
            _h.Rect(0L, 3300000L, SlideWidth, 70000L, Gold),
            _h.TextBox(1000000L, 2200000L, 10200000L, 1100000L, new[]
            {
                ("التقرير النهائي للاستبيان الرسمي", 40, true, White),
            }, center: true),
            _h.TextBox(1000000L, 3500000L, 10200000L, 1000000L, new[]
            {
                (m.SurveyTitle, 20, false, Gold),
                ("الهيئة العامة للمنافسة", 18, false, "CFE2F0"),
                ("فترة الجمع: " + period, 14, false, "CFE2F0"),
            }, center: true),
        };
        AddSlide(pres, layout, Navy, shapes);
    }

    private void OverviewSlide(PresentationPart pres, SlideLayoutPart layout, FinalReportViewModel m)
    {
        var shapes = ContentHeader("النظرة العامة");
        var q1 = m.Cards.FirstOrDefault(c => c.Order == 1)?.Likert;
        var q8 = m.Cards.FirstOrDefault(c => c.Order == 8)?.Likert;
        var lines = new List<(string, int, bool, string)>
        {
            ($"إجمالي الردود: {m.TotalResponses}", 22, true, Gold),
            ($"عدد الأسئلة: {m.Cards.Count}", 16, false, White),
        };
        if (q1 != null && q1.Total > 0)
            lines.Add(($"وضوح الاستراتيجية (س1): متوسط {q1.Mean:0.##}/5 · العالية {q1.PctHigh:0.#}%", 16, false, White));
        if (q8 != null && q8.Total > 0)
            lines.Add(($"القدرة على المساهمة (س8): متوسط {q8.Mean:0.##}/5 · العالية {q8.PctHigh:0.#}%", 16, false, White));
        foreach (var t in m.Takeaways.Take(3))
            lines.Add(($"• {t}", 13, false, White));
        shapes.Add(_h.TextBox(600000L, 1700000L, SlideWidth - 1200000L, 4500000L, lines));
        AddSlide(pres, layout, Navy, shapes);
    }

    private void QuestionSlide(PresentationPart pres, SlideLayoutPart layout, QuestionCard c, FinalReportViewModel m)
    {
        var shapes = ContentHeader($"السؤال {c.Order}");
        var lines = new List<(string, int, bool, string)>
        {
            (c.QuestionAr, 18, true, Gold),
        };
        switch (c.Type)
        {
            case QuestionType.Likert5:
                var l = c.Likert;
                if (l == null || l.Total == 0) { lines.Add(("لا توجد إجابات بعد.", 16, false, White)); break; }
                lines.Add(($"المتوسط {l.Mean:0.##}/5 · الوسيط {l.Median:0.#} · العالية {l.PctHigh:0.#}% · العدد {l.Total}", 16, false, White));
                for (int s = 1; s <= 5; s++)
                {
                    int cnt = l.Distribution[s - 1];
                    double pct = l.Total > 0 ? 100.0 * cnt / l.Total : 0;
                    lines.Add(($"الدرجة {s}: {cnt} ({pct:0.#}%)", 14, false, White));
                }
                break;
            case QuestionType.MultipleChoice:
                var ch = c.Choices;
                if (ch == null || ch.Count == 0) { lines.Add(("لا توجد إجابات بعد.", 16, false, White)); break; }
                foreach (var x in ch.Take(8))
                    lines.Add(($"{x.ChoiceText}: {x.Count} ({x.Percent:0.#}%)", 14, false, White));
                break;
            case QuestionType.OpenText:
                var o = c.OpenText;
                if (o == null || o.TotalResponses == 0) { lines.Add(("لا توجد إجابات نصية بعد.", 16, false, White)); break; }
                lines.Add(($"إجمالي الإجابات: {o.TotalResponses} · غير مصنّف: {o.UncategorizedCount}", 16, false, White));
                foreach (var cat in o.Categories.Take(6))
                    lines.Add(($"{cat.Category}: {cat.Count} ({cat.Percent:0.#}%)", 14, false, White));
                break;
        }
        if (m.Interpretations.TryGetValue(c.QuestionId, out var interp) && !string.IsNullOrEmpty(interp))
            lines.Add(($"التفسير: {interp}", 13, false, "CFE2F0"));
        shapes.Add(_h.TextBox(600000L, 1700000L, SlideWidth - 1200000L, 4500000L, lines));
        AddSlide(pres, layout, Navy, shapes);
    }

    private void ConclusionsSlide(PresentationPart pres, SlideLayoutPart layout, FinalReportViewModel m)
    {
        var shapes = ContentHeader("الخلاصة والرؤى");
        var lines = new List<(string, int, bool, string)>();
        if (m.Insights.Count == 0)
            lines.Add(("لا توجد رؤى كافية بعد.", 16, false, White));
        foreach (var ins in m.Insights)
            lines.Add(($"• {ins}", 14, false, White));
        lines.Add(("شكراً لجميع المشاركين في الاستبيان الرسمي.", 16, true, Gold));
        shapes.Add(_h.TextBox(600000L, 1700000L, SlideWidth - 1200000L, 4500000L, lines));
        AddSlide(pres, layout, Navy, shapes);
    }

    private List<OpenXmlElement> ContentHeader(string title) => new()
    {
        _h.Rect(0L, 0L, SlideWidth, 1300000L, LightNavy),
        _h.Rect(0L, 1300000L, SlideWidth, 50000L, Gold),
        _h.TextBox(600000L, 350000L, SlideWidth - 1200000L, 800000L, new[] { (title, 28, true, White) }),
    };
}
