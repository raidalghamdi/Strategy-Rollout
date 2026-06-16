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
                    var s = m.Sections;

                    // ----- Executive summary -----
                    if (s.Has(ExecReportSections.Overview))
                    {
                        col.Item().Text("الملخص التنفيذي").FontSize(15).Bold().FontColor(Navy);
                        col.Item().Row(r =>
                        {
                            r.Spacing(8);
                            Kpi(r, "إجمالي الجلسات", m.Overview.TotalSessions.ToString());
                            Kpi(r, "الجلسات المكتملة", m.Overview.TotalCompletedSessions.ToString());
                            Kpi(r, "الإدارات المشاركة", $"{m.Overview.TotalDepartmentsEngaged}/{m.Overview.TotalDepartments}");
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
                            Kpi(r, "نسبة الإكمال", $"{m.Overview.CompletionPercentage:0.#}%");
                            Kpi(r, "تواقيع الفرق", m.GroupSignatures.TotalCount.ToString());
                        });
                        if (m.Overview.NotEngagedDepartments.Count > 0)
                            col.Item().Text($"لم تشارك بعد: {string.Join("، ", m.Overview.NotEngagedDepartments)}").FontSize(9).FontColor(Colors.Grey.Darken1);
                        col.Item().PaddingTop(4).LineHorizontal(1).LineColor("#E5E7EB");
                    }

                    // ----- Department breakdown -----
                    if (s.Has(ExecReportSections.Departments))
                    {
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
                                    cols.RelativeColumn(0.8f);
                                    cols.RelativeColumn(3);
                                    cols.RelativeColumn(1.2f);
                                    cols.RelativeColumn(1.2f);
                                    cols.RelativeColumn(1.4f);
                                });
                                table.Header(h =>
                                {
                                    h.Cell().Background(Navy).Padding(5).Text("الترتيب").FontSize(9).Bold().FontColor(Colors.White);
                                    h.Cell().Background(Navy).Padding(5).Text("الإدارة").FontSize(9).Bold().FontColor(Colors.White);
                                    h.Cell().Background(Navy).Padding(5).Text("الجلسات").FontSize(9).Bold().FontColor(Colors.White);
                                    h.Cell().Background(Navy).Padding(5).Text("الحضور").FontSize(9).Bold().FontColor(Colors.White);
                                    h.Cell().Background(Navy).Padding(5).Text("نسبة الإكمال").FontSize(9).Bold().FontColor(Colors.White);
                                });
                                foreach (var d in m.DepartmentBreakdown)
                                {
                                    table.Cell().BorderBottom(1).BorderColor("#EEE").Padding(5).Text(d.Rank.ToString()).FontSize(9);
                                    table.Cell().BorderBottom(1).BorderColor("#EEE").Padding(5).Text(d.DeptName).FontSize(9);
                                    table.Cell().BorderBottom(1).BorderColor("#EEE").Padding(5).Text(d.SessionsCount.ToString()).FontSize(9);
                                    table.Cell().BorderBottom(1).BorderColor("#EEE").Padding(5).Text(d.AttendeesCount.ToString()).FontSize(9);
                                    table.Cell().BorderBottom(1).BorderColor("#EEE").Padding(5).Text($"{d.CompletionRate:0.#}%").FontSize(9);
                                }
                            });
                        }
                    }

                    // ----- Quiz analytics -----
                    if (s.Has(ExecReportSections.Quiz))
                    {
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
                        if (m.QuizAnalytics.Top3Strongest.Count > 0)
                        {
                            col.Item().PaddingTop(2).Text("نقاط القوة المعرفية").FontSize(12).Bold().FontColor(Gold);
                            foreach (var q in m.QuizAnalytics.Top3Strongest)
                                col.Item().Text($"• {q.QuestionAr} — نسبة الصواب {(100 - q.MissRate):0.#}%").FontSize(9);
                        }
                    }

                    // ----- Survey metrics -----
                    if (s.Has(ExecReportSections.Survey))
                    {
                        col.Item().PaddingTop(6).Text("مؤشرات الاستبيان الرسمي").FontSize(15).Bold().FontColor(Navy);
                        if (m.SurveyMetrics.Count == 0)
                        {
                            col.Item().Text("لا توجد بيانات استبيان بعد.").FontSize(9).FontColor(Colors.Grey.Medium);
                        }
                        else
                        {
                            foreach (var sv in m.SurveyMetrics.OrderBy(x => x.Order))
                            {
                                col.Item().PaddingTop(2).Column(sc =>
                                {
                                    sc.Item().Text($"السؤال {sv.Order}: {sv.QuestionAr}").FontSize(11).Bold().FontColor(Navy);
                                    sc.Item().Text($"{sv.Type} · {sv.Headline}").FontSize(9).FontColor(Colors.Grey.Darken1);
                                });
                            }
                        }
                    }

                    // ----- Contributions -----
                    if (s.Has(ExecReportSections.Contributions))
                    {
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

                        col.Item().PaddingTop(4).Text($"القيمة التي تعبّر عن الفرق · إجمالي الاختيارات: {m.TeamValues.TotalSelections}").FontSize(11).Bold().FontColor(Gold);
                        if (m.TeamValues.ByValue.Count == 0)
                            col.Item().Text("لم تختر الفرق قيمة بعد.").FontSize(9).FontColor(Colors.Grey.Medium);
                        foreach (var v in m.TeamValues.ByValue)
                            col.Item().Text($"• {v.Name} ({v.Count})").FontSize(9);
                    }

                    // ----- Group signatures -----
                    if (s.Has(ExecReportSections.Signatures))
                    {
                        col.Item().PaddingTop(6).Text("تواقيع الفرق وتعليقاتها").FontSize(15).Bold().FontColor(Navy);
                        col.Item().Text($"إجمالي التواقيع: {m.GroupSignatures.TotalCount} · الخرائط الاستراتيجية: {m.MapsCount}").FontSize(10).Bold().FontColor(Green);
                        if (m.GroupSignatures.TopKeywords.Count > 0)
                            col.Item().Text("موضوعات شائعة: " + string.Join("، ", m.GroupSignatures.TopKeywords.Take(8).Select(k => $"{k.Name} ({k.Count})"))).FontSize(9).FontColor(Colors.Grey.Darken1);
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
                    }

                    // ----- Leadership: strategic alignment -----
                    if (s.Has(ExecReportSections.LeadershipAlignment))
                    {
                        col.Item().PaddingTop(6).Text("الاتساق الاستراتيجي").FontSize(15).Bold().FontColor(Navy);
                        col.Item().Text($"إجمالي المساهمات المرتبطة بالركائز: {m.LeadershipAlignment.TotalContributions}").FontSize(10).Bold().FontColor(Green);
                        foreach (var ps in m.LeadershipAlignment.PillarShares)
                            col.Item().Text($"• {ps.PillarName}: {ps.Count} ({ps.Percent:0.#}%)").FontSize(9);
                        foreach (var g in m.LeadershipAlignment.Gaps)
                            col.Item().Text($"⚠ {g}").FontSize(9).FontColor(Gold);
                    }

                    // ----- Leadership: culture & participation -----
                    if (s.Has(ExecReportSections.LeadershipCulture))
                    {
                        col.Item().PaddingTop(6).Text("الثقافة والمشاركة").FontSize(15).Bold().FontColor(Navy);
                        col.Item().Text($"مؤشر روح الفريق: {m.LeadershipCulture.TeamSpiritScore:0.#}/100 ({m.LeadershipCulture.TeamSpiritLabel})").FontSize(10).Bold().FontColor(Green);
                        col.Item().Text($"التعليقات — إيجابية: {m.LeadershipCulture.PositiveComments} · محايدة: {m.LeadershipCulture.NeutralComments} · سلبية: {m.LeadershipCulture.NegativeComments}").FontSize(9);
                        foreach (var d in m.LeadershipCulture.DepartmentParticipation.Take(10))
                            col.Item().Text($"• {d.DeptName}: {d.Attendees} حضور").FontSize(9);
                    }

                    // ----- Leadership: risks & opportunities -----
                    if (s.Has(ExecReportSections.LeadershipRisks))
                    {
                        col.Item().PaddingTop(6).Text("المخاطر والفرص").FontSize(15).Bold().FontColor(Navy);
                        col.Item().Text("أبرز التحديات (س4)").FontSize(12).Bold().FontColor(Gold);
                        if (m.LeadershipRisks.TopChallenges.Count == 0) col.Item().Text("—").FontSize(9).FontColor(Colors.Grey.Medium);
                        foreach (var c in m.LeadershipRisks.TopChallenges.Take(5))
                            col.Item().Text($"• {c.Category} ({c.Count})").FontSize(9);
                        col.Item().Text("أبرز الفرص (س7)").FontSize(12).Bold().FontColor(Gold);
                        if (m.LeadershipRisks.TopOpportunities.Count == 0) col.Item().Text("—").FontSize(9).FontColor(Colors.Grey.Medium);
                        foreach (var o in m.LeadershipRisks.TopOpportunities.Take(5))
                            col.Item().Text($"• {o.Category} ({o.Count})").FontSize(9);
                    }

                    // ----- Leadership: organisational maturity -----
                    if (s.Has(ExecReportSections.LeadershipMaturity))
                    {
                        col.Item().PaddingTop(6).Text("النضج التنظيمي").FontSize(15).Bold().FontColor(Navy);
                        col.Item().Text($"ناضجة: {m.LeadershipMaturity.MatureCount} · متطورة: {m.LeadershipMaturity.DevelopingCount} · بحاجة دعم: {m.LeadershipMaturity.NeedsSupportCount}").FontSize(10).Bold().FontColor(Green);
                        foreach (var d in m.LeadershipMaturity.Departments.Take(12))
                            col.Item().Text($"• {d.DeptName}: {d.Score:0.##}/5 — {d.Tier}").FontSize(9);
                    }

                    // ----- Leadership: recommendations -----
                    if (s.Has(ExecReportSections.LeadershipRecommendations))
                    {
                        col.Item().PaddingTop(6).Text("توصيات القيادة").FontSize(15).Bold().FontColor(Navy);
                        if (m.LeadershipRecommendations.Count == 0)
                            col.Item().Text("لا توجد توصيات كافية بعد.").FontSize(9).FontColor(Colors.Grey.Medium);
                        foreach (var rec in m.LeadershipRecommendations)
                            col.Item().Text($"• {rec}").FontSize(9);
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
