using ClosedXML.Excel;
using StrategyHouse.Domain.Enums;
using StrategyHouse.Web.Models;
using static StrategyHouse.Web.Services.XlsxReportStyle;

namespace StrategyHouse.Web.Services;

// Phase 13.1 — branded .xlsx export of the official survey final report. Four sheets:
// summary, Likert (Q1 & Q8), multiple choice (Q2/Q3/Q6), open text (Q4/Q5/Q7). Shared
// GAC styling via XlsxReportStyle.
public class SurveyReportExcelBuilder
{
    public byte[] Build(FinalReportViewModel m)
    {
        using var wb = new XLWorkbook();
        wb.Style.Font.FontName = "Cairo"; // Phase 20.10 — unify exports on website font
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
        Title(ws, 1, "التقرير النهائي للاستبيان الرسمي");
        ws.Cell(2, 1).Value = m.SurveyTitle;

        HeaderRow(ws, 4, "المؤشر", "القيمة");
        int r = 5;
        DataRow(ws, r++, "إجمالي الردود", m.TotalResponses.ToString());
        DataRow(ws, r++, "عدد الأسئلة", m.Cards.Count.ToString());
        DataRow(ws, r++, "فترة الجمع", m.DateFrom != null ? $"{m.DateFrom:yyyy-MM-dd} ← {m.DateTo:yyyy-MM-dd}" : "—");

        if (m.Takeaways.Count > 0)
        {
            r++;
            ws.Cell(r, 1).Value = "أبرز النتائج";
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Font.FontColor = Navy;
            r++;
            foreach (var t in m.Takeaways) ws.Cell(r++, 1).Value = "• " + t;
        }
        if (m.Insights.Count > 0)
        {
            r++;
            ws.Cell(r, 1).Value = "رؤى شاملة";
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Font.FontColor = Navy;
            r++;
            foreach (var ins in m.Insights) ws.Cell(r++, 1).Value = "• " + ins;
        }
        Finish(ws);
    }

    private void BuildLikert(XLWorkbook wb, FinalReportViewModel m)
    {
        var ws = NewSheet(wb, "ليكرت");
        Title(ws, 1, "أسئلة مقياس ليكرت (س1 و س8)");
        int r = 3;
        foreach (var c in m.Cards.Where(x => x.Type == QuestionType.Likert5).OrderBy(x => x.Order))
        {
            ws.Cell(r, 1).Value = $"س{c.Order}: {c.QuestionAr}";
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Font.FontColor = Navy;
            r++;
            var l = c.Likert;
            if (l == null || l.Total == 0)
            {
                ws.Cell(r++, 1).Value = "لا توجد إجابات بعد.";
                r++;
                continue;
            }
            HeaderRow(ws, r++, "الدرجة", "التكرار", "النسبة %");
            for (int score = 1; score <= 5; score++)
            {
                int cnt = l.Distribution[score - 1];
                ws.Cell(r, 1).Value = score;
                ws.Cell(r, 2).Value = cnt;
                ws.Cell(r, 3).Value = l.Total > 0 ? Math.Round(100.0 * cnt / l.Total, 1) : 0;
                r++;
            }
            ws.Cell(r, 1).Value = $"المتوسط {l.Mean:0.##} · الوسيط {l.Median:0.#} · العالية {l.PctHigh:0.#}% · العدد {l.Total}";
            StyleTotal(ws.Range(r, 1, r, 3));
            r += 2;
        }
        Finish(ws);
    }

    private void BuildChoice(XLWorkbook wb, FinalReportViewModel m)
    {
        var ws = NewSheet(wb, "اختيار من متعدد");
        Title(ws, 1, "أسئلة الاختيار من متعدد (س2 و س3 و س6)");
        int r = 3;
        foreach (var c in m.Cards.Where(x => x.Type == QuestionType.MultipleChoice).OrderBy(x => x.Order))
        {
            ws.Cell(r, 1).Value = $"س{c.Order}: {c.QuestionAr}";
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Font.FontColor = Navy;
            r++;
            var ch = c.Choices;
            if (ch == null || ch.Count == 0)
            {
                ws.Cell(r++, 1).Value = "لا توجد إجابات بعد.";
                r++;
                continue;
            }
            HeaderRow(ws, r++, "الخيار", "العدد", "النسبة %");
            foreach (var x in ch)
            {
                ws.Cell(r, 1).Value = x.ChoiceText;
                ws.Cell(r, 2).Value = x.Count;
                ws.Cell(r, 3).Value = x.Percent;
                r++;
            }
            ws.Cell(r, 1).Value = "الإجمالي";
            ws.Cell(r, 2).Value = ch.Sum(x => x.Count);
            ws.Cell(r, 3).Value = "100";
            StyleTotal(ws.Range(r, 1, r, 3));
            r += 2;
        }
        Finish(ws);
    }

    private void BuildOpenText(XLWorkbook wb, FinalReportViewModel m)
    {
        var ws = NewSheet(wb, "نص حر");
        Title(ws, 1, "الأسئلة المفتوحة (س4 و س5 و س7)");
        int r = 3;
        foreach (var c in m.Cards.Where(x => x.Type == QuestionType.OpenText).OrderBy(x => x.Order))
        {
            ws.Cell(r, 1).Value = $"س{c.Order}: {c.QuestionAr}";
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Font.FontColor = Navy;
            r++;
            var o = c.OpenText;
            if (o == null || o.TotalResponses == 0)
            {
                ws.Cell(r++, 1).Value = "لا توجد إجابات نصية بعد.";
                r++;
                continue;
            }
            ws.Cell(r++, 1).Value = $"إجمالي الإجابات: {o.TotalResponses} · غير مصنّف: {o.UncategorizedCount}";
            HeaderRow(ws, r++, "الفئة", "العدد", "النسبة %");
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
            }
            r += 1;
        }
        Finish(ws);
    }

    private static void DataRow(IXLWorksheet ws, int row, string label, string value)
    {
        ws.Cell(row, 1).Value = label;
        ws.Cell(row, 2).Value = value;
    }
}
