using ClosedXML.Excel;

namespace StrategyHouse.Web.Services;

// Phase 20.24 — shared GAC styling for the report .xlsx builders.
// Phase 20.24.1 — widen KPI cards (2 cols each, wrap text) so Arabic labels
// no longer truncate; Finish() preserves explicit widths set by builders.
// Phase 20.24.3 — DataBarColumn helper retained for backward compat but no
// longer used; the builders now emit native Excel charts via XlsxChartBuilder.
// Phase 20.25 — official GAC brand typeface (Frutiger LT Arabic 55 Roman) is
// applied as the workbook default. Cairo / Calibri / Arial remain the OS-side
// fallback chain for users without Frutiger installed.
internal static class XlsxReportStyle
{
    // ---- GAC brand palette (guideline-kit.pdf + GAC-Brand-Manual.pdf) ----
    public static readonly XLColor Navy      = XLColor.FromHtml("#0E2A47"); // body title text
    public static readonly XLColor TealDk    = XLColor.FromHtml("#0B3D40"); // header gradient start
    public static readonly XLColor Green     = XLColor.FromHtml("#009845"); // GAC green
    public static readonly XLColor GreenDk   = XLColor.FromHtml("#006B30");
    public static readonly XLColor Blue      = XLColor.FromHtml("#0069A7"); // corporate blue
    public static readonly XLColor Cyan      = XLColor.FromHtml("#46BCCD");
    public static readonly XLColor Lime      = XLColor.FromHtml("#9DC41A"); // primary accent
    public static readonly XLColor Gold      = XLColor.FromHtml("#FAC126"); // totals
    public static readonly XLColor White     = XLColor.White;
    public static readonly XLColor Surface   = XLColor.FromHtml("#F7FAFC"); // light card background
    public static readonly XLColor SurfaceAlt = XLColor.FromHtml("#EEF5F9");
    public static readonly XLColor Border    = XLColor.FromHtml("#D6E0E8");
    public static readonly XLColor TextDk    = XLColor.FromHtml("#1F2937");
    public static readonly XLColor TextMd    = XLColor.FromHtml("#4B5563");

    public static IXLWorksheet NewSheet(XLWorkbook wb, string name)
    {
        var ws = wb.Worksheets.Add(name);
        ws.RightToLeft = true;
        ws.Style.Font.FontName = BrandFonts.Regular;
        // Phase 20.25 — also set the workbook-wide default font so every
        // newly-created cell inherits the brand face. Users on machines
        // without Frutiger fall back to Cairo / Calibri / Arial.
        wb.Style.Font.FontName = BrandFonts.Regular;
        ws.ShowGridLines = false; // cleaner look with our borders
        // Brand tab colour for the sheet tab.
        ws.SetTabColor(Green);
        // Phase 20.24.1 — wide layout for KPI strips (up to 6 cards × 2 cols)
        // and data-bar tables. Landscape + fit-to-1-page-wide ensures the full
        // KPI strip and data bars render on each page.
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.FitToPages(1, 0); // 1 page wide, unlimited tall
        ws.PageSetup.Margins.Top = 0.5;
        ws.PageSetup.Margins.Bottom = 0.5;
        ws.PageSetup.Margins.Left = 0.4;
        ws.PageSetup.Margins.Right = 0.4;
        return ws;
    }

    // Phase 20.24 — full-width brand header (navy band + lime stripe).
    public static void BrandHeader(IXLWorksheet ws, int row, string title, string? subtitle = null, int width = 6)
    {
        var band = ws.Range(row, 1, row, width);
        band.Merge();
        band.Value = title;
        band.Style.Fill.BackgroundColor = TealDk;
        band.Style.Font.FontColor = White;
        band.Style.Font.Bold = true;
        band.Style.Font.FontSize = 16;
        band.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        band.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(row).Height = 28;

        // Lime accent stripe directly beneath the band.
        var strip = ws.Range(row + 1, 1, row + 1, width);
        strip.Merge();
        strip.Style.Fill.BackgroundColor = Lime;
        ws.Row(row + 1).Height = 3;

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var sub = ws.Range(row + 2, 1, row + 2, width);
            sub.Merge();
            sub.Value = subtitle;
            sub.Style.Font.FontColor = TextMd;
            sub.Style.Font.FontSize = 11;
            sub.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }
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

    public static void SectionDivider(IXLWorksheet ws, int row, string text, int width = 5)
    {
        var range = ws.Range(row, 1, row, width);
        range.Merge();
        range.Value = text;
        range.Style.Fill.BackgroundColor = Navy;
        range.Style.Font.FontColor = White;
        range.Style.Font.Bold = true;
        range.Style.Font.FontSize = 12;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Row(row).Height = 22;
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
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        range.Style.Border.BottomBorderColor = Lime;
        ws.Row(row).Height = 22;
    }

