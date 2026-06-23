using ClosedXML.Excel;
using StrategyHouse.Web.Models;
using static StrategyHouse.Web.Services.XlsxReportStyle;

namespace StrategyHouse.Web.Services;

// Phase 20.24 — branded .xlsx export of the comprehensive executive report. Same data
// as before, but with the GAC visual identity:
//   • Brand header band + lime stripe on every sheet
//   • KPI strip on the overview sheet (mirrors PPTX KpiSlide)
//   • In-cell data-bar charts inside count/percent columns for departments, quiz,
//     contributions, pillar shares, risks, maturity (mirrors PDF charts)
//   • Striped tables with corporate palette
public class ExecutiveReportExcelBuilder
{
    public byte[] Build(ExecutiveReportViewModel m)
    {
        using var wb = new XLWorkbook();
        wb.Style.Font.FontName = "Cairo";
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

        if (!wb.Worksheets.Any())
            NewSheet(wb, "التقرير").Cell(1, 1).SetValue("لم يتم اختيار أي قسم.");

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private void BuildOverview(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "نظرة عامة");
        BrandHeader(ws, 1, "التقرير التنفيذي الشامل",
            "تم الإنشاء: " + m.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));

        var kpis = new List<(string, string)>
        {
            (m.Overview.TotalSessions.ToString(), "إجمالي الجلسات"),
            (m.Overview.TotalAttendees.ToString(), "إجمالي الحضور"),
            ($"{m.Overview.TotalDepartmentsEngaged}/{m.Overview.TotalDepartments}", "الإدارات المشاركة"),
            ($"{m.Overview.CompletionPercentage:0.#}%", "نسبة الإكمال"),
            ($"{m.Overview.AvgQuizScore:0.##}/5", "متوسط الاختبار"),
            (m.Overview.AvgSurveyClarity > 0 ? $"{m.Overview.AvgSurveyClarity:0.##}/5" : "—", "وضوح الاستراتيجية"),
        };
        KpiStrip(ws, 5, kpis, startCol: 1, totalCols: 6);

        int r = 9;
        SectionDivider(ws, r++, "المؤشرات التفصيلية", width: 6);
        HeaderRow(ws, r++, "المؤشر", "القيمة");
        int dataStart = r;
        DataRow(ws, r++, "إجمالي الجلسات", m.Overview.TotalSessions);
        DataRow(ws, r++, "الجلسات المكتملة", m.Overview.TotalCompletedSessions);
        DataRow(ws, r++, "إجمالي الحضور", m.Overview.TotalAttendees);
        DataRow(ws, r++, "الإدارات المشاركة", m.Overview.TotalDepartmentsEngaged);
        DataRow(ws, r++, "متوسط الاختبار (من 5)", m.Overview.AvgQuizScore.ToString("0.##"));
        DataRow(ws, r++, "وضوح الاستراتيجية (من 5)", m.Overview.AvgSurveyClarity > 0 ? m.Overview.AvgSurveyClarity.ToString("0.##") : "—");
        DataRow(ws, r++, "القدرة على المساهمة (من 5)", m.Overview.AvgContributionCapability > 0 ? m.Overview.AvgContributionCapability.ToString("0.##") : "—");
        DataRow(ws, r++, "الخرائط الاستراتيجية", m.MapsCount);
        StripeBand(ws, dataStart, r - 1, 2);
        TotalRow(ws, r++, "تواقيع الفرق", m.GroupSignatures.TotalCount);

