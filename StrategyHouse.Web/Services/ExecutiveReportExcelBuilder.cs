using ClosedXML.Excel;
using StrategyHouse.Web.Models;
using static StrategyHouse.Web.Services.XlsxReportStyle;

namespace StrategyHouse.Web.Services;

// Phase 20.24.3 — branded .xlsx export of the comprehensive executive report.
// Replaces in-cell data bars (a workaround) with NATIVE Excel charts bound to
// cell ranges. Editing values in the workbook re-draws the chart instantly,
// so no human interaction is required to refresh visuals.
public class ExecutiveReportExcelBuilder
{
    public byte[] Build(ExecutiveReportViewModel m)
    {
        var charts = new List<XlsxChartBuilder.ChartRequest>();
        byte[] raw;
        using (var wb = new XLWorkbook())
        {
            wb.Style.Font.FontName = "Cairo";
            var s = m.Sections;

            if (s.Has(ExecReportSections.Overview)) BuildOverview(wb, m, charts);
            if (s.Has(ExecReportSections.Departments)) BuildDepartments(wb, m, charts);
            if (s.Has(ExecReportSections.Quiz)) BuildQuiz(wb, m, charts);
            if (s.Has(ExecReportSections.Survey)) BuildSurvey(wb, m);
            if (s.Has(ExecReportSections.Contributions)) BuildContributions(wb, m, charts);
            if (s.Has(ExecReportSections.Signatures)) BuildSignatures(wb, m);
            if (s.Has(ExecReportSections.LeadershipAlignment)) BuildAlignment(wb, m, charts);
            if (s.Has(ExecReportSections.LeadershipCulture)) BuildCulture(wb, m, charts);
            if (s.Has(ExecReportSections.LeadershipRisks)) BuildRisks(wb, m, charts);
            if (s.Has(ExecReportSections.LeadershipMaturity)) BuildMaturity(wb, m, charts);
            if (s.Has(ExecReportSections.LeadershipRecommendations)) BuildRecommendations(wb, m);

            if (!wb.Worksheets.Any())
                NewSheet(wb, "التقرير").Cell(1, 1).SetValue("لم يتم اختيار أي قسم.");

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            raw = ms.ToArray();
        }

        return XlsxChartBuilder.InjectCharts(raw, charts);
    }

    private void BuildOverview(XLWorkbook wb, ExecutiveReportViewModel m,
        List<XlsxChartBuilder.ChartRequest> charts)
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

