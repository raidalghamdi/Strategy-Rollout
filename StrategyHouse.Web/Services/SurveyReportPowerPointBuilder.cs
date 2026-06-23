using Microsoft.AspNetCore.Hosting;
using StrategyHouse.Domain.Enums;
using StrategyHouse.Web.Models;
using static StrategyHouse.Web.Services.PptxDeck;

namespace StrategyHouse.Web.Services;

// Phase 20.24 — GAC-branded .pptx export of the official survey final report.
//   • Cover slide with navy→green gradient and white GAC logo
//   • Overview KPI slide (mirrors the PDF and the Excel summary)
//   • One slide per question — Likert/MC questions get a native bar chart;
//     OpenText categories also render as bar charts
//   • Conclusions / insights slide
public class SurveyReportPowerPointBuilder
{
    private readonly IWebHostEnvironment _env;
    public SurveyReportPowerPointBuilder(IWebHostEnvironment env) { _env = env; }

    public byte[] Build(FinalReportViewModel m)
    {
        var deck = new PptxDeck(WhiteLogo(), ColorLogo());
        var period = m.DateFrom != null ? $"{m.DateFrom:yyyy-MM-dd} ← {m.DateTo:yyyy-MM-dd}" : "—";

        deck.TitleSlide("التقرير النهائي للاستبيان الرسمي", new[]
        {
            L(m.SurveyTitle, 18, false, TextDk),
            L("الهيئة العامة للمنافسة", 16, false, TextMd),
            L("فترة الجمع: " + period, 13, false, TextMd),
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

        var cards = new List<(string, string)>
        {
            (m.TotalResponses.ToString(), "إجمالي الردود"),
            (m.Cards.Count.ToString(), "عدد الأسئلة"),
            (q1 is { Total: > 0 } ? $"{q1.Mean:0.##}/5" : "—", "وضوح الاستراتيجية (س1)"),
            (q8 is { Total: > 0 } ? $"{q8.Mean:0.##}/5" : "—", "القدرة على المساهمة (س8)"),
            (q1 is { Total: > 0 } ? $"{q1.PctHigh:0.#}%" : "—", "نسبة عالية لس1"),
            (q8 is { Total: > 0 } ? $"{q8.PctHigh:0.#}%" : "—", "نسبة عالية لس8"),
        };
        deck.KpiSlide("النظرة العامة على الاستبيان", cards);

        if (m.Takeaways.Count > 0)
        {
            // Phase 20.24.1 — title appears in the header band already; no duplicate.
            var lines = new List<Line>();
            foreach (var t in m.Takeaways.Take(6))
                lines.Add(L("• " + t, 16, false, TextDk));
            deck.ContentSlide("أبرز النتائج", lines);
        }
    }

    private static void QuestionSlide(PptxDeck deck, QuestionCard c, FinalReportViewModel m)
    {
        var title = $"السؤال {c.Order}";
        var subtitleLines = new List<Line>
        {
            L(c.QuestionAr, 16, true, Navy),
        };
        if (m.Interpretations.TryGetValue(c.QuestionId, out var interp) && !string.IsNullOrEmpty(interp))
            subtitleLines.Add(L("التفسير: " + interp, 12, false, TextMd));

        switch (c.Type)
        {
            case QuestionType.Likert5:
                var l = c.Likert;
                if (l == null || l.Total == 0)
                {
                    subtitleLines.Add(L("لا توجد إجابات بعد.", 14, false, TextMd));
                    deck.ContentSlide(title, subtitleLines);
                }
                else
                {
                    var data = new Dictionary<string, double>();
                    string[] labels = { "1 منخفض", "2", "3", "4", "5 عالي" };
                    for (int s = 0; s < 5; s++)
                        data[labels[s]] = l.Distribution[s];
                    subtitleLines.Add(L($"المتوسط {l.Mean:0.##}/5 · الوسيط {l.Median:0.#} · العالية {l.PctHigh:0.#}% · العدد {l.Total}", 14, false, TextMd));
                    deck.BarChartSlide(title, c.QuestionAr, data, subtitleLines);
                }
                break;

            case QuestionType.MultipleChoice:
                var ch = c.Choices;
                if (ch == null || ch.Count == 0)
                {
                    subtitleLines.Add(L("لا توجد إجابات بعد.", 14, false, TextMd));
                    deck.ContentSlide(title, subtitleLines);
                }
                else
                {
                    var data = new Dictionary<string, double>();
                    foreach (var x in ch.Take(8))
                        data[Trim(x.ChoiceText, 40)] = x.Count;
                    subtitleLines.Add(L($"إجمالي الإجابات: {ch.Sum(x => x.Count)}", 14, false, TextMd));
                    deck.BarChartSlide(title, c.QuestionAr, data, subtitleLines);
                }
                break;

            case QuestionType.OpenText:
                var o = c.OpenText;
                if (o == null || o.TotalResponses == 0)
                {
                    subtitleLines.Add(L("لا توجد إجابات نصية بعد.", 14, false, TextMd));
                    deck.ContentSlide(title, subtitleLines);
                }
                else
                {
                    subtitleLines.Add(L($"إجمالي الإجابات: {o.TotalResponses} · غير مصنّف: {o.UncategorizedCount}", 14, false, TextMd));
                    if (o.Categories.Count == 0)
                    {
                        subtitleLines.Add(L("لم تُصنّف الإجابات بعد.", 13, false, TextMd));
                        deck.ContentSlide(title, subtitleLines);
                    }
                    else
                    {
                        var data = new Dictionary<string, double>();
                        foreach (var cat in o.Categories.Take(8))
                            data[Trim(cat.Category, 40)] = cat.Count;
                        deck.BarChartSlide(title, c.QuestionAr, data, subtitleLines);
                    }
                }
                break;
        }
    }

    private static void ConclusionsSlide(PptxDeck deck, FinalReportViewModel m)
    {
        var lines = new List<Line>();
        if (m.Insights.Count == 0)
            lines.Add(L("لا توجد رؤى كافية بعد.", 14, false, TextMd));
        foreach (var ins in m.Insights)
            lines.Add(L("• " + ins, 14, false, TextDk));
        lines.Add(L("شكراً لجميع المشاركين في الاستبيان الرسمي.", 14, true, Green));
        deck.ContentSlide("الخلاصة والرؤى", lines);
    }

    private static string Trim(string s, int max)
        => string.IsNullOrEmpty(s) ? "—" : (s.Length <= max ? s : s.Substring(0, max - 1) + "…");

    private string? WhiteLogo()
        => Path.Combine(_env.WebRootPath ?? "", "images", "gac-logo-white.png");
    private string? ColorLogo()
        => Path.Combine(_env.WebRootPath ?? "", "images", "gac-logo-color.png");
}
