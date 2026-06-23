using ClosedXML.Excel;
using StrategyHouse.Domain.Enums;
using StrategyHouse.Web.Models;
using static StrategyHouse.Web.Services.XlsxReportStyle;

namespace StrategyHouse.Web.Services;

// Phase 20.24.3 — branded .xlsx export of the official survey final report.
// Major change vs 20.24: in-cell "data bar" conditional formatting was a
// fragile substitute for an actual chart. We now emit NATIVE Excel charts
// (column for distributions, bar for choice frequency) bound to cell ranges
// via DocumentFormat.OpenXml. Editing values in the table auto-redraws the
// chart — no human interaction needed.
//
// Phase 20.25 — official GAC brand typeface (Frutiger LT Arabic 55 Roman)
// applied globally; Cairo/Calibri/Arial provide OS-side fallback.
public class SurveyReportExcelBuilder
{
    public byte[] Build(FinalReportViewModel m)
    {
        var chartRequests = new List<XlsxChartBuilder.ChartRequest>();

        byte[] raw;
        using (var wb = new XLWorkbook())
        {
            wb.Style.Font.FontName = BrandFonts.Regular;
            BuildSummary(wb, m, chartRequests);
            BuildLikert(wb, m, chartRequests);
            BuildChoice(wb, m, chartRequests);
            BuildOpenText(wb, m, chartRequests);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            raw = ms.ToArray();
        }

        return XlsxChartBuilder.InjectCharts(raw, chartRequests);
    }

