using ClosedXML.Excel;
using StrategyHouse.Domain.Enums;
using StrategyHouse.Web.Models;
using static StrategyHouse.Web.Services.XlsxReportStyle;

namespace StrategyHouse.Web.Services;

// Phase 20.24 — branded .xlsx export of the official survey final report. Major upgrade:
//   • Brand header band (navy + lime stripe) on every sheet
//   • Top KPI strip on the summary sheet (mirrors the PPTX/PDF)
//   • In-cell data-bar charts inside the count columns for every question
//   • Striped tables with the corporate palette
//   • One sheet per question type — same content the PDF/PPTX render, now visual
public class SurveyReportExcelBuilder
{
    public byte[] Build(FinalReportViewModel m)
    {
        using var wb = new XLWorkbook();
        wb.Style.Font.FontName = "Cairo";
        BuildSummary(wb, m);
        BuildLikert(wb, m);
        BuildChoice(wb, m);
        BuildOpenText(wb, m);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private void BuildSummary(XLWorkbook wb, FinalReportViewModel m)
    {
        var ws = NewSheet(wb, "ملخص");

        var period = m.DateFrom != null ? $"{m.DateFrom:yyyy-MM-dd} ← {m.DateTo:yyyy-MM-dd}" : "—";
        BrandHeader(ws, 1, "التقرير النهائي للاستبيان الرسمي", $"{m.SurveyTitle} · فترة الجمع: {period}");

        // KPI strip (3 KPIs spanning 6 columns) — rows 5 (value) and 6 (label)
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
        // Keep KPI columns equal width
        for (int c = 1; c <= 6; c++) ws.Column(c).Width = 18;
    }

    private void BuildLikert(XLWorkbook wb, FinalReportViewModel m)
    {
        var ws = NewSheet(wb, "ليكرت");
        BrandHeader(ws, 1, "أسئلة مقياس ليكرت (س1 و س8)",
            "توزيع الإجابات على درجات 1 إلى 5 مع رسم بياني داخل الخلية", width: 4);

        int r = 5;
        foreach (var c in m.Cards.Where(x => x.Type == QuestionType.Likert5).OrderBy(x => x.Order))
        {
            ws.Cell(r, 1).Value = $"س{c.Order}: {c.QuestionAr}";
            ws.Range(r, 1, r, 4).Merge();
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

            HeaderRow(ws, r, "الدرجة", "التكرار", "النسبة %", "الرسم");
            int dataStart = r + 1;
            r++;
            for (int score = 1; score <= 5; score++)
            {
                int cnt = l.Distribution[score - 1];
                double pct = l.Total > 0 ? Math.Round(100.0 * cnt / l.Total, 1) : 0;
                ws.Cell(r, 1).Value = score;
                ws.Cell(r, 2).Value = cnt;
                ws.Cell(r, 3).Value = pct;
                ws.Cell(r, 4).Value = cnt; // duplicate for the visual bar column
                ws.Cell(r, 4).Style.Font.FontColor = White; // hide number on the coloured bar
                r++;
            }
            int dataEnd = r - 1;
            // Stripes + borders
            StripeBand(ws, dataStart, dataEnd, 4);
            // Data Bar on column 4 (visual bar)
            DataBarColumn(ws.Range(dataStart, 4, dataEnd, 4), Blue);
            // Number formats
            ws.Range(dataStart, 1, dataEnd, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(dataStart, 2, dataEnd, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(dataStart, 3, dataEnd, 3).Style.NumberFormat.Format = "0.0";
            ws.Range(dataStart, 3, dataEnd, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Stat strip
            var stat = ws.Range(r, 1, r, 4);
            stat.Merge();
            stat.Value = $"المتوسط {l.Mean:0.##} · الوسيط {l.Median:0.#} · العالية {l.PctHigh:0.#}% · العدد {l.Total}";
            StyleTotal(stat);
            stat.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            r += 2;
        }
        Finish(ws);
        ws.Column(1).Width = 14;
        ws.Column(2).Width = 14;
        ws.Column(3).Width = 14;
        ws.Column(4).Width = 32; // bar column wider
    }

    private void BuildChoice(XLWorkbook wb, FinalReportViewModel m)
    {
        var ws = NewSheet(wb, "اختيار من متعدد");
        BrandHeader(ws, 1, "أسئلة الاختيار من متعدد (س2 و س3 و س6)",
            "تكرار الخيارات والنسبة المئوية مع رسم بياني داخل الخلية", width: 4);

        int r = 5;
        foreach (var c in m.Cards.Where(x => x.Type == QuestionType.MultipleChoice).OrderBy(x => x.Order))
        {
            ws.Cell(r, 1).Value = $"س{c.Order}: {c.QuestionAr}";
            ws.Range(r, 1, r, 4).Merge();
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
            HeaderRow(ws, r, "الخيار", "العدد", "النسبة %", "الرسم");
            int dataStart = r + 1;
            r++;
            foreach (var x in ch)
            {
                ws.Cell(r, 1).Value = x.ChoiceText;
                ws.Cell(r, 2).Value = x.Count;
                ws.Cell(r, 3).Value = x.Percent;
                ws.Cell(r, 4).Value = x.Count;
                ws.Cell(r, 4).Style.Font.FontColor = White;
                r++;
            }
            int dataEnd = r - 1;
            StripeBand(ws, dataStart, dataEnd, 4);
            DataBarColumn(ws.Range(dataStart, 4, dataEnd, 4), Green);
            ws.Range(dataStart, 2, dataEnd, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(dataStart, 3, dataEnd, 3).Style.NumberFormat.Format = "0.0";
            ws.Range(dataStart, 3, dataEnd, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(dataStart, 1, dataEnd, 1).Style.Alignment.WrapText = true;

            var tot = ws.Range(r, 1, r, 4);
            ws.Cell(r, 1).Value = "الإجمالي";
            ws.Cell(r, 2).Value = ch.Sum(x => x.Count);
            ws.Cell(r, 3).Value = 100;
            ws.Cell(r, 3).Style.NumberFormat.Format = "0";
            ws.Cell(r, 4).Value = "";
            StyleTotal(tot);
            r += 2;
        }
        Finish(ws);
        ws.Column(1).Width = 40;
        ws.Column(2).Width = 12;
        ws.Column(3).Width = 12;
        ws.Column(4).Width = 28;
    }

    private void BuildOpenText(XLWorkbook wb, FinalReportViewModel m)
    {
        var ws = NewSheet(wb, "نص حر");
        BrandHeader(ws, 1, "الأسئلة المفتوحة (س4 و س5 و س7)",
            "تصنيف الإجابات النصية تلقائياً", width: 4);

        int r = 5;
        foreach (var c in m.Cards.Where(x => x.Type == QuestionType.OpenText).OrderBy(x => x.Order))
        {
            ws.Cell(r, 1).Value = $"س{c.Order}: {c.QuestionAr}";
            ws.Range(r, 1, r, 4).Merge();
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
            ws.Range(r, 1, r, 4).Merge();
            ws.Cell(r, 1).Style.Font.FontColor = TextMd;
            r++;

            HeaderRow(ws, r, "الفئة", "العدد", "النسبة %", "الرسم");
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
                    ws.Cell(r, 4).Value = cat.Count;
                    ws.Cell(r, 4).Style.Font.FontColor = White;
                    r++;
                }
                int dataEnd = r - 1;
                StripeBand(ws, dataStart, dataEnd, 4);
                DataBarColumn(ws.Range(dataStart, 4, dataEnd, 4), Cyan);
                ws.Range(dataStart, 3, dataEnd, 3).Style.NumberFormat.Format = "0.0";
            }
            r += 1;
        }
        Finish(ws);
        ws.Column(1).Width = 40;
        ws.Column(2).Width = 12;
        ws.Column(3).Width = 12;
        ws.Column(4).Width = 28;
    }
}
