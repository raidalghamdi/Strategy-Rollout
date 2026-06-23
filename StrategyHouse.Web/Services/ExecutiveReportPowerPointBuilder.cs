using Microsoft.AspNetCore.Hosting;
using StrategyHouse.Web.Models;
using static StrategyHouse.Web.Services.PptxDeck;

namespace StrategyHouse.Web.Services;

// Phase 20.24 — GAC-branded .pptx export of the comprehensive executive report.
// Each section becomes a brand slide; numeric distributions render as native
// PowerPoint bar/pie charts; KPI overview mirrors the PDF.
public class ExecutiveReportPowerPointBuilder
{
    private readonly IWebHostEnvironment _env;
    public ExecutiveReportPowerPointBuilder(IWebHostEnvironment env) { _env = env; }

    public byte[] Build(ExecutiveReportViewModel m)
    {
        var deck = new PptxDeck(WhiteLogo(), ColorLogo());
        var s = m.Sections;

        deck.TitleSlide("التقرير التنفيذي الشامل", new[]
        {
            L("بناء البيت الاستراتيجي — رحلة الإدارات", 20, false, TextDk),
            L("الهيئة العامة للمنافسة", 16, false, TextMd),
            L("تاريخ الإصدار: " + m.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd"), 13, false, TextMd),
        });

        if (s.Has(ExecReportSections.Overview)) OverviewSlide(deck, m);
        if (s.Has(ExecReportSections.Departments)) DepartmentsSlide(deck, m);
        if (s.Has(ExecReportSections.Quiz)) QuizSlides(deck, m);
        if (s.Has(ExecReportSections.Survey)) SurveySlide(deck, m);
        if (s.Has(ExecReportSections.Contributions)) ContributionsSlides(deck, m);
        if (s.Has(ExecReportSections.Signatures)) SignaturesSlide(deck, m);
        if (s.Has(ExecReportSections.LeadershipAlignment)) AlignmentSlide(deck, m);
        if (s.Has(ExecReportSections.LeadershipCulture)) CultureSlide(deck, m);
        if (s.Has(ExecReportSections.LeadershipRisks)) RisksSlides(deck, m);
        if (s.Has(ExecReportSections.LeadershipMaturity)) MaturitySlide(deck, m);
        if (s.Has(ExecReportSections.LeadershipRecommendations)) RecommendationsSlide(deck, m);

        return deck.ToBytes();
    }

    private static void OverviewSlide(PptxDeck deck, ExecutiveReportViewModel m)
    {
        deck.KpiSlide("النظرة العامة التنفيذية", new (string, string)[]
        {
            (m.Overview.TotalSessions.ToString(), "إجمالي الجلسات"),
            (m.Overview.TotalAttendees.ToString(), "إجمالي الحضور"),
            ($"{m.Overview.TotalDepartmentsEngaged}/{m.Overview.TotalDepartments}", "الإدارات المشاركة"),
            ($"{m.Overview.CompletionPercentage:0.#}%", "نسبة الإكمال"),
            ($"{m.Overview.AvgQuizScore:0.##}/5", "متوسط الاختبار"),
            (m.Overview.AvgSurveyClarity > 0 ? $"{m.Overview.AvgSurveyClarity:0.##}/5" : "—", "وضوح الاستراتيجية"),
        });
    }

    private static void DepartmentsSlide(PptxDeck deck, ExecutiveReportViewModel m)
    {
        if (m.DepartmentBreakdown.Count == 0)
        {
            deck.ContentSlide("تفصيل الإدارات", new[] { L("لا توجد بيانات إدارات بعد.", 14, false, TextMd) });
            return;
        }
        var data = new Dictionary<string, double>();
        foreach (var d in m.DepartmentBreakdown.Take(11))
            data[Trim(d.DeptName, 28)] = d.AttendeesCount;

        var body = new List<Line>();
        if (m.Overview.NotEngagedDepartments.Count > 0)
            body.Add(L("لم تشارك بعد: " + string.Join("، ", m.Overview.NotEngagedDepartments.Take(6)), 12, false, TextMd));
        deck.BarChartSlide("الحضور حسب الإدارة", "إجمالي الحضور", data, body);
    }

    private static void QuizSlides(PptxDeck deck, ExecutiveReportViewModel m)
    {
        var qa = m.QuizAnalytics;
        // Bucket distribution slide
        var dist = new Dictionary<string, double>
        {
            ["منخفض (0-2)"] = qa.Bucket0to2,
            ["متوسط (3-4)"] = qa.Bucket3to4,
            ["ممتاز (5)"] = qa.Bucket5,
        };
        var body = new List<Line>
        {
            L($"إجمالي المحاولات: {qa.TotalAttempts} · المتوسط: {qa.AvgScore:0.##} / 5", 14, true, Navy),
        };
        deck.BarChartSlide("توزيع نتائج الاختبار", "عدد المحاولات حسب الفئة", dist, body);

        if (qa.Top3MostMissed.Count > 0)
        {
            var data = new Dictionary<string, double>();
            foreach (var q in qa.Top3MostMissed)
                data[Trim(q.QuestionAr, 35)] = q.MissRate;
            deck.BarChartSlide("أكثر الأسئلة صعوبة", "نسبة الخطأ %", data);
        }
        if (qa.Top3Strongest.Count > 0)
        {
            var data = new Dictionary<string, double>();
            foreach (var q in qa.Top3Strongest)
                data[Trim(q.QuestionAr, 35)] = 100 - q.MissRate;
            deck.BarChartSlide("نقاط القوة المعرفية", "نسبة الصواب %", data);
        }
    }

    private static void SurveySlide(PptxDeck deck, ExecutiveReportViewModel m)
    {
        if (m.SurveyMetrics.Count == 0)
        {
            deck.ContentSlide("تحليلات الاستبيان الرسمي", new[] { L("لا توجد بيانات استبيان بعد.", 14, false, TextMd) });
            return;
        }
        // Show numeric metrics only (where headline parses to a number)
        var data = new Dictionary<string, double>();
        var fallback = new List<Line>();
        foreach (var sv in m.SurveyMetrics.OrderBy(x => x.Order))
        {
            var n = ExtractFirstNumber(sv.Headline);
            if (n.HasValue)
                data[$"س{sv.Order}"] = n.Value;
            fallback.Add(L($"س{sv.Order}: {Trim(sv.QuestionAr, 60)}", 13, true, Navy));
            fallback.Add(L(sv.Headline, 12, false, TextDk));
        }
        if (data.Count >= 2)
            deck.BarChartSlide("ملخص مؤشرات الاستبيان", "القيم العددية للأسئلة", data, fallback.Take(8));
        else
            deck.ContentSlide("تحليلات الاستبيان الرسمي", fallback);
    }

    private static void ContributionsSlides(PptxDeck deck, ExecutiveReportViewModel m)
    {
        if (m.Contributions.TopObjectives.Count > 0)
        {
            var data = new Dictionary<string, double>();
            foreach (var o in m.Contributions.TopObjectives.Take(8))
                data[Trim(o.Name, 40)] = o.Count;
            var body = new List<Line> { L($"إجمالي التعهدات: {m.Contributions.TotalPledges}", 14, true, Navy) };
            deck.BarChartSlide("أبرز الأهداف", "عدد التعهدات", data, body);
        }
        if (m.Contributions.TopInitiatives.Count > 0)
        {
            var data = new Dictionary<string, double>();
            foreach (var i in m.Contributions.TopInitiatives.Take(8))
                data[Trim(i.Name, 40)] = i.Count;
            deck.BarChartSlide("أبرز المبادرات", "عدد التعهدات", data);
        }
    }

    private static void SignaturesSlide(PptxDeck deck, ExecutiveReportViewModel m)
    {
        var lines = new List<Line>
        {
            L($"إجمالي تواقيع الفرق: {m.GroupSignatures.TotalCount}", 17, true, Navy),
            L($"الخرائط الاستراتيجية: {m.MapsCount}", 14, false, TextDk),
        };
        if (m.GroupSignatures.TopKeywords.Count > 0)
            lines.Add(L("موضوعات شائعة: " + string.Join("، ", m.GroupSignatures.TopKeywords.Take(8).Select(k => $"{k.Name} ({k.Count})")), 12, false, TextMd));
        foreach (var c in m.GroupSignatures.RecentComments.Take(4))
            lines.Add(L($"• {c.DeptName}: {Trim(c.Text, 110)}", 12, false, TextDk));
        deck.ContentSlide("التوقيعات والتعليقات", lines);

        // Bonus: top-keywords bar chart
        if (m.GroupSignatures.TopKeywords.Count >= 3)
        {
            var data = new Dictionary<string, double>();
            foreach (var k in m.GroupSignatures.TopKeywords.Take(8))
                data[Trim(k.Name, 25)] = k.Count;
            deck.BarChartSlide("الكلمات الأبرز في التعليقات", "عدد المرات", data);
        }
    }

    private static void AlignmentSlide(PptxDeck deck, ExecutiveReportViewModel m)
    {
        var al = m.LeadershipAlignment;
        if (al.PillarShares.Count == 0)
        {
            deck.ContentSlide("الاتساق الاستراتيجي", new[] { L("لا توجد مساهمات مرتبطة بعد.", 14, false, TextMd) });
            return;
        }
        var data = new Dictionary<string, double>();
        foreach (var ps in al.PillarShares)
            data[Trim(ps.PillarName, 30)] = ps.Count;

        var body = new List<Line>
        {
            L($"إجمالي المساهمات: {al.TotalContributions}", 14, true, Navy),
        };
        foreach (var ps in al.PillarShares)
            body.Add(L($"• {ps.PillarName}: {ps.Count} ({ps.Percent:0.#}%)", 12, false, TextDk));
        foreach (var g in al.Gaps.Take(3))
            body.Add(L("⚠ " + g, 12, false, TextMd));
        deck.PieChartSlide("توزيع المساهمات على الركائز", "حصص الركائز", data, body);
    }

    private static void CultureSlide(PptxDeck deck, ExecutiveReportViewModel m)
    {
        var cu = m.LeadershipCulture;
        // KPI-style cards for sentiment + score
        deck.KpiSlide("الثقافة والمشاركة", new (string, string)[]
        {
            ($"{cu.TeamSpiritScore:0.#}/100", "مؤشر روح الفريق"),
            (cu.TeamSpiritLabel, "التصنيف"),
            (cu.PositiveComments.ToString(), "تعليقات إيجابية"),
            (cu.NeutralComments.ToString(), "تعليقات محايدة"),
            (cu.NegativeComments.ToString(), "تعليقات سلبية"),
            (cu.DepartmentParticipation.Count.ToString(), "إدارة مشاركة"),
        });

        if (cu.DepartmentParticipation.Count > 0)
        {
            var data = new Dictionary<string, double>();
            foreach (var d in cu.DepartmentParticipation.Take(10))
                data[Trim(d.DeptName, 25)] = d.Attendees;
            deck.BarChartSlide("المشاركة حسب الإدارة", "عدد الحضور", data);
        }
    }

    private static void RisksSlides(PptxDeck deck, ExecutiveReportViewModel m)
    {
        var ri = m.LeadershipRisks;
        if (ri.TopChallenges.Count > 0)
        {
            var data = new Dictionary<string, double>();
            foreach (var c in ri.TopChallenges.Take(8))
                data[Trim(c.Category, 30)] = c.Count;
            deck.BarChartSlide("أبرز التحديات (س4)", "العدد", data);
        }
        if (ri.TopOpportunities.Count > 0)
        {
            var data = new Dictionary<string, double>();
            foreach (var o in ri.TopOpportunities.Take(8))
                data[Trim(o.Category, 30)] = o.Count;
            deck.BarChartSlide("أبرز الفرص (س7)", "العدد", data);
        }
        if (ri.RiskHeatmap.Count > 0)
        {
            var lines = new List<Line> { L("الأكثر ذكراً للتحديات:", 14, true, Navy) };
            foreach (var h in ri.RiskHeatmap.Take(8))
                lines.Add(L($"• {h.Name} ({h.Count})", 13, false, TextDk));
            deck.ContentSlide("الخريطة الحرارية للمخاطر", lines);
        }
    }

    private static void MaturitySlide(PptxDeck deck, ExecutiveReportViewModel m)
    {
        var ma = m.LeadershipMaturity;
        deck.KpiSlide("النضج التنظيمي — الملخص", new (string, string)[]
        {
            (ma.MatureCount.ToString(), "ناضجة"),
            (ma.DevelopingCount.ToString(), "متطورة"),
            (ma.NeedsSupportCount.ToString(), "بحاجة دعم"),
        });

        if (ma.Departments.Count > 0)
        {
            var data = new Dictionary<string, double>();
            foreach (var d in ma.Departments.Take(10))
                data[Trim(d.DeptName, 25)] = Math.Round(d.Score, 2);
            deck.BarChartSlide("النضج حسب الإدارة", "المؤشر (من 5)", data);
        }
    }

    private static void RecommendationsSlide(PptxDeck deck, ExecutiveReportViewModel m)
    {
        var lines = new List<Line>();
        if (m.LeadershipRecommendations.Count == 0)
            lines.Add(L("لا توجد توصيات كافية بعد.", 14, false, TextMd));
        foreach (var r in m.LeadershipRecommendations)
            lines.Add(L("• " + r, 14, false, TextDk));
        lines.Add(L("شكراً لكل الإدارات على مشاركتها في بناء البيت الاستراتيجي.", 14, true, Green));
        deck.ContentSlide("توصيات القيادة", lines);
    }

    private static string Trim(string s, int max)
        => string.IsNullOrEmpty(s) ? "—" : (s.Length <= max ? s : s.Substring(0, max - 1) + "…");

    // Extract first numeric value from a localised string ("4.2 / 5", "67%" etc).
    private static double? ExtractFirstNumber(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var sb = new System.Text.StringBuilder();
        bool sawDigit = false, sawDot = false;
        foreach (var ch in s)
        {
            if (char.IsDigit(ch)) { sb.Append(ch); sawDigit = true; }
            else if ((ch == '.' || ch == ',') && sawDigit && !sawDot) { sb.Append('.'); sawDot = true; }
            else if (sawDigit) break;
        }
        if (!sawDigit) return null;
        return double.TryParse(sb.ToString(), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private string? WhiteLogo() => Path.Combine(_env.WebRootPath ?? "", "images", "gac-logo-white.png");
    private string? ColorLogo() => Path.Combine(_env.WebRootPath ?? "", "images", "gac-logo-color.png");
}
