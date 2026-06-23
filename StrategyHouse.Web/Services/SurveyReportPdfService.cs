using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using StrategyHouse.Domain.Entities;

namespace StrategyHouse.Web.Services;

// Phase 4 — printable survey report: response totals, Likert averages,
// MCQ distributions, per-department breakdown, and a sample of verbatim text.
public class SurveyReportPdfService
{
    // Phase 20.25 — unify exports on the official GAC brand typeface.
    private const string Font = BrandFonts.Regular;
    private const string Primary = "#194F90";
    private const string Cyan = "#46BCCD";
    private const string Green = "#009845";
    private const string Gold = "#D79A2B";

    public SurveyReportPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public class QuestionStat
    {
        public string QuestionAr { get; set; } = "";
        public string Type { get; set; } = "";
        public int Answered { get; set; }
        public double? Average { get; set; }              // Likert5
        public List<(string Label, int Count)> Distribution { get; set; } = new(); // MCQ / YesNo / Likert
        public List<string> Verbatim { get; set; } = new(); // Text (top sample)
    }

    public class ReportModel
    {
        public string SurveyTitle { get; set; } = "";
        public int TotalResponses { get; set; }
        public List<QuestionStat> Questions { get; set; } = new();
        public List<(string Dept, int Count)> ByDept { get; set; } = new();
    }

    public byte[] Generate(ReportModel m)
    {
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A3);
                page.Margin(18, Unit.Millimetre);
                page.ContentFromRightToLeft();
                page.DefaultTextStyle(x => x.FontFamily(Font).FontSize(12));

                page.Header().Column(col =>
                {
                    col.Item().Text("تقرير الاستبيان").FontSize(26).Bold().FontColor(Primary);
                    col.Item().Text(m.SurveyTitle).FontSize(16).FontColor(Gold);
                    col.Item().PaddingTop(4).Text($"إجمالي الردود: {m.TotalResponses}").FontSize(14).FontColor(Green);
                    col.Item().PaddingTop(6).LineHorizontal(2).LineColor(Cyan);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(12);

                    if (m.ByDept.Count > 0)
                    {
                        col.Item().Text("التوزيع حسب الإدارة").FontSize(16).Bold().FontColor(Primary);
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(1); });
                            t.Header(h =>
                            {
                                h.Cell().Background(Primary).Padding(5).Text("الإدارة").FontColor(Colors.White);
                                h.Cell().Background(Primary).Padding(5).Text("عدد الردود").FontColor(Colors.White);
                            });
                            foreach (var (dept, cnt) in m.ByDept)
                            {
                                t.Cell().BorderBottom(1).BorderColor("#EEE").Padding(4).Text(dept);
                                t.Cell().BorderBottom(1).BorderColor("#EEE").Padding(4).Text(cnt.ToString());
                            }
                        });
                    }

                    foreach (var q in m.Questions)
                    {
                        col.Item().PaddingTop(6).Column(c =>
                        {
                            c.Item().Text(q.QuestionAr).FontSize(14).Bold().FontColor(Primary);
                            c.Item().Text($"النوع: {TypeAr(q.Type)} · عدد الإجابات: {q.Answered}").FontSize(10).FontColor(Colors.Grey.Darken1);

                            if (q.Average != null)
                                c.Item().PaddingTop(2).Text($"المتوسط: {q.Average:0.00} / 5").FontSize(13).Bold().FontColor(Green);

                            if (q.Distribution.Count > 0)
                            {
                                int maxCount = Math.Max(1, q.Distribution.Max(d => d.Count));
                                c.Item().PaddingTop(4).Column(dc =>
                                {
                                    foreach (var (label, cnt) in q.Distribution)
                                    {
                                        dc.Item().Row(row =>
                                        {
                                            row.ConstantItem(160).Text(label).FontSize(11);
                                            row.RelativeItem().Height(14).Background("#EEF2F7").AlignLeft()
                                                .Row(br =>
                                                {
                                                    var frac = (float)cnt / maxCount;
                                                    br.RelativeItem(Math.Max(0.001f, frac)).Background(Cyan);
                                                    br.RelativeItem(Math.Max(0.001f, 1 - frac));
                                                });
                                            row.ConstantItem(50).AlignLeft().Text(cnt.ToString()).FontSize(11).Bold();
                                        });
                                    }
                                });
                            }

                            if (q.Verbatim.Count > 0)
                            {
                                c.Item().PaddingTop(4).Text("عينة من الإجابات النصية:").FontSize(11).Bold().FontColor(Gold);
                                foreach (var v in q.Verbatim)
                                    c.Item().PaddingRight(8).Text("• " + v).FontSize(11).FontColor(Colors.Grey.Darken2);
                            }
                        });
                        col.Item().LineHorizontal(1).LineColor("#EEE");
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("الهيئة العامة للمنافسة — تقرير الاستبيان · ").FontColor(Colors.Grey.Darken1).FontSize(10);
                    t.Span(DateTime.UtcNow.ToString("yyyy-MM-dd")).FontColor(Primary).FontSize(10);
                });
            });
        });

        return doc.GeneratePdf();
    }

    private static string TypeAr(string type) => type switch
    {
        "Likert5" => "مقياس (1-5)",
        "MCQ" => "اختيار من متعدد",
        "YesNo" => "نعم / لا",
        "Text" => "نص حر",
        _ => type,
    };
}
