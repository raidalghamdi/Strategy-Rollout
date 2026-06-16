using ClosedXML.Excel;

namespace StrategyHouse.Web.Services;

// Phase 13.1 — shared GAC styling for the report .xlsx builders: navy header rows with
// white bold font, gold totals, RTL Cairo sheets, auto-sized (capped) columns.
internal static class XlsxReportStyle
{
    public static readonly XLColor Navy = XLColor.FromHtml("#00192B");
    public static readonly XLColor Gold = XLColor.FromHtml("#FAC126");
    public static readonly XLColor White = XLColor.White;

    public static IXLWorksheet NewSheet(XLWorkbook wb, string name)
    {
        var ws = wb.Worksheets.Add(name);
        ws.RightToLeft = true;
        ws.Style.Font.FontName = "Cairo";
        return ws;
    }

    public static void Title(IXLWorksheet ws, int row, string text)
    {
        var cell = ws.Cell(row, 1);
        cell.Value = text;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 14;
        cell.Style.Font.FontColor = Navy;
    }

    public static void SubTitle(IXLWorksheet ws, int row, string text)
    {
        var cell = ws.Cell(row, 1);
        cell.Value = text;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontColor = Navy;
    }

    public static void HeaderRow(IXLWorksheet ws, int row, params string[] headers)
    {
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(row, i + 1).Value = headers[i];
        var range = ws.Range(row, 1, row, headers.Length);
        range.Style.Fill.BackgroundColor = Navy;
        range.Style.Font.FontColor = White;
        range.Style.Font.Bold = true;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    public static void StyleTotal(IXLRange range)
    {
        range.Style.Fill.BackgroundColor = Gold;
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = Navy;
    }

    public static void Finish(IXLWorksheet ws)
    {
        ws.Columns().AdjustToContents();
        foreach (var col in ws.ColumnsUsed())
            if (col.Width > 60) col.Width = 60;
    }
}
