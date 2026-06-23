using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using StrategyHouse.Domain.Enums;
using StrategyHouse.Web.Models;

namespace StrategyHouse.Web.Services;

// Phase 12 — branded final report PDF for the official survey. Navy header with the GAC
// colour logo top-right (visual-left in RTL), gold accent rule, Cairo font. Renders an
// executive summary, per-question sections with metric values + bar distributions, and a
// cross-question insights section.
public class SurveyFinalReportPdfService
{
    private const string Font = "Cairo";
    private const string Navy = "#0E2A47";
    private const string Gold = "#FAC126";
    private const string Cyan = "#46BCCD";
    private const string Green = "#009845";

    private readonly byte[]? _logoColor;

    public SurveyFinalReportPdfService(IWebHostEnvironment env)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        // Phase 20.24.2 — use the official white logo over the navy PDF header.
        var logoPath = Path.Combine(env.WebRootPath ?? "wwwroot", "images", "gac-logo-white.png");
        if (File.Exists(logoPath))
        {
            try { _logoColor = File.ReadAllBytes(logoPath); } catch { _logoColor = null; }
        }
    }

    public byte[] Generate(FinalReportViewModel m)
    {
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(16, Unit.Millimetre);
                page.ContentFromRightToLeft();
                page.DefaultTextStyle(x => x.FontFamily(Font).FontSize(11).FontColor(Colors.Grey.Darken3));

                page.Header().Column(header =>
                {
                    header.Item().Background(Navy).Padding(12).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("التقرير النهائي للاستبيان").FontSize(20).Bold().FontColor(Colors.White);
                            c.Item().Text(m.SurveyTitle).FontSize(12).FontColor(Gold);
                            c.Item().Text("الهيئة العامة للمنافسة").FontSize(10).FontColor(Cyan);
                        });
                        if (_logoColor != null)
                            row.ConstantItem(70).AlignLeft().AlignMiddle().Height(40).Image(_logoColor).FitHeight();
                        else
                            row.ConstantItem(70).AlignLeft().AlignMiddle().Text("GAC").FontSize(22).Bold().FontColor(Gold);
                    });
                    header.Item().Height(3).Background(Gold);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(10);

                    // Executive summary
                    col.Item().Text("الملخص التنفيذي").FontSize(15).Bold().FontColor(Navy);
                    col.Item().Row(r =>
                    {
                        r.Spacing(8);
                        Kpi(r, "إجمالي الردود", m.TotalResponses.ToString());
                        Kpi(r, "عدد الأسئلة", m.Cards.Count.ToString());
                        var range = m.DateFrom != null && m.DateTo != null
                            ? $"{m.DateFrom:yyyy-MM-dd} ← {m.DateTo:yyyy-MM-dd}" : "—";
                        Kpi(r, "الفترة", range);
                    });

                    if (m.Takeaways.Count > 0)
                    {
                        col.Item().PaddingTop(2).Text("أبرز النتائج").FontSize(12).Bold().FontColor(Gold);
                        foreach (var t in m.Takeaways)
                            col.Item().Text("• " + t).FontSize(10);
                    }

                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor("#E5E7EB");

                    // Per-question sections
                    foreach (var c in m.Cards.OrderBy(x => x.Order))
                    {
                        col.Item().PaddingTop(4).Column(qc =>
                        {
                            qc.Item().Text($"السؤال {c.Order}: {c.QuestionAr}").FontSize(12).Bold().FontColor(Navy);
                            qc.Item().Text($"النوع: {TypeAr(c.Type)} · المقياس: {c.Metric}").FontSize(9).FontColor(Colors.Grey.Darken1);

                            switch (c.Type)
                            {
                                case QuestionType.Likert5 when c.Likert != null:
                                    qc.Item().PaddingTop(2).Text($"المتوسط: {c.Likert.Mean:0.##} / 5 · الوسيط: {c.Likert.Median:0.#} · نسبة العالية (4-5): {c.Likert.PctHigh:0.#}% · العدد: {c.Likert.Total}")
                                        .FontSize(10).Bold().FontColor(Green);
                                    Bars(qc, Enumerable.Range(1, 5).Select(i => ($"درجة {i}", c.Likert.Distribution[i - 1])).ToList());
                                    break;
                                case QuestionType.MultipleChoice when c.Choices != null:
                                    Bars(qc, c.Choices.Select(ch => ($"{ch.ChoiceText} ({ch.Percent:0.#}%)", ch.Count)).ToList());
                                    break;
                                case QuestionType.OpenText when c.OpenText != null:
                                    qc.Item().PaddingTop(2).Text($"إجمالي الإجابات: {c.OpenText.TotalResponses} · غير مصنّف: {c.OpenText.UncategorizedCount}")
                                        .FontSize(10).Bold().FontColor(Green);
                                    if (c.OpenText.Categories.Count > 0)
                                        Bars(qc, c.OpenText.Categories.Select(k => ($"{k.Category} ({k.Percent:0.#}%)", k.Count)).ToList());
                                    break;
                            }

                            if (m.Interpretations.TryGetValue(c.QuestionId, out var interp) && !string.IsNullOrEmpty(interp))
                                qc.Item().PaddingTop(2).Text("التفسير: " + interp).FontSize(9).Italic().FontColor(Cyan);
                        });
                        col.Item().LineHorizontal(1).LineColor("#EEE");
                    }

                    // Overall insights
                    if (m.Insights.Count > 0)
                    {
                        col.Item().PaddingTop(4).Text("رؤى شاملة").FontSize(15).Bold().FontColor(Navy);
                        foreach (var ins in m.Insights)
                            col.Item().Text("◆ " + ins).FontSize(10);
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("الهيئة العامة للمنافسة — التقرير النهائي للاستبيان · ").FontColor(Colors.Grey.Darken1).FontSize(9);
                    t.Span(DateTime.UtcNow.ToString("yyyy-MM-dd")).FontColor(Navy).FontSize(9);
                });
            });
        });

        return doc.GeneratePdf();
    }

    private static void Kpi(RowDescriptor r, string label, string value)
    {
        r.RelativeItem().Border(1).BorderColor("#E5E7EB").Background("#F7FAFC").Padding(8).Column(c =>
        {
            c.Item().Text(value).FontSize(14).Bold().FontColor(Navy);
            c.Item().Text(label).FontSize(9).FontColor(Colors.Grey.Darken1);
        });
    }

    private void Bars(ColumnDescriptor col, List<(string Label, int Count)> data)
    {
        if (data.Count == 0) { col.Item().Text("لا توجد بيانات.").FontSize(9).FontColor(Colors.Grey.Medium); return; }
        int max = Math.Max(1, data.Max(d => d.Count));
        col.Item().PaddingTop(3).Column(dc =>
        {
            foreach (var (label, cnt) in data)
            {
                dc.Item().PaddingBottom(2).Row(row =>
                {
                    row.ConstantItem(180).Text(label).FontSize(9);
                    row.RelativeItem().Height(12).Background("#EEF2F7").AlignRight().Row(br =>
                    {
                        var frac = (float)cnt / max;
                        br.RelativeItem(Math.Max(0.001f, frac)).Background(Cyan);
                        br.RelativeItem(Math.Max(0.001f, 1 - frac));
                    });
                    row.ConstantItem(36).AlignCenter().Text(cnt.ToString()).FontSize(9).Bold();
                });
            }
        });
    }

    private static string TypeAr(QuestionType t) => t switch
    {
        QuestionType.Likert5 => "مقياس ليكرت (1-5)",
        QuestionType.MultipleChoice => "اختيار من متعدد",
        QuestionType.OpenText => "سؤال مفتوح",
        _ => t.ToString(),
    };
}
