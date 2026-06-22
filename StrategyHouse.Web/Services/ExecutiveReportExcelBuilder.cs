using ClosedXML.Excel;
using StrategyHouse.Web.Models;
using static StrategyHouse.Web.Services.XlsxReportStyle;

namespace StrategyHouse.Web.Services;

// Phase 13.1 / 14 — branded .xlsx export of the comprehensive executive report. One sheet
// per selected section (overview, departments, quiz, survey, contributions, signatures, plus
// the Phase 14 leadership sheets). Shared GAC styling via XlsxReportStyle.
public class ExecutiveReportExcelBuilder
{
    public byte[] Build(ExecutiveReportViewModel m)
    {
        using var wb = new XLWorkbook();
        wb.Style.Font.FontName = "Cairo"; // Phase 20.10 — unify exports on website font
        var s = m.Sections;

        if (s.Has(ExecReportSections.Overview)) BuildOverview(wb, m);
        if (s.Has(ExecReportSections.Departments)) BuildDepartments(wb, m);
        if (s.Has(ExecReportSections.Quiz)) BuildQuiz(wb, m);
        if (s.Has(ExecReportSections.Survey)) BuildSurvey(wb, m);
        if (s.Has(ExecReportSections.Contributions)) BuildContributions(wb, m);
        if (s.Has(ExecReportSections.Signatures)) BuildSignatures(wb, m);
        if (s.Has(ExecReportSections.LeadershipAlignment)) BuildAlignment(wb, m);
        if (s.Has(ExecReportSections.LeadershipCulture)) BuildCulture(wb, m);
        if (s.Has(ExecReportSections.LeadershipRisks)) BuildRisks(wb, m);
        if (s.Has(ExecReportSections.LeadershipMaturity)) BuildMaturity(wb, m);
        if (s.Has(ExecReportSections.LeadershipRecommendations)) BuildRecommendations(wb, m);

        // ClosedXML refuses to save a workbook with no worksheets; guarantee at least one.
        if (!wb.Worksheets.Any())
            NewSheet(wb, "التقرير").Cell(1, 1).SetValue("لم يتم اختيار أي قسم.");

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private void BuildOverview(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "نظرة عامة");
        Title(ws, 1, "النظرة العامة على رحلة بناء البيت الاستراتيجي");
        ws.Cell(2, 1).Value = "تم الإنشاء: " + m.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        HeaderRow(ws, 4, "المؤشر", "القيمة");
        int r = 5;
        DataRow(ws, r++, "إجمالي الجلسات", m.Overview.TotalSessions);
        DataRow(ws, r++, "الجلسات المكتملة", m.Overview.TotalCompletedSessions);
        DataRow(ws, r++, "إجمالي الحضور", m.Overview.TotalAttendees);
        DataRow(ws, r++, "الإدارات المشاركة", m.Overview.TotalDepartmentsEngaged);
        DataRow(ws, r++, "متوسط الاختبار (من 5)", m.Overview.AvgQuizScore.ToString("0.##"));
        DataRow(ws, r++, "وضوح الاستراتيجية (من 5)", m.Overview.AvgSurveyClarity > 0 ? m.Overview.AvgSurveyClarity.ToString("0.##") : "—");
        DataRow(ws, r++, "القدرة على المساهمة (من 5)", m.Overview.AvgContributionCapability > 0 ? m.Overview.AvgContributionCapability.ToString("0.##") : "—");
        DataRow(ws, r++, "الخرائط الاستراتيجية", m.MapsCount);
        TotalRow(ws, r++, "تواقيع الفرق", m.GroupSignatures.TotalCount);
        Finish(ws);
    }