        Finish(ws);
        for (int c = 1; c <= 6; c++) ws.Column(c).Width = 18;
    }

    private void BuildDepartments(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "الإدارات");
        BrandHeader(ws, 1, "الحضور والإكمال حسب الإدارة",
            "الترتيب بعدد الحضور — مع رسم بياني داخل الخلية", width: 6);

        int r = 5;
        HeaderRow(ws, r, "الترتيب", "الإدارة", "الجلسات", "الحضور", "نسبة الإكمال %", "الرسم");
        int dataStart = r + 1;
        r++;
        foreach (var d in m.DepartmentBreakdown)
        {
            ws.Cell(r, 1).Value = d.Rank;
            ws.Cell(r, 2).Value = d.DeptName;
            ws.Cell(r, 3).Value = d.SessionsCount;
            ws.Cell(r, 4).Value = d.AttendeesCount;
            ws.Cell(r, 5).Value = d.CompletionRate;
            ws.Cell(r, 6).Value = d.AttendeesCount;
            ws.Cell(r, 6).Style.Font.FontColor = White;
            r++;
        }
        int dataEnd = r - 1;
        if (m.DepartmentBreakdown.Count > 0)
        {
            StripeBand(ws, dataStart, dataEnd, 6);
            DataBarColumn(ws.Range(dataStart, 6, dataEnd, 6), Blue);
            ws.Range(dataStart, 1, dataEnd, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(dataStart, 3, dataEnd, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(dataStart, 5, dataEnd, 5).Style.NumberFormat.Format = "0.0";

            ws.Cell(r, 1).Value = "";
            ws.Cell(r, 2).Value = "الإجمالي";
            ws.Cell(r, 3).Value = m.DepartmentBreakdown.Sum(d => d.SessionsCount);
            ws.Cell(r, 4).Value = m.DepartmentBreakdown.Sum(d => d.AttendeesCount);
            ws.Cell(r, 5).Value = "—";
            ws.Cell(r, 6).Value = "";
            StyleTotal(ws.Range(r, 1, r, 6));
        }
        Finish(ws);
        ws.Column(2).Width = 30; ws.Column(6).Width = 26;
    }

    private void BuildQuiz(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "الاختبار");
        var qa = m.QuizAnalytics;
        BrandHeader(ws, 1, "تحليلات الاختبار",
            $"إجمالي المحاولات: {qa.TotalAttempts} · المتوسط: {qa.AvgScore:0.##} / 5", width: 4);

        // KPI strip
        KpiStrip(ws, 5, new List<(string, string)>
        {
            (qa.TotalAttempts.ToString(), "إجمالي المحاولات"),
            ($"{qa.AvgScore:0.##}/5", "المتوسط"),
            (qa.Bucket5.ToString(), "ممتاز (5)"),
            (qa.Bucket0to2.ToString(), "منخفض (0-2)"),
        }, totalCols: 4);

        int r = 9;
        SectionDivider(ws, r++, "توزيع النتائج", width: 4);
        HeaderRow(ws, r, "فئة النتيجة", "عدد المحاولات", "النسبة %", "الرسم");
        int dataStart = r + 1;
        r++;
        int total = Math.Max(1, qa.Bucket0to2 + qa.Bucket3to4 + qa.Bucket5);
        AddDist(ws, r++, "منخفض (0-2)", qa.Bucket0to2, total);
        AddDist(ws, r++, "متوسط (3-4)", qa.Bucket3to4, total);
        AddDist(ws, r++, "ممتاز (5)", qa.Bucket5, total);
        int dataEnd = r - 1;
        StripeBand(ws, dataStart, dataEnd, 4);
        DataBarColumn(ws.Range(dataStart, 4, dataEnd, 4), Green);
        ws.Range(dataStart, 2, dataEnd, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(dataStart, 3, dataEnd, 3).Style.NumberFormat.Format = "0.0";
        TotalRow(ws, r++, "الإجمالي", total);

        // Top missed
        r++;
        SectionDivider(ws, r++, "أكثر الأسئلة صعوبة", width: 4);
        HeaderRow(ws, r, "السؤال", "نسبة الخطأ %", "عدد المحاولات", "الرسم");
        int mStart = r + 1;
        r++;
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
                ws.Cell(r, 4).Value = q.MissRate;
                ws.Cell(r, 4).Style.Font.FontColor = White;
                r++;
            }
            int mEnd = r - 1;
            StripeBand(ws, mStart, mEnd, 4);
            DataBarColumn(ws.Range(mStart, 4, mEnd, 4), XLColor.FromHtml("#A13544")); // red for "missed"
            ws.Range(mStart, 2, mEnd, 2).Style.NumberFormat.Format = "0.0";
            ws.Range(mStart, 1, mEnd, 1).Style.Alignment.WrapText = true;
        }

        // Strongest
        if (qa.Top3Strongest.Count > 0)
        {
            r++;
            SectionDivider(ws, r++, "نقاط القوة المعرفية", width: 4);
            HeaderRow(ws, r, "السؤال", "نسبة الصواب %", "عدد المحاولات", "الرسم");
            int sStart = r + 1;
            r++;
            foreach (var q in qa.Top3Strongest)
            {
                double correct = 100 - q.MissRate;
                ws.Cell(r, 1).Value = q.QuestionAr;
                ws.Cell(r, 2).Value = correct;
                ws.Cell(r, 3).Value = q.Attempts;
                ws.Cell(r, 4).Value = correct;
                ws.Cell(r, 4).Style.Font.FontColor = White;
                r++;
            }
            int sEnd = r - 1;
            StripeBand(ws, sStart, sEnd, 4);
            DataBarColumn(ws.Range(sStart, 4, sEnd, 4), Green);
            ws.Range(sStart, 2, sEnd, 2).Style.NumberFormat.Format = "0.0";
            ws.Range(sStart, 1, sEnd, 1).Style.Alignment.WrapText = true;
        }

        Finish(ws);
        ws.Column(1).Width = 40; ws.Column(4).Width = 24;
    }

    private static void AddDist(IXLWorksheet ws, int row, string label, int count, int total)
    {
        ws.Cell(row, 1).Value = label;
        ws.Cell(row, 2).Value = count;
        ws.Cell(row, 3).Value = total > 0 ? Math.Round(100.0 * count / total, 1) : 0;
        ws.Cell(row, 4).Value = count;
        ws.Cell(row, 4).Style.Font.FontColor = White;
    }

    private void BuildSurvey(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "الاستبيان");
        BrandHeader(ws, 1, "مؤشرات الاستبيان الرسمي", width: 3);
        int r = 5;
        HeaderRow(ws, r++, "السؤال", "النوع", "المؤشر");
        int dataStart = r;
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
                ws.Cell(r, 1).Style.Alignment.WrapText = true;
                ws.Cell(r, 3).Style.Alignment.WrapText = true;
                r++;
            }
            StripeBand(ws, dataStart, r - 1, 3);
        }
        Finish(ws);
        ws.Column(1).Width = 38; ws.Column(3).Width = 40;
    }

    private void BuildContributions(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "المساهمات");
        BrandHeader(ws, 1, "المساهمات الأبرز",
            $"إجمالي التعهدات: {m.Contributions.TotalPledges}", width: 3);

        int r = 5;
        SectionDivider(ws, r++, "أبرز الأهداف", width: 3);
        HeaderRow(ws, r, "الهدف", "عدد التعهدات", "الرسم");
        int oStart = r + 1;
        r++;
        if (m.Contributions.TopObjectives.Count == 0) { ws.Cell(r++, 1).Value = "—"; }
        foreach (var o in m.Contributions.TopObjectives)
        {
            ws.Cell(r, 1).Value = o.Name;
            ws.Cell(r, 2).Value = o.Count;
            ws.Cell(r, 3).Value = o.Count;
            ws.Cell(r, 3).Style.Font.FontColor = White;
            ws.Cell(r, 1).Style.Alignment.WrapText = true;
            r++;
        }
        int oEnd = r - 1;
        if (oEnd >= oStart)
        {
            StripeBand(ws, oStart, oEnd, 3);
            DataBarColumn(ws.Range(oStart, 3, oEnd, 3), Blue);
        }

        r++;
        SectionDivider(ws, r++, "أبرز المبادرات", width: 3);
        HeaderRow(ws, r, "المبادرة", "عدد التعهدات", "الرسم");
        int iStart = r + 1;
        r++;
        if (m.Contributions.TopInitiatives.Count == 0) { ws.Cell(r++, 1).Value = "—"; }
        foreach (var i in m.Contributions.TopInitiatives)
        {
            ws.Cell(r, 1).Value = i.Name;
            ws.Cell(r, 2).Value = i.Count;
            ws.Cell(r, 3).Value = i.Count;
            ws.Cell(r, 3).Style.Font.FontColor = White;
            ws.Cell(r, 1).Style.Alignment.WrapText = true;
            r++;
        }
        int iEnd = r - 1;
        if (iEnd >= iStart)
        {
            StripeBand(ws, iStart, iEnd, 3);
            DataBarColumn(ws.Range(iStart, 3, iEnd, 3), Green);
        }

        Finish(ws);
        ws.Column(1).Width = 45; ws.Column(3).Width = 24;
    }

    private void BuildSignatures(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "التواقيع");
        BrandHeader(ws, 1, "تواقيع الفرق وتعليقاتها",
            $"إجمالي التواقيع: {m.GroupSignatures.TotalCount}", width: 3);

        int r = 5;
        HeaderRow(ws, r++, "الإدارة", "التعليق", "التاريخ");
        int dataStart = r;
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
                ws.Cell(r, 2).Style.Alignment.WrapText = true;
                r++;
            }
            StripeBand(ws, dataStart, r - 1, 3);
        }
        Finish(ws);
        ws.Column(2).Width = 50;
    }

    private void BuildAlignment(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "الاتساق الاستراتيجي");
        BrandHeader(ws, 1, "توزيع المساهمات على الركائز الاستراتيجية",
            $"إجمالي المساهمات المرتبطة بالركائز: {m.LeadershipAlignment.TotalContributions}", width: 4);

        int r = 5;
        HeaderRow(ws, r, "الركيزة", "عدد المساهمات", "النسبة %", "الرسم");
        int dataStart = r + 1;
        r++;
        if (m.LeadershipAlignment.PillarShares.Count == 0) { ws.Cell(r++, 1).Value = "—"; }
        foreach (var ps in m.LeadershipAlignment.PillarShares)
        {
            ws.Cell(r, 1).Value = ps.PillarName;
            ws.Cell(r, 2).Value = ps.Count;
            ws.Cell(r, 3).Value = ps.Percent;
            ws.Cell(r, 4).Value = ps.Count;
            ws.Cell(r, 4).Style.Font.FontColor = White;
            ws.Cell(r, 1).Style.Alignment.WrapText = true;
            r++;
        }
        int dataEnd = r - 1;
        if (dataEnd >= dataStart)
        {
            StripeBand(ws, dataStart, dataEnd, 4);
            DataBarColumn(ws.Range(dataStart, 4, dataEnd, 4), Green);
            ws.Range(dataStart, 3, dataEnd, 3).Style.NumberFormat.Format = "0.0";
        }

        if (m.LeadershipAlignment.Gaps.Count > 0)
        {
            r++;
            SectionDivider(ws, r++, "فجوات الاتساق", width: 4);
            foreach (var g in m.LeadershipAlignment.Gaps)
            {
                ws.Cell(r, 1).Value = "⚠ " + g;
                ws.Range(r, 1, r, 4).Merge();
                ws.Cell(r, 1).Style.Alignment.WrapText = true;
                ws.Row(r).Height = 22;
                r++;
            }
        }
        Finish(ws);
        ws.Column(1).Width = 38; ws.Column(4).Width = 24;
    }

    private void BuildCulture(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "الثقافة والمشاركة");
        var cu = m.LeadershipCulture;
        BrandHeader(ws, 1, "الثقافة والمشاركة",
            $"مؤشر روح الفريق: {cu.TeamSpiritScore:0.#}/100 ({cu.TeamSpiritLabel})", width: 3);

        // KPI strip — comments breakdown
        KpiStrip(ws, 5, new List<(string, string)>
        {
            (cu.PositiveComments.ToString(), "تعليقات إيجابية"),
            (cu.NeutralComments.ToString(), "تعليقات محايدة"),
            (cu.NegativeComments.ToString(), "تعليقات سلبية"),
        }, totalCols: 3);

        int r = 9;
        SectionDivider(ws, r++, "المشاركة حسب الإدارة", width: 3);
        HeaderRow(ws, r, "الإدارة", "الحضور", "الرسم");
        int dataStart = r + 1;
        r++;
        if (cu.DepartmentParticipation.Count == 0) { ws.Cell(r++, 1).Value = "—"; }
        foreach (var d in cu.DepartmentParticipation)
        {
            ws.Cell(r, 1).Value = d.DeptName;
            ws.Cell(r, 2).Value = d.Attendees;
            ws.Cell(r, 3).Value = d.Attendees;
            ws.Cell(r, 3).Style.Font.FontColor = White;
            r++;
        }
        int dataEnd = r - 1;
        if (dataEnd >= dataStart)
        {
            StripeBand(ws, dataStart, dataEnd, 3);
            DataBarColumn(ws.Range(dataStart, 3, dataEnd, 3), Blue);
        }
        Finish(ws);
        ws.Column(1).Width = 30; ws.Column(3).Width = 28;
    }

    private void BuildRisks(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "المخاطر والفرص");
        BrandHeader(ws, 1, "المخاطر والفرص", width: 4);

        int r = 5;
        SectionDivider(ws, r++, "أبرز التحديات (س4)", width: 4);
        HeaderRow(ws, r, "التحدي", "العدد", "النسبة %", "الرسم");
        int cStart = r + 1;
        r++;
        if (m.LeadershipRisks.TopChallenges.Count == 0) { ws.Cell(r++, 1).Value = "—"; }
        foreach (var c in m.LeadershipRisks.TopChallenges)
        {
            ws.Cell(r, 1).Value = c.Category;
            ws.Cell(r, 2).Value = c.Count;
            ws.Cell(r, 3).Value = c.Percent;
            ws.Cell(r, 4).Value = c.Count;
            ws.Cell(r, 4).Style.Font.FontColor = White;
            ws.Cell(r, 1).Style.Alignment.WrapText = true;
            r++;
        }
        int cEnd = r - 1;
        if (cEnd >= cStart)
        {
            StripeBand(ws, cStart, cEnd, 4);
            DataBarColumn(ws.Range(cStart, 4, cEnd, 4), XLColor.FromHtml("#A13544"));
            ws.Range(cStart, 3, cEnd, 3).Style.NumberFormat.Format = "0.0";
        }

        r++;
        SectionDivider(ws, r++, "أبرز الفرص (س7)", width: 4);
        HeaderRow(ws, r, "الفرصة", "العدد", "النسبة %", "الرسم");
        int oStart = r + 1;
        r++;
        if (m.LeadershipRisks.TopOpportunities.Count == 0) { ws.Cell(r++, 1).Value = "—"; }
        foreach (var o in m.LeadershipRisks.TopOpportunities)
        {
            ws.Cell(r, 1).Value = o.Category;
            ws.Cell(r, 2).Value = o.Count;
            ws.Cell(r, 3).Value = o.Percent;
            ws.Cell(r, 4).Value = o.Count;
            ws.Cell(r, 4).Style.Font.FontColor = White;
            ws.Cell(r, 1).Style.Alignment.WrapText = true;
            r++;
        }
        int oEnd = r - 1;
        if (oEnd >= oStart)
        {
            StripeBand(ws, oStart, oEnd, 4);
            DataBarColumn(ws.Range(oStart, 4, oEnd, 4), Green);
            ws.Range(oStart, 3, oEnd, 3).Style.NumberFormat.Format = "0.0";
        }
        Finish(ws);
        ws.Column(1).Width = 40; ws.Column(4).Width = 24;
    }

    private void BuildMaturity(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "النضج التنظيمي");
        var ma = m.LeadershipMaturity;
        BrandHeader(ws, 1, "النضج التنظيمي حسب الإدارة", width: 4);

        KpiStrip(ws, 5, new List<(string, string)>
        {
            (ma.MatureCount.ToString(), "ناضجة"),
            (ma.DevelopingCount.ToString(), "متطورة"),
            (ma.NeedsSupportCount.ToString(), "بحاجة دعم"),
        }, totalCols: 4);

        int r = 9;
        HeaderRow(ws, r, "الإدارة", "المؤشر (من 5)", "التصنيف", "الرسم");
        int dataStart = r + 1;
        r++;
        if (ma.Departments.Count == 0) { ws.Cell(r++, 1).Value = "—"; }
        foreach (var d in ma.Departments)
        {
            ws.Cell(r, 1).Value = d.DeptName;
            ws.Cell(r, 2).Value = Math.Round(d.Score, 2);
            ws.Cell(r, 3).Value = d.Tier;
            ws.Cell(r, 4).Value = Math.Round(d.Score, 2);
            ws.Cell(r, 4).Style.Font.FontColor = White;
            r++;
        }
        int dataEnd = r - 1;
        if (dataEnd >= dataStart)
        {
            StripeBand(ws, dataStart, dataEnd, 4);
            DataBarColumn(ws.Range(dataStart, 4, dataEnd, 4), Cyan);
            ws.Range(dataStart, 2, dataEnd, 2).Style.NumberFormat.Format = "0.00";
            ws.Range(dataStart, 2, dataEnd, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        Finish(ws);
        ws.Column(1).Width = 30; ws.Column(4).Width = 24;
    }

    private void BuildRecommendations(XLWorkbook wb, ExecutiveReportViewModel m)
    {
        var ws = NewSheet(wb, "توصيات القيادة");
        BrandHeader(ws, 1, "توصيات القيادة", width: 2);
        int r = 5;
        HeaderRow(ws, r++, "#", "التوصية");
        int dataStart = r;
        if (m.LeadershipRecommendations.Count == 0)
        {
            ws.Cell(r++, 2).Value = "لا توجد توصيات كافية بعد.";
        }
        int n = 1;
        foreach (var rec in m.LeadershipRecommendations)
        {
            ws.Cell(r, 1).Value = n++;
            ws.Cell(r, 2).Value = rec;
            ws.Cell(r, 2).Style.Alignment.WrapText = true;
            ws.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(r).Height = 30;
            r++;
        }
        if (r - 1 >= dataStart)
            StripeBand(ws, dataStart, r - 1, 2);
        Finish(ws);
        ws.Column(1).Width = 6; ws.Column(2).Width = 80;
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
