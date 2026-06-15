using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using StrategyHouse.Web.Models;

namespace StrategyHouse.Web.Services;

// Phase 13 — branded executive report PDF aggregating the entire rollout event.
// Navy header with the GAC colour logo, gold accent rule, Cairo font, page numbers.
public class ExecutiveReportPdfDocument
{
    private const string Font = "Cairo";
    private const string Navy = "#0E2A47";
    private const string Gold = "#FAC126";
    private const string Cyan = "#46BCCD";
    private const string Green = "#009845";

    private readonly byte[]? _logoColor;

    public ExecutiveReportPdfDocument(IWebHostEnvironment env)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var logoPath = Path.Combine(env.WebRootPath ?? "wwwroot", "images", "gac-logo-color.png");
        if (File.Exists(logoPath))
        {
            try { _logoColor = File.ReadAllBytes(logoPath); } catch { _logoColor = null; }
        }
    }

    public byte[] Generate(ExecutiveReportViewModel m)
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
                            c.Item().Text("التقرير التنفيذي").FontSize(20).Bold().FontColor(Colors.White);
                            c.Item().Text("بناء البيت الاستراتيجي — رحلة الإدارات").FontSize(12).FontColor(Gold);
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

                    // ----- Executive summary -----
                    col.Item().Text("الملخص التنفيذي").FontSize(15).Bold().FontColor(Navy);
                    col.Item().Row(r =>
                    {
                        r.Spacing(8);
                        Kpi(r, "إجمالي الجلسات", m.Overview.TotalSessions.ToString());
                        Kpi(r, "الجلسات المكتملة", m.Overview.TotalCompletedSessions.ToString());
                        Kpi(r, "الإدارات المشاركة", m.Overview.TotalDepartmentsEngaged.ToString());
                    });
                    col.Item().Row(r =>
                    {
                        r.Spacing(8);
                        Kpi(r, "إجمالي الحضور", m.Overview.TotalAttendees.ToString());
                        Kpi(r, "متوسط الاختبار", $"{m.Overview.AvgQuizScore:0.##} / 5");
                        Kpi(r, "وضوح الاستراتيجية", m.Overview.AvgSurveyClarity > 0 ? $"{m.Overview.AvgSurveyClarity:0.##} / 5" : "—");
                    });
                    col.Item().Row(r =>
                    {
                        r.Spacing(8);
                        Kpi(r, "القدرة على المساهمة", m.Overview.AvgContributionCapability > 0 ? $"{m.Overview.AvgContributionCapability:0.##} / 5" : "—");
                        Kpi(r, "الخرائط الاستراتيجية", m.MapsCount.ToString());
                        Kpi(r, "تواقيع الفرق", m.GroupSignatures.TotalCount.ToString());
                    });

                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor("#E5E7EB");

                    // ----- Department breakdown -----
                    col.Item().PaddingTop(4).Text("توزيع الإدارات").FontSize(15).Bold().FontColor(Navy);
                    if (m.DepartmentBreakdown.Count == 0)
                    {
                        col.Item().Text("لا توجد بيانات إدارات بعد.").FontSize(9).FontColor(Colors.Grey.Medium);
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(3);
                                cols.RelativeColumn(1.2f);
                                cols.RelativeColumn(1.2f);
                                cols.RelativeColumn(1.4f);
                            });
                            table.Header(h =>
                            {
                                h.Cell().Background(Navy).Padding(5).Text("الإدارة").FontSize(9).Bold().FontColor(Colors.White);
                                h.Cell().Background(Navy).Padding(5).Text("الجلسات").FontSize(9).Bold().FontColor(Colors.White);
                                h.Cell().Background(Navy).Padding(5).Text("الحضور").FontSize(9).Bold().FontColor(Colors.White);
                                h.Cell().Background(Navy).Padding(5).Text("نسبة الإكمال").FontSize(9).Bold().FontColor(Colors.White);
                            });
                            foreach (var d in m.DepartmentBreakdown)
                            {
                                table.Cell().BorderBottom(1).BorderColor("#EEE").Padding(5).Text(d.DeptName).FontSize(9);
                                table.Cell().BorderBottom(1).BorderColor("#EEE").Padding(5).Text(d.SessionsCount.ToString()).FontSize(9);
                                table.Cell().BorderBottom(1).BorderColor("#EEE").Padding(5).Text(d.AttendeesCount.ToString()).FontSize(9);
                                table.Cell().BorderBottom(1).BorderColor("#EEE").Padding(5).Text($"{d.CompletionRate:0.#}%").FontSize(9);
                            }
                        });
                    }

                    // ----- Quiz analytics -----
                    col.Item().PaddingTop(6).Text("تحليلات الاختبار").FontSize(15).Bold().FontColor(Navy);
                    col.Item().Text($"إجمالي المحاولات: {m.QuizAnalytics.TotalAttempts} · المتوسط: {m.QuizAnalytics.AvgScore:0.##} / 5")
                        .FontSize(10).Bold().FontColor(Green);
                    Bars(col, new List<(string, int)>
                    {
                        ("منخفض (0-2)", m.QuizAnalytics.Bucket0to2),
                        ("متوسط (3-4)", m.QuizAnalytics.Bucket3to4),
                        ("ممتاز (5)", m.QuizAnalytics.Bucket5),
                    });
                    if (m.QuizAnalytics.Top3MostMissed.Count > 0)
                    {
                        col.Item().PaddingTop(2).Text("أكثر الأسئلة صعوبة").FontSize(12).Bold().FontColor(Gold);
                        foreach (var q in m.QuizAnalytics.Top3MostMissed)
                            col.Item().Text($"• {q.QuestionAr} — نسبة الخطأ {q.MissRate:0.#}% ({q.Attempts} محاولة)").FontSize(9);
                    }

                    // ----- Survey metrics -----
                    col.Item().PaddingTop(6).Text("مؤشرات الاستبيان الرسمي").FontSize(15).Bold().FontColor(Navy);
                    if (m.SurveyMetrics.Count == 0)
                    {
                        col.Item().Text("لا توجد بيانات استبيان بعد.").FontSize(9).FontColor(Colors.Grey.Medium);
                    }
                    else
                    {
                        foreach (var s in m.SurveyMetrics.OrderBy(x => x.Order))
                        {
                            col.Item().PaddingTop(2).Column(sc =>
                            {
                                sc.Item().Text($"السؤال {s.Order}: {s.QuestionAr}").FontSize(11).Bold().FontColor(Navy);
                                sc.Item().Text($"{s.Type} · {s.Headline}").FontSize(9).FontColor(Colors.Grey.Darken1);
                            });
                        }
                    }

                    // ----- Contributions -----
                    col.Item().PaddingTop(6).Text("المساهمات").FontSize(15).Bold().FontColor(Navy);
                    col.Item().Text($"إجمالي التعهدات: {m.Contributions.TotalPledges}").FontSize(10).Bold().FontColor(Green);
                    col.Item().Row(r =>
                    {
                        r.Spacing(12);
                        r.RelativeItem().Column(oc =>
                        {
                            oc.Item().Text("أبرز الأهداف").FontSize(12).Bold().FontColor(Gold);
                            if (m.Contributions.TopObjectives.Count == 0)
                                oc.Item().Text("—").FontSize(9).FontColor(Colors.Grey.Medium);
                            foreach (var o in m.Contributions.TopObjectives)
                                oc.Item().Text($"• {o.Name} ({o.Count})").FontSize(9);
                        });
                        r.RelativeItem().Column(ic =>
                        {
                            ic.Item().Text("أبرز المبادرات").FontSize(12).Bold().FontColor(Gold);
                            if (m.Contributions.TopInitiatives.Count == 0)
                                ic.Item().Text("—").FontSize(9).FontColor(Colors.Grey.Medium);
                            foreach (var i in m.Contributions.TopInitiatives)
                                ic.Item().Text($"• {i.Name} ({i.Count})").FontSize(9);
                        });
                    });

                    // ----- Group signatures -----
                    col.Item().PaddingTop(6).Text("تواقيع الفرق وتعليقاتها").FontSize(15).Bold().FontColor(Navy);
                    col.Item().Text($"إجمالي التواقيع: {m.GroupSignatures.TotalCount}").FontSize(10).Bold().FontColor(Green);
                    if (m.GroupSignatures.RecentComments.Count == 0)
                    {
                        col.Item().Text("لا توجد تعليقات بعد.").FontSize(9).FontColor(Colors.Grey.Medium);
                    }
                    else
                    {
                        foreach (var c in m.GroupSignatures.RecentComments)
                        {
                            col.Item().PaddingTop(2).Border(1).BorderColor("#E5E7EB").Background("#F7FAFC").Padding(6).Column(cc =>
                            {
                                cc.Item().Text(c.DeptName).FontSize(10).Bold().FontColor(Navy);
                                cc.Item().Text(c.Text).FontSize(9);
                                cc.Item().Text(c.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")).FontSize(8).FontColor(Colors.Grey.Darken1);
                            });
                        }
                    }
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text(t =>
                    {
                        t.Span("الهيئة العامة للمنافسة — التقرير التنفيذي · ").FontColor(Colors.Grey.Darken1).FontSize(9);
                        t.Span(m.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")).FontColor(Navy).FontSize(9);
                    });
                    row.ConstantItem(120).AlignLeft().Text(t =>
                    {
                        t.Span("صفحة ").FontSize(9).FontColor(Colors.Grey.Darken1);
                        t.CurrentPageNumber().FontSize(9).FontColor(Navy);
                        t.Span(" من ").FontSize(9).FontColor(Colors.Grey.Darken1);
                        t.TotalPages().FontSize(9).FontColor(Navy);
                    });
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
                    row.ConstantItem(140).Text(label).FontSize(9);
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
}