    private void BuildSummary(XLWorkbook wb, FinalReportViewModel m,
        List<XlsxChartBuilder.ChartRequest> charts)
    {
        var ws = NewSheet(wb, "ملخص");

        var period = m.DateFrom != null ? $"{m.DateFrom:yyyy-MM-dd} ← {m.DateTo:yyyy-MM-dd}" : "—";
        BrandHeader(ws, 1, "التقرير النهائي للاستبيان الرسمي", $"{m.SurveyTitle} · فترة الجمع: {period}");

        var q1 = m.Cards.FirstOrDefault(c => c.Order == 1)?.Likert;
        var q8 = m.Cards.FirstOrDefault(c => c.Order == 8)?.Likert;
        var kpis = new List<(string, string)>
        {
            (m.TotalResponses.ToString(), "إجمالي الردود"),
            (m.Cards.Count.ToString(), "عدد الأسئلة"),
            (q1 is { Total: > 0 } ? $"{q1.Mean:0.##}/5" : "—", "وضوح الاستراتيجية (س1)"),
        };
        if (q8 is { Total: > 0 })
            kpis.Add(($"{q8.Mean:0.##}/5", "القدرة على المساهمة (س8)"));
        KpiStrip(ws, 5, kpis, startCol: 1, totalCols: 6);

        int r = 9;

        // --- Likert means summary chart (live-bound to a small table) ---
        var likerts = m.Cards.Where(x => x.Type == QuestionType.Likert5 && x.Likert is { Total: > 0 })
                              .OrderBy(x => x.Order).ToList();
        if (likerts.Count > 0)
        {
            SectionDivider(ws, r++, "متوسطات أسئلة ليكرت (تتحدث تلقائياً)", width: 6);
            HeaderRow(ws, r++, "السؤال", "المتوسط (من 5)");
            int dataStart = r;
            foreach (var c in likerts)
            {
                ws.Cell(r, 1).Value = $"س{c.Order}";
                ws.Cell(r, 2).Value = Math.Round(c.Likert!.Mean, 2);
                r++;
            }
            int dataEnd = r - 1;
            StripeBand(ws, dataStart, dataEnd, 2);
            ws.Range(dataStart, 2, dataEnd, 2).Style.NumberFormat.Format = "0.00";
            ws.Range(dataStart, 2, dataEnd, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Native column chart bound to A:B of those rows.
            charts.Add(new XlsxChartBuilder.ChartRequest(
                SheetName: "ملخص",
                Kind: XlsxChartBuilder.ChartKind.Column,
                Title: "متوسطات أسئلة ليكرت",
                CategoryRange: $"A{dataStart}:A{dataEnd}",
                ValueRange: $"B{dataStart}:B{dataEnd}",
                SeriesName: "المتوسط",
                FromCol: 3, FromRow: dataStart - 1,
                ToCol: 7, ToRow: dataStart - 1 + Math.Max(10, dataEnd - dataStart + 6)));
            r = Math.Max(r, dataStart - 1 + Math.Max(10, dataEnd - dataStart + 6)) + 2;
        }

        SectionDivider(ws, r++, "أبرز النتائج", width: 6);
        if (m.Takeaways.Count == 0) ws.Cell(r++, 1).Value = "—";
        foreach (var t in m.Takeaways)
        {
            ws.Cell(r, 1).Value = "• " + t;
            ws.Range(r, 1, r, 6).Merge();
            ws.Range(r, 1, r, 6).Style.Alignment.WrapText = true;
            ws.Row(r).Height = 22;
            r++;
        }

        if (m.Insights.Count > 0)
        {
            r++;
            SectionDivider(ws, r++, "رؤى شاملة", width: 6);
            foreach (var ins in m.Insights)
            {
                ws.Cell(r, 1).Value = "• " + ins;
                ws.Range(r, 1, r, 6).Merge();
                ws.Range(r, 1, r, 6).Style.Alignment.WrapText = true;
                ws.Row(r).Height = 22;
                r++;
            }
        }

        Finish(ws);
        for (int c = 1; c <= 7; c++) ws.Column(c).Width = 18;
    }

    private void BuildLikert(XLWorkbook wb, FinalReportViewModel m,
        List<XlsxChartBuilder.ChartRequest> charts)
    {
        var ws = NewSheet(wb, "ليكرت");
        BrandHeader(ws, 1, "أسئلة مقياس ليكرت (س1 و س8)",
            "توزيع الإجابات على درجات 1 إلى 5 — كل سؤال له رسم بياني مستقل يتحدث تلقائياً", width: 7);

        int r = 5;
        foreach (var c in m.Cards.Where(x => x.Type == QuestionType.Likert5).OrderBy(x => x.Order))
        {
            ws.Cell(r, 1).Value = $"س{c.Order}: {c.QuestionAr}";
            ws.Range(r, 1, r, 7).Merge();
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Font.FontColor = Navy;
            ws.Cell(r, 1).Style.Font.FontSize = 12;
            ws.Cell(r, 1).Style.Alignment.WrapText = true;
            ws.Row(r).Height = 30;
            r++;

            var l = c.Likert;
            if (l == null || l.Total == 0)
            {
                ws.Cell(r++, 1).Value = "لا توجد إجابات بعد.";
                r++;
                continue;
            }

            HeaderRow(ws, r, "الدرجة", "التكرار", "النسبة %");
            int dataStart = r + 1;
            r++;
            for (int score = 1; score <= 5; score++)
            {
                int cnt = l.Distribution[score - 1];
                double pct = l.Total > 0 ? Math.Round(100.0 * cnt / l.Total, 1) : 0;
                ws.Cell(r, 1).Value = score;
                ws.Cell(r, 2).Value = cnt;
                ws.Cell(r, 3).Value = pct;
                r++;
            }
            int dataEnd = r - 1;
            StripeBand(ws, dataStart, dataEnd, 3);
            ws.Range(dataStart, 1, dataEnd, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(dataStart, 3, dataEnd, 3).Style.NumberFormat.Format = "0.0";

            // Native column chart, anchored to the right of the small table.
            charts.Add(new XlsxChartBuilder.ChartRequest(
                SheetName: "ليكرت",
                Kind: XlsxChartBuilder.ChartKind.Column,
                Title: $"س{c.Order} — توزيع الإجابات",
                CategoryRange: $"A{dataStart}:A{dataEnd}",
                ValueRange: $"B{dataStart}:B{dataEnd}",
                SeriesName: "التكرار",
                FromCol: 4, FromRow: dataStart - 2,
                ToCol: 10, ToRow: dataStart - 2 + 13));

            // Stat strip below the table.
            var stat = ws.Range(r, 1, r, 3);
            stat.Merge();
            stat.Value = $"المتوسط {l.Mean:0.##} · الوسيط {l.Median:0.#} · العالية {l.PctHigh:0.#}% · العدد {l.Total}";
            StyleTotal(stat);
            stat.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            // Advance past the taller of (table + stat) and the chart anchor.
            int chartEnd = dataStart - 2 + 13;
            r = Math.Max(r + 1, chartEnd) + 2;
        }
        Finish(ws);
        ws.Column(1).Width = 14;
        ws.Column(2).Width = 14;
        ws.Column(3).Width = 14;
        for (int c = 4; c <= 10; c++) ws.Column(c).Width = 12;
    }

    private void BuildChoice(XLWorkbook wb, FinalReportViewModel m,
        List<XlsxChartBuilder.ChartRequest> charts)
    {
        var ws = NewSheet(wb, "اختيار من متعدد");
        BrandHeader(ws, 1, "أسئلة الاختيار من متعدد (س2 و س3 و س6)",
            "تكرار الخيارات ونسبتها المئوية — رسم بياني تلقائي لكل سؤال", width: 7);

        int r = 5;
        foreach (var c in m.Cards.Where(x => x.Type == QuestionType.MultipleChoice).OrderBy(x => x.Order))
        {
            ws.Cell(r, 1).Value = $"س{c.Order}: {c.QuestionAr}";
            ws.Range(r, 1, r, 7).Merge();
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Font.FontColor = Navy;
            ws.Cell(r, 1).Style.Font.FontSize = 12;
            ws.Cell(r, 1).Style.Alignment.WrapText = true;
            ws.Row(r).Height = 30;
            r++;

            var ch = c.Choices;
            if (ch == null || ch.Count == 0)
            {
                ws.Cell(r++, 1).Value = "لا توجد إجابات بعد.";
                r++;
                continue;
            }
            HeaderRow(ws, r, "الخيار", "العدد", "النسبة %");
            int dataStart = r + 1;
            r++;
            foreach (var x in ch)
            {
                ws.Cell(r, 1).Value = x.ChoiceText;
                ws.Cell(r, 2).Value = x.Count;
                ws.Cell(r, 3).Value = x.Percent;
                r++;
            }
            int dataEnd = r - 1;
            StripeBand(ws, dataStart, dataEnd, 3);
            ws.Range(dataStart, 2, dataEnd, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(dataStart, 3, dataEnd, 3).Style.NumberFormat.Format = "0.0";
            ws.Range(dataStart, 3, dataEnd, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(dataStart, 1, dataEnd, 1).Style.Alignment.WrapText = true;

            // Horizontal bar chart works better for long Arabic option texts.
            charts.Add(new XlsxChartBuilder.ChartRequest(
                SheetName: "اختيار من متعدد",
                Kind: XlsxChartBuilder.ChartKind.Bar,
                Title: $"س{c.Order} — تكرار الخيارات",
                CategoryRange: $"A{dataStart}:A{dataEnd}",
                ValueRange: $"B{dataStart}:B{dataEnd}",
                SeriesName: "العدد",
                FromCol: 4, FromRow: dataStart - 2,
                ToCol: 10, ToRow: dataStart - 2 + Math.Max(14, dataEnd - dataStart + 5)));

            var tot = ws.Range(r, 1, r, 3);
            ws.Cell(r, 1).Value = "الإجمالي";
            ws.Cell(r, 2).Value = ch.Sum(x => x.Count);
            ws.Cell(r, 3).Value = 100;
            ws.Cell(r, 3).Style.NumberFormat.Format = "0";
            StyleTotal(tot);

            int chartEnd = dataStart - 2 + Math.Max(14, dataEnd - dataStart + 5);
            r = Math.Max(r + 1, chartEnd) + 2;
        }
        Finish(ws);
        ws.Column(1).Width = 40;
        ws.Column(2).Width = 12;
        ws.Column(3).Width = 12;
        for (int c = 4; c <= 10; c++) ws.Column(c).Width = 12;
    }

    private void BuildOpenText(XLWorkbook wb, FinalReportViewModel m,
        List<XlsxChartBuilder.ChartRequest> charts)
    {
        var ws = NewSheet(wb, "نص حر");
        BrandHeader(ws, 1, "الأسئلة المفتوحة (س4 و س5 و س7)",
            "تصنيف الإجابات النصية تلقائياً — رسم بياني لكل سؤال", width: 7);

        int r = 5;
        foreach (var c in m.Cards.Where(x => x.Type == QuestionType.OpenText).OrderBy(x => x.Order))
        {
            ws.Cell(r, 1).Value = $"س{c.Order}: {c.QuestionAr}";
            ws.Range(r, 1, r, 7).Merge();
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Font.FontColor = Navy;
            ws.Cell(r, 1).Style.Font.FontSize = 12;
            ws.Cell(r, 1).Style.Alignment.WrapText = true;
            ws.Row(r).Height = 30;
            r++;

            var o = c.OpenText;
            if (o == null || o.TotalResponses == 0)
            {
                ws.Cell(r++, 1).Value = "لا توجد إجابات نصية بعد.";
                r++;
                continue;
            }
            ws.Cell(r, 1).Value = $"إجمالي الإجابات: {o.TotalResponses} · غير مصنّف: {o.UncategorizedCount}";
            ws.Range(r, 1, r, 7).Merge();
            ws.Cell(r, 1).Style.Font.FontColor = TextMd;
            r++;

            HeaderRow(ws, r, "الفئة", "العدد", "النسبة %");
            int dataStart = r + 1;
            r++;
            if (o.Categories.Count == 0)
            {
                ws.Cell(r++, 1).Value = "لم تُصنّف الإجابات بعد.";
            }
            else
            {
                foreach (var cat in o.Categories)
                {
                    ws.Cell(r, 1).Value = cat.Category;
                    ws.Cell(r, 2).Value = cat.Count;
                    ws.Cell(r, 3).Value = cat.Percent;
                    r++;
                }
                int dataEnd = r - 1;
                StripeBand(ws, dataStart, dataEnd, 3);
                ws.Range(dataStart, 3, dataEnd, 3).Style.NumberFormat.Format = "0.0";

                charts.Add(new XlsxChartBuilder.ChartRequest(
                    SheetName: "نص حر",
                    Kind: XlsxChartBuilder.ChartKind.Bar,
                    Title: $"س{c.Order} — تصنيفات الإجابات",
                    CategoryRange: $"A{dataStart}:A{dataEnd}",
                    ValueRange: $"B{dataStart}:B{dataEnd}",
                    SeriesName: "العدد",
                    FromCol: 4, FromRow: dataStart - 3,
                    ToCol: 10, ToRow: dataStart - 3 + Math.Max(14, dataEnd - dataStart + 5)));
                int chartEnd = dataStart - 3 + Math.Max(14, dataEnd - dataStart + 5);
                r = Math.Max(r, chartEnd) + 1;
            }
            r += 1;
        }
        Finish(ws);
        ws.Column(1).Width = 40;
        ws.Column(2).Width = 12;
        ws.Column(3).Width = 12;
        for (int c = 4; c <= 10; c++) ws.Column(c).Width = 12;
    }
}