    public static void StyleTotal(IXLRange range)
    {
        range.Style.Fill.BackgroundColor = Gold;
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = Navy;
        range.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        range.Style.Border.TopBorderColor = Navy;
    }

    // Phase 20.24 — alternating row stripes for a polished tabular look.
    public static void StripeBand(IXLWorksheet ws, int firstRow, int lastRow, int colCount)
    {
        for (int r = firstRow; r <= lastRow; r++)
        {
            if ((r - firstRow) % 2 == 0)
            {
                ws.Range(r, 1, r, colCount).Style.Fill.BackgroundColor = SurfaceAlt;
            }
            ws.Range(r, 1, r, colCount).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            ws.Range(r, 1, r, colCount).Style.Border.BottomBorderColor = Border;
        }
    }

    // Phase 20.24 — bar-style data bar on a numeric range (in-cell chart).
    // ClosedXML 0.105 supports DataBar conditional formatting; we colour-code by series.
    public static void DataBarColumn(IXLRange range, XLColor color)
    {
        var cf = range.AddConditionalFormat().DataBar(color, false);
        cf.LowestValue().HighestValue();
    }

    // Phase 20.24 — KPI strip: a row of branded KPI cards (label + value).
    // Phase 20.24.1 — each card spans 2 columns and underlying column widths
    // are bumped to ≥ 16 so Arabic labels (~15-20 chars) wrap cleanly instead
    // of truncating ("على المساهمة", "إدارات المشاركة", etc.).
    public static void KpiStrip(IXLWorksheet ws, int row,
        IReadOnlyList<(string Label, string Value)> kpis, int startCol = 1, int totalCols = 0)
    {
        if (kpis.Count == 0) return;
        const int colsPerCard = 2;
        // Ensure underlying column widths are wide enough for Arabic text.
        int lastCol = startCol + kpis.Count * colsPerCard - 1;
        for (int c = startCol; c <= lastCol; c++)
        {
            if (ws.Column(c).Width < 16) ws.Column(c).Width = 16;
        }
        for (int i = 0; i < kpis.Count; i++)
        {
            int c1 = startCol + i * colsPerCard;
            int c2 = c1 + colsPerCard - 1;

            // Value row (large, blue)
            var val = ws.Range(row, c1, row, c2);
            val.Merge();
            val.Value = kpis[i].Value;
            val.Style.Fill.BackgroundColor = Surface;
            val.Style.Font.FontColor = Blue;
            val.Style.Font.Bold = true;
            val.Style.Font.FontSize = 18;
            val.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            val.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            val.Style.Alignment.WrapText = true;
            val.Style.Border.TopBorder = XLBorderStyleValues.Thick;
            val.Style.Border.TopBorderColor = Lime;
            val.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
            val.Style.Border.LeftBorderColor = Border;
            val.Style.Border.RightBorder = XLBorderStyleValues.Thin;
            val.Style.Border.RightBorderColor = Border;
            ws.Row(row).Height = 32;

            // Label row (small, muted) — wrap text so Arabic doesn't truncate
            var lab = ws.Range(row + 1, c1, row + 1, c2);
            lab.Merge();
            lab.Value = kpis[i].Label;
            lab.Style.Fill.BackgroundColor = Surface;
            lab.Style.Font.FontColor = TextMd;
            lab.Style.Font.FontSize = 10;
            lab.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            lab.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            lab.Style.Alignment.WrapText = true;
            lab.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            lab.Style.Border.BottomBorderColor = Border;
            lab.Style.Border.LeftBorder = XLBorderStyleValues.Thin;
            lab.Style.Border.LeftBorderColor = Border;
            lab.Style.Border.RightBorder = XLBorderStyleValues.Thin;
            lab.Style.Border.RightBorderColor = Border;
            ws.Row(row + 1).Height = 26;
        }
    }

    // Phase 20.24.1 — preserve explicit column widths set by builders
    // (e.g. KpiStrip widens to ≥16). Only auto-fit columns that weren't
    // touched. Final width is clamped to [14, 60].
    public static void Finish(IXLWorksheet ws)
    {
        foreach (var col in ws.ColumnsUsed())
        {
            double existing = col.Width;
            if (existing < 14)
            {
                col.AdjustToContents();
                if (col.Width > 60) col.Width = 60;
                else if (col.Width < 14) col.Width = 14;
            }
            else if (existing > 60)
            {
                col.Width = 60;
            }
        }
    }
}