    private void BuildDepartments(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "الإدارات");
        Title(ws, 1, "الحضور والإكمال حسب الإدارة");
        HeaderRow(ws, 3, "الترتيب", "الإدارة", "الجلسات", "الحضور", "نسبة الإكمال %");
        int r = 4;
        foreach (var d in m.DepartmentBreakdown)
        {
            ws.Cell(r, 1).Value = d.Rank;
            ws.Cell(r, 2).Value = d.DeptName;
            ws.Cell(r, 3).Value = d.SessionsCount;
            ws.Cell(r, 4).Value = d.AttendeesCount;
            ws.Cell(r, 5).Value = d.CompletionRate;
            r++;
        }
        if (m.DepartmentBreakdown.Count > 0)
        {
            ws.Cell(r, 1).Value = "";
            ws.Cell(r, 2).Value = "الإجمالي";
            ws.Cell(r, 3).Value = m.DepartmentBreakdown.Sum(d => d.SessionsCount);
            ws.Cell(r, 4).Value = m.DepartmentBreakdown.Sum(d => d.AttendeesCount);
            ws.Cell(r, 5).Value = "—";
            StyleTotal(ws.Range(r, 1, r, 5));
        }
        Finish(ws);
    }

    private void BuildQuiz(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "الاختبار");
        Title(ws, 1, "تحليلات الاختبار");
        var qa = m.QuizAnalytics;
        ws.Cell(2, 1).Value = $"إجمالي المحاولات: {qa.TotalAttempts} · المتوسط: {qa.AvgScore:0.##} / 5";

        HeaderRow(ws, 4, "فئة النتيجة", "عدد المحاولات");
        int r = 5;
        DataRow(ws, r++, "منخفض (0-2)", qa.Bucket0to2);
        DataRow(ws, r++, "متوسط (3-4)", qa.Bucket3to4);
        DataRow(ws, r++, "ممتاز (5)", qa.Bucket5);
        TotalRow(ws, r++, "الإجمالي", qa.Bucket0to2 + qa.Bucket3to4 + qa.Bucket5);

        r++;
        ws.Cell(r, 1).Value = "أكثر الأسئلة صعوبة";
        ws.Cell(r, 1).Style.Font.Bold = true;
        ws.Cell(r, 1).Style.Font.FontColor = Navy;
        r++;
        HeaderRow(ws, r++, "السؤال", "نسبة الخطأ %", "عدد المحاولات");
        if (qa.Top3MostMissed.Count == 0)
        {
            ws.Cell(r++, 1).Value = "لا توجد بيانات بعد.";
        }
        else
        {
            foreach (var q in qa.Top3MostMissed)
            {
                ws.Cell(r, 1).Value = q.QuestionAr;
                ws.Cell(r, 2).Value = q.MissRate;
                ws.Cell(r, 3).Value = q.Attempts;
                r++;
            }
        }
        Finish(ws);
    }

    private void BuildSurvey(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "الاستبيان");
        Title(ws, 1, "مؤشرات الاستبيان الرسمي");
        HeaderRow(ws, 3, "السؤال", "النوع", "المؤشر");
        int r = 4;
        if (m.SurveyMetrics.Count == 0)
        {
            ws.Cell(r++, 1).Value = "لا توجد بيانات استبيان بعد.";
        }
        else
        {
            foreach (var s in m.SurveyMetrics.OrderBy(x => x.Order))
            {
                ws.Cell(r, 1).Value = $"س{s.Order}: {s.QuestionAr}";
                ws.Cell(r, 2).Value = s.Type;
                ws.Cell(r, 3).Value = s.Headline;
                r++;
            }
        }
        Finish(ws);
    }

    private void BuildContributions(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "المساهمات");
        Title(ws, 1, "المساهمات الأبرز");
        ws.Cell(2, 1).Value = $"إجمالي التعهدات: {m.Contributions.TotalPledges}";

        HeaderRow(ws, 4, "أبرز الأهداف", "عدد التعهدات");
        int r = 5;
        if (m.Contributions.TopObjectives.Count == 0) ws.Cell(r++, 1).Value = "—";
        foreach (var o in m.Contributions.TopObjectives)
        {
            ws.Cell(r, 1).Value = o.Name;
            ws.Cell(r, 2).Value = o.Count;
            r++;
        }

        r++;
        HeaderRow(ws, r++, "أبرز المبادرات", "عدد التعهدات");
        if (m.Contributions.TopInitiatives.Count == 0) ws.Cell(r++, 1).Value = "—";
        foreach (var i in m.Contributions.TopInitiatives)
        {
            ws.Cell(r, 1).Value = i.Name;
            ws.Cell(r, 2).Value = i.Count;
            r++;
        }
        Finish(ws);
    }

    private void BuildSignatures(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "التواقيع");
        Title(ws, 1, "تواقيع الفرق وتعليقاتها");
        TotalRow(ws, 2, "إجمالي التواقيع", m.GroupSignatures.TotalCount);

        HeaderRow(ws, 4, "الإدارة", "التعليق", "التاريخ");
        int r = 5;
        if (m.GroupSignatures.RecentComments.Count == 0)
        {
            ws.Cell(r++, 1).Value = "لا توجد تعليقات بعد.";
        }
        else
        {
            foreach (var c in m.GroupSignatures.RecentComments)
            {
                ws.Cell(r, 1).Value = c.DeptName;
                ws.Cell(r, 2).Value = c.Text;
                ws.Cell(r, 3).Value = c.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                r++;
            }
        }
        Finish(ws);
    }

    private void BuildAlignment(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "الاتساق الاستراتيجي");
        Title(ws, 1, "توزيع المساهمات على الركائز الاستراتيجية");
        ws.Cell(2, 1).Value = $"إجمالي المساهمات المرتبطة بالركائز: {m.LeadershipAlignment.TotalContributions}";
        HeaderRow(ws, 4, "الركيزة", "عدد المساهمات", "النسبة %");
        int r = 5;
        if (m.LeadershipAlignment.PillarShares.Count == 0) ws.Cell(r++, 1).Value = "—";
        foreach (var ps in m.LeadershipAlignment.PillarShares)
        {
            ws.Cell(r, 1).Value = ps.PillarName;
            ws.Cell(r, 2).Value = ps.Count;
            ws.Cell(r, 3).Value = ps.Percent;
            r++;
        }
        if (m.LeadershipAlignment.Gaps.Count > 0)
        {
            r++;
            ws.Cell(r++, 1).Value = "فجوات الاتساق:";
            foreach (var g in m.LeadershipAlignment.Gaps)
                ws.Cell(r++, 1).Value = g;
        }
        Finish(ws);
    }

    private void BuildCulture(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "الثقافة والمشاركة");
        Title(ws, 1, "الثقافة والمشاركة");
        var cu = m.LeadershipCulture;
        ws.Cell(2, 1).Value = $"مؤشر روح الفريق: {cu.TeamSpiritScore:0.#}/100 ({cu.TeamSpiritLabel})";
        ws.Cell(3, 1).Value = $"التعليقات — إيجابية: {cu.PositiveComments} · محايدة: {cu.NeutralComments} · سلبية: {cu.NegativeComments}";
        HeaderRow(ws, 5, "الإدارة", "الحضور");
        int r = 6;
        if (cu.DepartmentParticipation.Count == 0) ws.Cell(r++, 1).Value = "—";
        foreach (var d in cu.DepartmentParticipation)
        {
            ws.Cell(r, 1).Value = d.DeptName;
            ws.Cell(r, 2).Value = d.Attendees;
            r++;
        }
        Finish(ws);
    }

    private void BuildRisks(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "المخاطر والفرص");
        Title(ws, 1, "المخاطر والفرص");
        HeaderRow(ws, 3, "أبرز التحديات (س4)", "العدد", "النسبة %");
        int r = 4;
        if (m.LeadershipRisks.TopChallenges.Count == 0) ws.Cell(r++, 1).Value = "—";
        foreach (var c in m.LeadershipRisks.TopChallenges)
        {
            ws.Cell(r, 1).Value = c.Category;
            ws.Cell(r, 2).Value = c.Count;
            ws.Cell(r, 3).Value = c.Percent;
            r++;
        }
        r++;
        HeaderRow(ws, r++, "أبرز الفرص (س7)", "العدد", "النسبة %");
        if (m.LeadershipRisks.TopOpportunities.Count == 0) ws.Cell(r++, 1).Value = "—";
        foreach (var o in m.LeadershipRisks.TopOpportunities)
        {
            ws.Cell(r, 1).Value = o.Category;
            ws.Cell(r, 2).Value = o.Count;
            ws.Cell(r, 3).Value = o.Percent;
            r++;
        }
        Finish(ws);
    }

    private void BuildMaturity(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "النضج التنظيمي");
        Title(ws, 1, "النضج التنظيمي حسب الإدارة");
        var ma = m.LeadershipMaturity;
        ws.Cell(2, 1).Value = $"ناضجة: {ma.MatureCount} · متطورة: {ma.DevelopingCount} · بحاجة دعم: {ma.NeedsSupportCount}";
        HeaderRow(ws, 4, "الإدارة", "المؤشر (من 5)", "التصنيف");
        int r = 5;
        if (ma.Departments.Count == 0) ws.Cell(r++, 1).Value = "—";
        foreach (var d in ma.Departments)
        {
            ws.Cell(r, 1).Value = d.DeptName;
            ws.Cell(r, 2).Value = Math.Round(d.Score, 2);
            ws.Cell(r, 3).Value = d.Tier;
            r++;
        }
        Finish(ws);
    }

    private void BuildRecommendations(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "توصيات القيادة");
        Title(ws, 1, "توصيات القيادة");
        HeaderRow(ws, 3, "#", "التوصية");
        int r = 4;
        if (m.LeadershipRecommendations.Count == 0) ws.Cell(r++, 2).Value = "لا توجد توصيات كافية بعد.";
        int n = 1;
        foreach (var rec in m.LeadershipRecommendations)
        {
            ws.Cell(r, 1).Value = n++;
            ws.Cell(r, 2).Value = rec;
            r++;
        }
        Finish(ws);
    }

    private static void DataRow(IXLWorksheet ws, int row, string label, object value)
    {
        ws.Cell(row, 1).Value = label;
        ws.Cell(row, 2).Value = value.ToString();
    }

    private static void TotalRow(IXLWorksheet ws, int row, string label, object value)
    {
        ws.Cell(row, 1).Value = label;
        ws.Cell(row, 2).Value = value.ToString();
        StyleTotal(ws.Range(row, 1, row, 2));
    }
}
