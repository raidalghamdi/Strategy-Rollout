using StrategyHouse.Web.Models;
using static StrategyHouse.Web.Services.PptxDeck;

namespace StrategyHouse.Web.Services;

// Phase 14 — branded .pptx export of the comprehensive executive report, rebuilt on
// ShapeCrawler (the Phase 13.1 OpenXML builder produced files PowerPoint refused to open).
// Honours the section filter: only selected sections become slides. GAC navy/gold, RTL,
// Cairo latin font (fallback on the viewer).
public class ExecutiveReportPowerPointBuilder
{
    public byte[] Build(ExecutiveReportViewModel m)
    {
        var deck = new PptxDeck();
        var s = m.Sections;

        deck.TitleSlide("التقرير التنفيذي الشامل", new[]
        {
            L("بناء البيت الاستراتيجي — رحلة الإدارات", 20, false, Gold),
            L("الهيئة العامة للمنافسة", 16, false, PaleBlue),
            L("تاريخ الإصدار: " + m.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd"), 13, false, PaleBlue),
        });

        if (s.Has(ExecReportSections.Overview)) OverviewSlide(deck, m);
        if (s.Has(ExecReportSections.Departments)) DepartmentsSlide(deck, m);
        if (s.Has(ExecReportSections.Quiz)) QuizSlide(deck, m);
        if (s.Has(ExecReportSections.Survey)) SurveySlide(deck, m);
        if (s.Has(ExecReportSections.Contributions)) ContributionsSlide(deck, m);
        if (s.Has(ExecReportSections.Signatures)) SignaturesSlide(deck, m);
        if (s.Has(ExecReportSections.LeadershipAlignment)) AlignmentSlide(deck, m);
        if (s.Has(ExecReportSections.LeadershipCulture)) CultureSlide(deck, m);
        if (s.Has(ExecReportSections.LeadershipRisks)) RisksSlide(deck, m);
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
        var lines = new List<Line> { L("الترتيب — الإدارة — الجلسات — الحضور — الإكمال", 15, true, Gold) };
        if (m.DepartmentBreakdown.Count == 0)
            lines.Add(L("لا توجد بيانات إدارات بعد.", 15, false, White));
        foreach (var d in m.DepartmentBreakdown.Take(11))
            lines.Add(L($"{d.Rank}. {d.DeptName} — {d.SessionsCount} — {d.AttendeesCount} — {d.CompletionRate:0.#}%", 13, false, White));
        if (m.Overview.NotEngagedDepartments.Count > 0)
            lines.Add(L($"لم تشارك بعد: {string.Join("، ", m.Overview.NotEngagedDepartments.Take(6))}", 12, false, PaleBlue));
        deck.ContentSlide("تفصيل الإدارات", lines);
    }

    private static void QuizSlide(PptxDeck deck, ExecutiveReportViewModel m)
    {
        var qa = m.QuizAnalytics;
        var lines = new List<Line>
        {
            L($"إجمالي المحاولات: {qa.TotalAttempts} · المتوسط: {qa.AvgScore:0.##} / 5", 17, true, Gold),
            L($"منخفض (0-2): {qa.Bucket0to2}  ·  متوسط (3-4): {qa.Bucket3to4}  ·  ممتاز (5): {qa.Bucket5}", 14, false, White),
        };
        if (qa.Top3MostMissed.Count > 0)
        {
            lines.Add(L("أكثر الأسئلة صعوبة:", 15, true, Gold));
            foreach (var q in qa.Top3MostMissed)
                lines.Add(L($"• {q.QuestionAr} — نسبة الخطأ {q.MissRate:0.#}%", 12, false, White));
        }
        if (qa.Top3Strongest.Count > 0)
        {
            lines.Add(L("نقاط القوة المعرفية:", 15, true, Gold));
            foreach (var q in qa.Top3Strongest)
                lines.Add(L($"• {q.QuestionAr} — نسبة الصواب {(100 - q.MissRate):0.#}%", 12, false, White));
        }
        deck.ContentSlide("تحليلات الاختبار", lines);
    }

    private static void SurveySlide(PptxDeck deck, ExecutiveReportViewModel m)
    {
        var lines = new List<Line>();
        if (m.SurveyMetrics.Count == 0)
            lines.Add(L("لا توجد بيانات استبيان بعد.", 15, false, White));
        foreach (var sv in m.SurveyMetrics.OrderBy(x => x.Order))
        {
            lines.Add(L($"س{sv.Order}: {sv.QuestionAr}", 14, true, Gold));
            lines.Add(L($"{sv.Type} · {sv.Headline}", 12, false, White));
        }
        deck.ContentSlide("تحليلات الاستبيان الرسمي", lines);
    }

    private static void ContributionsSlide(PptxDeck deck, ExecutiveReportViewModel m)
    {
        var lines = new List<Line>
        {
            L($"إجمالي التعهدات: {m.Contributions.TotalPledges}", 17, true, Gold),
            L("أبرز الأهداف:", 14, true, White),
        };
        if (m.Contributions.TopObjectives.Count == 0) lines.Add(L("—", 13, false, White));
        foreach (var o in m.Contributions.TopObjectives.Take(5))
            lines.Add(L($"• {o.Name} ({o.Count})", 13, false, White));
        lines.Add(L("أبرز المبادرات:", 14, true, White));
        if (m.Contributions.TopInitiatives.Count == 0) lines.Add(L("—", 13, false, White));
        foreach (var i in m.Contributions.TopInitiatives.Take(5))
            lines.Add(L($"• {i.Name} ({i.Count})", 13, false, White));
        deck.ContentSlide("المساهمات", lines);
    }

    private static void SignaturesSlide(PptxDeck deck, ExecutiveReportViewModel m)
    {
        var lines = new List<Line>
        {
            L($"إجمالي تواقيع الفرق: {m.GroupSignatures.TotalCount}", 17, true, Gold),
            L($"الخرائط الاستراتيجية: {m.MapsCount}", 14, false, White),
        };
        if (m.GroupSignatures.TopKeywords.Count > 0)
            lines.Add(L("موضوعات شائعة: " + string.Join("، ", m.GroupSignatures.TopKeywords.Take(8).Select(k => $"{k.Name} ({k.Count})")), 12, false, PaleBlue));
        foreach (var c in m.GroupSignatures.RecentComments.Take(4))
            lines.Add(L($"• {c.DeptName}: {c.Text}", 12, false, White));
        deck.ContentSlide("التوقيعات والتعليقات", lines);
    }

    private static void AlignmentSlide(PptxDeck deck, ExecutiveReportViewModel m)
    {
        var al = m.LeadershipAlignment;
        var lines = new List<Line>
        {
            L($"إجمالي المساهمات المرتبطة بالركائز: {al.TotalContributions}", 16, true, Gold),
        };
        foreach (var ps in al.PillarShares)
            lines.Add(L($"• {ps.PillarName}: {ps.Count} ({ps.Percent:0.#}%)", 13, false, White));
        foreach (var g in al.Gaps.Take(3))
            lines.Add(L($"⚠ {g}", 12, false, PaleBlue));
        deck.ContentSlide("الاتساق الاستراتيجي", lines);
    }

    private static void CultureSlide(PptxDeck deck, ExecutiveReportViewModel m)
    {
        var cu = m.LeadershipCulture;
        var lines = new List<Line>
        {
            L($"مؤشر روح الفريق: {cu.TeamSpiritScore:0.#}/100 ({cu.TeamSpiritLabel})", 16, true, Gold),
            L($"التعليقات — إيجابية: {cu.PositiveComments} · محايدة: {cu.NeutralComments} · سلبية: {cu.NegativeComments}", 13, false, White),
            L("المشاركة حسب الإدارة (الحضور):", 14, true, Gold),
        };
        foreach (var d in cu.DepartmentParticipation.Take(8))
            lines.Add(L($"• {d.DeptName}: {d.Attendees}", 12, false, White));
        deck.ContentSlide("الثقافة والمشاركة", lines);
    }

    private static void RisksSlide(PptxDeck deck, ExecutiveReportViewModel m)
    {
        var ri = m.LeadershipRisks;
        var lines = new List<Line> { L("أبرز التحديات (س4):", 15, true, Gold) };
        if (ri.TopChallenges.Count == 0) lines.Add(L("—", 13, false, White));
        foreach (var c in ri.TopChallenges.Take(4))
            lines.Add(L($"• {c.Category} ({c.Count})", 13, false, White));
        lines.Add(L("أبرز الفرص (س7):", 15, true, Gold));
        if (ri.TopOpportunities.Count == 0) lines.Add(L("—", 13, false, White));
        foreach (var o in ri.TopOpportunities.Take(4))
            lines.Add(L($"• {o.Category} ({o.Count})", 13, false, White));
        if (ri.RiskHeatmap.Count > 0)
            lines.Add(L("الأكثر ذكراً للتحديات: " + string.Join("، ", ri.RiskHeatmap.Take(4).Select(h => $"{h.Name} ({h.Count})")), 12, false, PaleBlue));
        deck.ContentSlide("المخاطر والفرص", lines);
    }

    private static void MaturitySlide(PptxDeck deck, ExecutiveReportViewModel m)
    {
        var ma = m.LeadershipMaturity;
        var lines = new List<Line>
        {
            L($"ناضجة: {ma.MatureCount} · متطورة: {ma.DevelopingCount} · بحاجة دعم: {ma.NeedsSupportCount}", 16, true, Gold),
        };
        foreach (var d in ma.Departments.Take(9))
            lines.Add(L($"• {d.DeptName}: {d.Score:0.##}/5 — {d.Tier}", 12, false, White));
        deck.ContentSlide("النضج التنظيمي", lines);
    }

    private static void RecommendationsSlide(PptxDeck deck, ExecutiveReportViewModel m)
    {
        var lines = new List<Line>();
        if (m.LeadershipRecommendations.Count == 0)
            lines.Add(L("لا توجد توصيات كافية بعد.", 15, false, White));
        foreach (var r in m.LeadershipRecommendations)
            lines.Add(L($"• {r}", 14, false, White));
        lines.Add(L("شكراً لكل الإدارات على مشاركتها في بناء البيت الاستراتيجي.", 14, true, Gold));
        deck.ContentSlide("توصيات القيادة", lines);
    }
}
