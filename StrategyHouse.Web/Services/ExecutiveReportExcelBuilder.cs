using ClosedXML.Excel;
using StrategyHouse.Web.Models;
using static StrategyHouse.Web.Services.XlsxReportStyle;

namespace StrategyHouse.Web.Services;

// Phase 13.1 — branded .xlsx export of the comprehensive executive report. Six sheets
// (overview, departments, quiz, survey, contributions, signatures). Shared GAC styling
// via XlsxReportStyle.
public class ExecutiveReportExcelBuilder
{
    public byte[] Build(ExecutiveReportViewModel m)
    {
        using var wb = new XLWorkbook();

        BuildOverview(wb, m);
        BuildDepartments(wb, m);
        BuildQuiz(wb, m);
        BuildSurvey(wb, m);
        BuildContributions(wb, m);
        BuildSignatures(wb, m);

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
        HeaderRow(ws, 3, "الإدارة", "الجلسات", "الحضور", "نسبة الإكمال %");
        int r = 4;
        foreach (var d in m.DepartmentBreakdown)
        {
            ws.Cell(r, 1).Value = d.DeptName;
            ws.Cell(r, 2).Value = d.SessionsCount;
            ws.Cell(r, 3).Value = d.AttendeesCount;
            ws.Cell(r, 4).Value = d.CompletionRate;
            r++;
        }
        if (m.DepartmentBreakdown.Count > 0)
        {
            ws.Cell(r, 1).Value = "الإجمالي";
            ws.Cell(r, 2).Value = m.DepartmentBreakdown.Sum(d => d.SessionsCount);
            ws.Cell(r, 3).Value = m.DepartmentBreakdown.Sum(d => d.AttendeesCount);
            ws.Cell(r, 4).Value = "—";
            StyleTotal(ws.Range(r, 1, r, 4));
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