    private void BuildDepartments(XLWorkbook wb, ExecutiveReportViewModel m,
        List<XlsxChartBuilder.ChartRequest> charts)
    {
        var ws = NewSheet(wb, "الإدارات");
        BrandHeader(ws, 1, "الحضور والإكمال حسب الإدارة",
            "الترتيب بعدد الحضور — رسم بياني يتحدث تلقائياً", width: 8);

        int r = 5;
        HeaderRow(ws, r, "الترتيب", "الإدارة", "الجلسات", "الحضور", "نسبة الإكمال %");
        int dataStart = r + 1;
        r++;
        foreach (var d in m.DepartmentBreakdown)
        {
            ws.Cell(r, 1).Value = d.Rank;
            ws.Cell(r, 2).Value = d.DeptName;
            ws.Cell(r, 3).Value = d.SessionsCount;
            ws.Cell(r, 4).Value = d.AttendeesCount;
            ws.Cell(r, 5).Value = d.CompletionRate;
            r++;
        }
        int dataEnd = r - 1;
        if (m.DepartmentBreakdown.Count > 0)
        {
            StripeBand(ws, dataStart, dataEnd, 5);
            ws.Range(dataStart, 1, dataEnd, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(dataStart, 3, dataEnd, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(dataStart, 5, dataEnd, 5).Style.NumberFormat.Format = "0.0";

            // Horizontal bar chart works best for many long Arabic department names.
            charts.Add(new XlsxChartBuilder.ChartRequest(
                SheetName: "الإدارات",
                Kind: XlsxChartBuilder.ChartKind.Bar,
                Title: "الحضور حسب الإدارة",
                CategoryRange: $"B{dataStart}:B{dataEnd}",
                ValueRange: $"D{dataStart}:D{dataEnd}",
                SeriesName: "الحضور",
                FromCol: 6, FromRow: dataStart - 2,
                ToCol: 14, ToRow: dataStart - 2 + Math.Max(16, (dataEnd - dataStart + 1) + 4)));

            ws.Cell(r, 1).Value = "";
            ws.Cell(r, 2).Value = "الإجمالي";
            ws.Cell(r, 3).Value = m.DepartmentBreakdown.Sum(d => d.SessionsCount);
            ws.Cell(r, 4).Value = m.DepartmentBreakdown.Sum(d => d.AttendeesCount);
            ws.Cell(r, 5).Value = "—";
            StyleTotal(ws.Range(r, 1, r, 5));
        }
        Finish(ws);
        ws.Column(2).Width = 30;
        for (int c = 6; c <= 14; c++) ws.Column(c).Width = 12;
    }

    private void BuildQuiz(XLWorkbook wb, ExecutiveReportViewModel m,
        List<XlsxChartBuilder.ChartRequest> charts)
    {
        var ws = NewSheet(wb, "الاختبار");
        var qa = m.QuizAnalytics;
        BrandHeader(ws, 1, "تحليلات الاختبار",
            $"إجمالي المحاولات: {qa.TotalAttempts} · المتوسط: {qa.AvgScore:0.##} / 5", width: 7);

        KpiStrip(ws, 5, new List<(string, string)>
        {
            (qa.TotalAttempts.ToString(), "إجمالي المحاولات"),
            ($"{qa.AvgScore:0.##}/5", "المتوسط"),
            (qa.Bucket5.ToString(), "ممتاز (5)"),
            (qa.Bucket0to2.ToString(), "منخفض (0-2)"),
        }, totalCols: 4);

        int r = 9;
        SectionDivider(ws, r++, "توزيع النتائج", width: 7);
        HeaderRow(ws, r, "فئة النتيجة", "عدد المحاولات", "النسبة %");
        int dataStart = r + 1;
        r++;
        int total = Math.Max(1, qa.Bucket0to2 + qa.Bucket3to4 + qa.Bucket5);
        AddDist(ws, r++, "منخفض (0-2)", qa.Bucket0to2, total);
        AddDist(ws, r++, "متوسط (3-4)", qa.Bucket3to4, total);
        AddDist(ws, r++, "ممتاز (5)", qa.Bucket5, total);
        int dataEnd = r - 1;
        StripeBand(ws, dataStart, dataEnd, 3);
        ws.Range(dataStart, 2, dataEnd, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(dataStart, 3, dataEnd, 3).Style.NumberFormat.Format = "0.0";

        charts.Add(new XlsxChartBuilder.ChartRequest(
            SheetName: "الاختبار",
            Kind: XlsxChartBuilder.ChartKind.Column,
            Title: "توزيع نتائج الاختبار",
            CategoryRange: $"A{dataStart}:A{dataEnd}",
            ValueRange: $"B{dataStart}:B{dataEnd}",
            SeriesName: "المحاولات",
            FromCol: 4, FromRow: dataStart - 2,
            ToCol: 11, ToRow: dataStart - 2 + 13));

        TotalRow(ws, r++, "الإجمالي", total);
        r = Math.Max(r, dataStart - 2 + 13) + 1;

        // Top missed
        r++;
        SectionDivider(ws, r++, "أكثر الأسئلة صعوبة", width: 7);
        HeaderRow(ws, r, "السؤال", "نسبة الخطأ %", "عدد المحاولات");
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
                r++;
            }
            int mEnd = r - 1;
            StripeBand(ws, mStart, mEnd, 3);
            ws.Range(mStart, 2, mEnd, 2).Style.NumberFormat.Format = "0.0";
            ws.Range(mStart, 1, mEnd, 1).Style.Alignment.WrapText = true;

            charts.Add(new XlsxChartBuilder.ChartRequest(
                SheetName: "الاختبار",
                Kind: XlsxChartBuilder.ChartKind.Bar,
                Title: "نسب الخطأ — أكثر الأسئلة صعوبة",
                CategoryRange: $"A{mStart}:A{mEnd}",
                ValueRange: $"B{mStart}:B{mEnd}",
                SeriesName: "نسبة الخطأ %",
                FromCol: 4, FromRow: mStart - 2,
                ToCol: 11, ToRow: mStart - 2 + 13));
            r = Math.Max(r, mStart - 2 + 13) + 1;
        }

        // Strongest
        if (qa.Top3Strongest.Count > 0)
        {
            r++;
            SectionDivider(ws, r++, "نقاط القوة المعرفية", width: 7);
            HeaderRow(ws, r, "السؤال", "نسبة الصواب %", "عدد المحاولات");
            int sStart = r + 1;
            r++;
            foreach (var q in qa.Top3Strongest)
            {
                double correct = 100 - q.MissRate;
                ws.Cell(r, 1).Value = q.QuestionAr;
                ws.Cell(r, 2).Value = correct;
                ws.Cell(r, 3).Value = q.Attempts;
                r++;
            }
            int sEnd = r - 1;
            StripeBand(ws, sStart, sEnd, 3);
            ws.Range(sStart, 2, sEnd, 2).Style.NumberFormat.Format = "0.0";
            ws.Range(sStart, 1, sEnd, 1).Style.Alignment.WrapText = true;

            charts.Add(new XlsxChartBuilder.ChartRequest(
                SheetName: "الاختبار",
                Kind: XlsxChartBuilder.ChartKind.Bar,
                Title: "نسب الصواب — نقاط القوة",
                CategoryRange: $"A{sStart}:A{sEnd}",
                ValueRange: $"B{sStart}:B{sEnd}",
                SeriesName: "نسبة الصواب %",
                FromCol: 4, FromRow: sStart - 2,
                ToCol: 11, ToRow: sStart - 2 + 13));
        }

        Finish(ws);
        ws.Column(1).Width = 40;
        for (int c = 4; c <= 11; c++) ws.Column(c).Width = 12;
    }

    private static void AddDist(IXLWorksheet ws, int row, string label, int count, int total)
    {
        ws.Cell(row, 1).Value = label;
        ws.Cell(row, 2).Value = count;
        ws.Cell(row, 3).Value = total > 0 ? Math.Round(100.0 * count / total, 1) : 0;
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

    private void BuildContributions(XLWorkbook wb, ExecutiveReportViewModel m,
        List<XlsxChartBuilder.ChartRequest> charts)
    {
        var ws = NewSheet(wb, "المساهمات");
        BrandHeader(ws, 1, "المساهمات الأبرز",
            $"إجمالي التعهدات: {m.Contributions.TotalPledges}", width: 7);

        int r = 5;
        SectionDivider(ws, r++, "أبرز الأهداف", width: 7);
        HeaderRow(ws, r, "الهدف", "عدد التعهدات");
        int oStart = r + 1;
        r++;
        if (m.Contributions.TopObjectives.Count == 0) { ws.Cell(r++, 1).Value = "—"; }
        foreach (var o in m.Contributions.TopObjectives)
        {
            ws.Cell(r, 1).Value = o.Name;
            ws.Cell(r, 2).Value = o.Count;
            ws.Cell(r, 1).Style.Alignment.WrapText = true;
            r++;
        }
        int oEnd = r - 1;
        if (oEnd >= oStart && m.Contributions.TopObjectives.Count > 0)
        {
            StripeBand(ws, oStart, oEnd, 2);
            charts.Add(new XlsxChartBuilder.ChartRequest(
                SheetName: "المساهمات",
                Kind: XlsxChartBuilder.ChartKind.Bar,
                Title: "أبرز الأهداف",
                CategoryRange: $"A{oStart}:A{oEnd}",
                ValueRange: $"B{oStart}:B{oEnd}",
                SeriesName: "التعهدات",
                FromCol: 3, FromRow: oStart - 2,
                ToCol: 10, ToRow: oStart - 2 + 13));
            r = Math.Max(r, oStart - 2 + 13) + 1;
        }

        r++;
        SectionDivider(ws, r++, "أبرز المبادرات", width: 7);
        HeaderRow(ws, r, "المبادرة", "عدد التعهدات");
        int iStart = r + 1;
        r++;
        if (m.Contributions.TopInitiatives.Count == 0) { ws.Cell(r++, 1).Value = "—"; }
        foreach (var i in m.Contributions.TopInitiatives)
        {
            ws.Cell(r, 1).Value = i.Name;
            ws.Cell(r, 2).Value = i.Count;
            ws.Cell(r, 1).Style.Alignment.WrapText = true;
            r++;
        }
        int iEnd = r - 1;
        if (iEnd >= iStart && m.Contributions.TopInitiatives.Count > 0)
        {
            StripeBand(ws, iStart, iEnd, 2);
            charts.Add(new XlsxChartBuilder.ChartRequest(
                SheetName: "المساهمات",
                Kind: XlsxChartBuilder.ChartKind.Bar,
                Title: "أبرز المبادرات",
                CategoryRange: $"A{iStart}:A{iEnd}",
                ValueRange: $"B{iStart}:B{iEnd}",
                SeriesName: "التعهدات",
                FromCol: 3, FromRow: iStart - 2,
                ToCol: 10, ToRow: iStart - 2 + 13));
        }

        Finish(ws);
        ws.Column(1).Width = 45;
        for (int c = 3; c <= 10; c++) ws.Column(c).Width = 12;
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

    private void BuildAlignment(XLWorkbook wb, ExecutiveReportViewModel m,
        List<XlsxChartBuilder.ChartRequest> charts)
    {
        var ws = NewSheet(wb, "الاتساق الاستراتيجي");
        BrandHeader(ws, 1, "توزيع المساهمات على الركائز الاستراتيجية",
            $"إجمالي المساهمات المرتبطة بالركائز: {m.LeadershipAlignment.TotalContributions}", width: 7);

        int r = 5;
        HeaderRow(ws, r, "الركيزة", "عدد المساهمات", "النسبة %");
        int dataStart = r + 1;
        r++;
        if (m.LeadershipAlignment.PillarShares.Count == 0) { ws.Cell(r++, 1).Value = "—"; }
        foreach (var ps in m.LeadershipAlignment.PillarShares)
        {
            ws.Cell(r, 1).Value = ps.PillarName;
            ws.Cell(r, 2).Value = ps.Count;
            ws.Cell(r, 3).Value = ps.Percent;
            ws.Cell(r, 1).Style.Alignment.WrapText = true;
            r++;
        }
        int dataEnd = r - 1;
        if (dataEnd >= dataStart && m.LeadershipAlignment.PillarShares.Count > 0)
        {
            StripeBand(ws, dataStart, dataEnd, 3);
            ws.Range(dataStart, 3, dataEnd, 3).Style.NumberFormat.Format = "0.0";

            // Pie chart — strategic alignment is naturally part-of-whole.
            charts.Add(new XlsxChartBuilder.ChartRequest(
                SheetName: "الاتساق الاستراتيجي",
                Kind: XlsxChartBuilder.ChartKind.Pie,
                Title: "توزيع المساهمات على الركائز",
                CategoryRange: $"A{dataStart}:A{dataEnd}",
                ValueRange: $"B{dataStart}:B{dataEnd}",
                SeriesName: "المساهمات",
                FromCol: 4, FromRow: dataStart - 2,
                ToCol: 11, ToRow: dataStart - 2 + 16));
            r = Math.Max(r, dataStart - 2 + 16) + 1;
        }

        if (m.LeadershipAlignment.Gaps.Count > 0)
        {
            r++;
            SectionDivider(ws, r++, "فجوات الاتساق", width: 7);
            foreach (var g in m.LeadershipAlignment.Gaps)
            {
                ws.Cell(r, 1).Value = "⚠ " + g;
                ws.Range(r, 1, r, 7).Merge();
                ws.Cell(r, 1).Style.Alignment.WrapText = true;
                ws.Row(r).Height = 22;
                r++;
            }
        }
        Finish(ws);
        ws.Column(1).Width = 38;
        for (int c = 4; c <= 11; c++) ws.Column(c).Width = 12;
    }

    private void BuildCulture(XLWorkbook wb, ExecutiveReportViewModel m,
        List<XlsxChartBuilder.ChartRequest> charts)
    {
        var ws = NewSheet(wb, "الثقافة والمشاركة");
        var cu = m.LeadershipCulture;
        BrandHeader(ws, 1, "الثقافة والمشاركة",
            $"مؤشر روح الفريق: {cu.TeamSpiritScore:0.#}/100 ({cu.TeamSpiritLabel})", width: 7);

        KpiStrip(ws, 5, new List<(string, string)>
        {
            (cu.PositiveComments.ToString(), "تعليقات إيجابية"),
            (cu.NeutralComments.ToString(), "تعليقات محايدة"),
            (cu.NegativeComments.ToString(), "تعليقات سلبية"),
        }, totalCols: 3);

        int r = 9;
        SectionDivider(ws, r++, "المشاركة حسب الإدارة", width: 7);
        HeaderRow(ws, r, "الإدارة", "الحضور");
        int dataStart = r + 1;
        r++;
        if (cu.DepartmentParticipation.Count == 0) { ws.Cell(r++, 1).Value = "—"; }
        foreach (var d in cu.DepartmentParticipation)
        {
            ws.Cell(r, 1).Value = d.DeptName;
            ws.Cell(r, 2).Value = d.Attendees;
            r++;
        }
        int dataEnd = r - 1;
        if (dataEnd >= dataStart && cu.DepartmentParticipation.Count > 0)
        {
            StripeBand(ws, dataStart, dataEnd, 2);
            charts.Add(new XlsxChartBuilder.ChartRequest(
                SheetName: "الثقافة والمشاركة",
                Kind: XlsxChartBuilder.ChartKind.Bar,
                Title: "المشاركة حسب الإدارة",
                CategoryRange: $"A{dataStart}:A{dataEnd}",
                ValueRange: $"B{dataStart}:B{dataEnd}",
                SeriesName: "الحضور",
                FromCol: 3, FromRow: dataStart - 2,
                ToCol: 11, ToRow: dataStart - 2 + Math.Max(15, (dataEnd - dataStart + 1) + 4)));
        }
        Finish(ws);
        ws.Column(1).Width = 30;
        for (int c = 3; c <= 11; c++) ws.Column(c).Width = 12;
    }

    private void BuildRisks(XLWorkbook wb, ExecutiveReportViewModel m,
        List<XlsxChartBuilder.ChartRequest> charts)
    {
        var ws = NewSheet(wb, "المخاطر والفرص");
        BrandHeader(ws, 1, "المخاطر والفرص", width: 7);

        int r = 5;
        SectionDivider(ws, r++, "أبرز التحديات (س4)", width: 7);
        HeaderRow(ws, r, "التحدي", "العدد", "النسبة %");
        int cStart = r + 1;
        r++;
        if (m.LeadershipRisks.TopChallenges.Count == 0) { ws.Cell(r++, 1).Value = "—"; }
        foreach (var c in m.LeadershipRisks.TopChallenges)
        {
            ws.Cell(r, 1).Value = c.Category;
            ws.Cell(r, 2).Value = c.Count;
            ws.Cell(r, 3).Value = c.Percent;
            ws.Cell(r, 1).Style.Alignment.WrapText = true;
            r++;
        }
        int cEnd = r - 1;
        if (cEnd >= cStart && m.LeadershipRisks.TopChallenges.Count > 0)
        {
            StripeBand(ws, cStart, cEnd, 3);
            ws.Range(cStart, 3, cEnd, 3).Style.NumberFormat.Format = "0.0";
            charts.Add(new XlsxChartBuilder.ChartRequest(
                SheetName: "المخاطر والفرص",
                Kind: XlsxChartBuilder.ChartKind.Bar,
                Title: "أبرز التحديات",
                CategoryRange: $"A{cStart}:A{cEnd}",
                ValueRange: $"B{cStart}:B{cEnd}",
                SeriesName: "العدد",
                FromCol: 4, FromRow: cStart - 2,
                ToCol: 11, ToRow: cStart - 2 + 14));
            r = Math.Max(r, cStart - 2 + 14) + 1;
        }

        r++;
        SectionDivider(ws, r++, "أبرز الفرص (س7)", width: 7);
        HeaderRow(ws, r, "الفرصة", "العدد", "النسبة %");
        int oStart = r + 1;
        r++;
        if (m.LeadershipRisks.TopOpportunities.Count == 0) { ws.Cell(r++, 1).Value = "—"; }
        foreach (var o in m.LeadershipRisks.TopOpportunities)
        {
            ws.Cell(r, 1).Value = o.Category;
            ws.Cell(r, 2).Value = o.Count;
            ws.Cell(r, 3).Value = o.Percent;
            ws.Cell(r, 1).Style.Alignment.WrapText = true;
            r++;
        }
        int oEnd = r - 1;
        if (oEnd >= oStart && m.LeadershipRisks.TopOpportunities.Count > 0)
        {
            StripeBand(ws, oStart, oEnd, 3);
            ws.Range(oStart, 3, oEnd, 3).Style.NumberFormat.Format = "0.0";
            charts.Add(new XlsxChartBuilder.ChartRequest(
                SheetName: "المخاطر والفرص",
                Kind: XlsxChartBuilder.ChartKind.Bar,
                Title: "أبرز الفرص",
                CategoryRange: $"A{oStart}:A{oEnd}",
                ValueRange: $"B{oStart}:B{oEnd}",
                SeriesName: "العدد",
                FromCol: 4, FromRow: oStart - 2,
                ToCol: 11, ToRow: oStart - 2 + 14));
        }
        Finish(ws);
        ws.Column(1).Width = 40;
        for (int c = 4; c <= 11; c++) ws.Column(c).Width = 12;
    }

    private void BuildMaturity(XLWorkbook wb, ExecutiveReportViewModel m,
        List<XlsxChartBuilder.ChartRequest> charts)
    {
        var ws = NewSheet(wb, "النضج التنظيمي");
        var ma = m.LeadershipMaturity;
        BrandHeader(ws, 1, "النضج التنظيمي حسب الإدارة", width: 7);

        KpiStrip(ws, 5, new List<(string, string)>
        {
            (ma.MatureCount.ToString(), "ناضجة"),
            (ma.DevelopingCount.ToString(), "متطورة"),
            (ma.NeedsSupportCount.ToString(), "بحاجة دعم"),
        }, totalCols: 4);

        int r = 9;
        HeaderRow(ws, r, "الإدارة", "المؤشر (من 5)", "التصنيف");
        int dataStart = r + 1;
        r++;
        if (ma.Departments.Count == 0) { ws.Cell(r++, 1).Value = "—"; }
        foreach (var d in ma.Departments)
        {
            ws.Cell(r, 1).Value = d.DeptName;
            ws.Cell(r, 2).Value = Math.Round(d.Score, 2);
            ws.Cell(r, 3).Value = d.Tier;
            r++;
        }
        int dataEnd = r - 1;
        if (dataEnd >= dataStart && ma.Departments.Count > 0)
        {
            StripeBand(ws, dataStart, dataEnd, 3);
            ws.Range(dataStart, 2, dataEnd, 2).Style.NumberFormat.Format = "0.00";
            ws.Range(dataStart, 2, dataEnd, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            charts.Add(new XlsxChartBuilder.ChartRequest(
                SheetName: "النضج التنظيمي",
                Kind: XlsxChartBuilder.ChartKind.Bar,
                Title: "النضج التنظيمي (من 5)",
                CategoryRange: $"A{dataStart}:A{dataEnd}",
                ValueRange: $"B{dataStart}:B{dataEnd}",
                SeriesName: "المؤشر",
                FromCol: 4, FromRow: dataStart - 2,
                ToCol: 11, ToRow: dataStart - 2 + Math.Max(15, (dataEnd - dataStart + 1) + 4)));
        }
        Finish(ws);
        ws.Column(1).Width = 30;
        for (int c = 4; c <= 11; c++) ws.Column(c).Width = 12;
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
