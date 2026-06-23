using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Drawing.Spreadsheet;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace StrategyHouse.Web.Services;

// Phase 20.24.3 — native Excel chart injector built on top of the SDK.
// ClosedXML 0.105 does NOT expose a chart API, so we open the workbook bytes
// produced by ClosedXML with DocumentFormat.OpenXml and append a ChartPart to
// any sheet that registered a chart request.
//
// All charts are bound to cell ranges via <c:f> formulas, so opening the file
// in Excel and editing the numbers automatically re-draws the chart — no
// human interaction needed. Charts also render when the bound cells are 0 or
// empty (the plot area just appears flat / category axis only).
//
// Three chart kinds are supported, all common in the GAC reports:
//   • Column (vertical bar)  — distributions, counts, scores
//   • Pie                    — part-of-whole (strategic alignment shares)
//   • Bar    (horizontal)    — long category labels (Arabic department names)
//
// Phase 20.25 — chart titles + axis tick labels use the official GAC brand
// typeface (Frutiger LT Arabic 55 Roman). Cairo is the graceful fallback.
internal static class XlsxChartBuilder
{
    // GAC palette (must match XlsxReportStyle constants — hex without #).
    private const string ClrNavy  = "0E2A47";
    private const string ClrGreen = "009845";
    private const string ClrBlue  = "0069A7";
    private const string ClrCyan  = "46BCCD";
    private const string ClrLime  = "9DC41A";
    private const string ClrGold  = "FAC126";
    private const string ClrRed   = "A13544";

    private static readonly string[] PaletteCycle = new[]
    {
        ClrBlue, ClrGreen, ClrCyan, ClrLime, ClrGold, ClrNavy, ClrRed,
    };

    public enum ChartKind { Column, Bar, Pie }

    /// <summary>
    /// A pending chart request collected by the builder.
    /// All ranges are sheet-relative addresses in A1 form WITHOUT sheet prefix
    /// (e.g. "A5:A9"). The sheet name is bound at injection time.
    /// </summary>
    public sealed record ChartRequest(
        string SheetName,
        ChartKind Kind,
        string Title,
        string CategoryRange,         // e.g. "A5:A9"
        string ValueRange,            // e.g. "B5:B9"
        string? SeriesName,           // optional legend label
        // Anchor (top-left + bottom-right in 0-based column/row indexes).
        int FromCol, int FromRow, int ToCol, int ToRow);

    /// <summary>
    /// Takes a .xlsx byte buffer produced by ClosedXML and injects each chart.
    /// Returns the modified bytes. If <paramref name="requests"/> is empty,
    /// the original bytes are returned untouched.
    /// </summary>
    public static byte[] InjectCharts(byte[] xlsxBytes, IReadOnlyList<ChartRequest> requests)
    {
        if (requests.Count == 0) return xlsxBytes;

        // Copy into a resizeable MemoryStream — SpreadsheetDocument.Open mutates in place.
        using var ms = new MemoryStream();
        ms.Write(xlsxBytes, 0, xlsxBytes.Length);
        ms.Position = 0;

        using (var doc = SpreadsheetDocument.Open(ms, true))
        {
            var wbPart = doc.WorkbookPart!;
            // Group requests by sheet so each sheet gets a single DrawingsPart.
            foreach (var grp in requests.GroupBy(r => r.SheetName))
            {
                var sheet = wbPart.Workbook.Descendants<Sheet>()
                    .FirstOrDefault(s => s.Name == grp.Key);
                if (sheet == null) continue;
                var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id!);
                EnsureDrawingsPart(wsPart, out var drawingsPart, out var wsDrawing);

                foreach (var req in grp)
                {
                    AppendChart(wsPart, drawingsPart, wsDrawing, req);
                }
            }
        }

        return ms.ToArray();
    }

    // --------------------- internals ---------------------

    private static void EnsureDrawingsPart(
        WorksheetPart wsPart,
        out DrawingsPart drawingsPart,
        out Xdr.WorksheetDrawing wsDrawing)
    {
        if (wsPart.DrawingsPart is { } existing)
        {
            drawingsPart = existing;
            wsDrawing = drawingsPart.WorksheetDrawing ??= new Xdr.WorksheetDrawing();
            // Ensure the relationship is declared on the worksheet root.
            if (wsPart.Worksheet.Elements<Drawing>().FirstOrDefault() == null)
            {
                wsPart.Worksheet.Append(new Drawing { Id = wsPart.GetIdOfPart(drawingsPart) });
            }
            return;
        }

        drawingsPart = wsPart.AddNewPart<DrawingsPart>();
        wsDrawing = new Xdr.WorksheetDrawing();
        drawingsPart.WorksheetDrawing = wsDrawing;

        // Tell the worksheet about its drawing part.
        wsPart.Worksheet.Append(new Drawing { Id = wsPart.GetIdOfPart(drawingsPart) });
    }

    private static void AppendChart(
        WorksheetPart wsPart,
        DrawingsPart drawingsPart,
        Xdr.WorksheetDrawing wsDrawing,
        ChartRequest req)
    {
        // 1) Create a new ChartPart inside the DrawingsPart.
        var chartPart = drawingsPart.AddNewPart<ChartPart>();
        chartPart.ChartSpace = BuildChartSpace(wsPart, req);

        // 2) Anchor it to the worksheet via a TwoCellAnchor in the drawing.
        string chartRelId = drawingsPart.GetIdOfPart(chartPart);
        var anchor = BuildTwoCellAnchor(req, chartRelId, wsDrawing);
        wsDrawing.Append(anchor);
    }

    private static C.ChartSpace BuildChartSpace(WorksheetPart wsPart, ChartRequest req)
    {
        // Resolve the absolute sheet name used in formulas. We always quote-wrap
        // because GAC sheet names contain Arabic characters and spaces.
        string sheetName = req.SheetName.Replace("'", "''");
        string catRef = $"'{sheetName}'!{req.CategoryRange}";
        string valRef = $"'{sheetName}'!{req.ValueRange}";

        var chartSpace = new C.ChartSpace();
        chartSpace.Append(new C.EditingLanguage { Val = "ar-SA" });

        var chart = new C.Chart();
        chartSpace.Append(chart);

        chart.Append(new C.Title(
            new C.ChartText(new C.RichText(
                new A.BodyProperties { Rotation = 0, UseParagraphSpacing = true, VerticalOverflow = A.TextVerticalOverflowValues.Ellipsis, Vertical = A.TextVerticalValues.Horizontal, Wrap = A.TextWrappingValues.Square, Anchor = A.TextAnchoringTypeValues.Center, AnchorCenter = true },
                new A.ListStyle(),
                new A.Paragraph(
                    new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Right },
                    new A.Run(
                        new A.RunProperties(new A.LatinFont { Typeface = BrandFonts.Bold }) { Language = "ar-SA", FontSize = 1100, Bold = true },
                        new A.Text(req.Title))))),
            new C.Overlay { Val = false }));
        chart.Append(new C.AutoTitleDeleted { Val = false });

        var plotArea = new C.PlotArea();
        plotArea.Append(new C.Layout());
        chart.Append(plotArea);

        switch (req.Kind)
        {
            case ChartKind.Pie:
                BuildPie(plotArea, req, catRef, valRef);
                break;
            case ChartKind.Bar:
                BuildBarOrColumn(plotArea, req, catRef, valRef, horizontal: true);
                break;
            default:
                BuildBarOrColumn(plotArea, req, catRef, valRef, horizontal: false);
                break;
        }

        chart.Append(new C.PlotVisibleOnly { Val = true });
        chart.Append(new C.DisplayBlanksAs { Val = C.DisplayBlanksAsValues.Gap });

        // Right-to-left chart layout for Arabic.
        chartSpace.Append(new C.ExternalData(
            new C.AutoUpdate { Val = false })
        { Id = "rId0" }); // placeholder — real id only needed if external link exists; we omit by removing below

        // Remove placeholder ExternalData (we don't actually have a relationship).
        var ext = chartSpace.Elements<C.ExternalData>().FirstOrDefault();
        if (ext != null) ext.Remove();

        return chartSpace;
    }

    private static void BuildBarOrColumn(C.PlotArea plotArea, ChartRequest req,
        string catRef, string valRef, bool horizontal)
    {
        var bar = new C.BarChart(
            new C.BarDirection { Val = horizontal ? C.BarDirectionValues.Bar : C.BarDirectionValues.Column },
            new C.BarGrouping { Val = C.BarGroupingValues.Clustered },
            new C.VaryColors { Val = false });

        var series = new C.BarChartSeries(
            new C.Index { Val = 0U },
            new C.Order { Val = 0U });

        // Series title (legend label).
        if (!string.IsNullOrWhiteSpace(req.SeriesName))
        {
            series.Append(new C.SeriesText(new C.NumericValue(req.SeriesName!)));
        }

        // Series fill — first GAC accent.
        series.Append(new C.ChartShapeProperties(
            new A.SolidFill(new A.RgbColorModelHex { Val = ClrBlue }),
            new A.Outline(new A.NoFill())));

        // Data labels — show value at end of each bar/column.
        series.Append(new C.DataLabels(
            new C.ShowLegendKey { Val = false },
            new C.ShowValue { Val = true },
            new C.ShowCategoryName { Val = false },
            new C.ShowSeriesName { Val = false },
            new C.ShowPercent { Val = false },
            new C.ShowBubbleSize { Val = false }));

        // Category axis (sheet!range).
        series.Append(new C.CategoryAxisData(new C.StringReference(
            new C.Formula(catRef),
            new C.StringCache())));

        // Values (sheet!range).
        series.Append(new C.Values(new C.NumberReference(
            new C.Formula(valRef),
            new C.NumberingCache(new C.FormatCode("General")))));

        bar.Append(series);
        bar.Append(new C.GapWidth { Val = 150 });
        bar.Append(new C.Overlap { Val = 0 });
        bar.Append(new C.AxisId { Val = 111111111U });
        bar.Append(new C.AxisId { Val = 222222222U });
        plotArea.Append(bar);

        // Category axis.
        plotArea.Append(new C.CategoryAxis(
            new C.AxisId { Val = 111111111U },
            new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }),
            new C.Delete { Val = false },
            new C.AxisPosition { Val = horizontal ? C.AxisPositionValues.Left : C.AxisPositionValues.Bottom },
            new C.TextProperties(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(new A.ParagraphProperties(
                    new A.DefaultRunProperties(new A.LatinFont { Typeface = BrandFonts.Regular }) { FontSize = 900, Language = "ar-SA" }))),
            new C.CrossingAxis { Val = 222222222U }));

        // Value axis.
        plotArea.Append(new C.ValueAxis(
            new C.AxisId { Val = 222222222U },
            new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }),
            new C.Delete { Val = false },
            new C.AxisPosition { Val = horizontal ? C.AxisPositionValues.Bottom : C.AxisPositionValues.Left },
            new C.MajorGridlines(),
            new C.NumberingFormat { FormatCode = "General", SourceLinked = true },
            new C.MajorTickMark { Val = C.TickMarkValues.Outside },
            new C.MinorTickMark { Val = C.TickMarkValues.None },
            new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo },
            new C.TextProperties(
                new A.BodyProperties(),
                new A.ListStyle(),
                new A.Paragraph(new A.ParagraphProperties(
                    new A.DefaultRunProperties(new A.LatinFont { Typeface = BrandFonts.Regular }) { FontSize = 900, Language = "ar-SA" }))),
            new C.CrossingAxis { Val = 111111111U }));
    }

    private static void BuildPie(C.PlotArea plotArea, ChartRequest req, string catRef, string valRef)
    {
        var pie = new C.PieChart(new C.VaryColors { Val = true });

        var series = new C.PieChartSeries(
            new C.Index { Val = 0U },
            new C.Order { Val = 0U });
        if (!string.IsNullOrWhiteSpace(req.SeriesName))
            series.Append(new C.SeriesText(new C.NumericValue(req.SeriesName!)));

        // Color each slice from the palette cycle.
        for (uint i = 0; i < PaletteCycle.Length; i++)
        {
            var dp = new C.DataPoint(
                new C.Index { Val = i },
                new C.Bubble3D { Val = false },
                new C.ChartShapeProperties(
                    new A.SolidFill(new A.RgbColorModelHex { Val = PaletteCycle[i] }),
                    new A.Outline(new A.SolidFill(new A.RgbColorModelHex { Val = "FFFFFF" })) { Width = 9525 }));
            series.Append(dp);
        }

        series.Append(new C.DataLabels(
            new C.ShowLegendKey { Val = false },
            new C.ShowValue { Val = false },
            new C.ShowCategoryName { Val = true },
            new C.ShowSeriesName { Val = false },
            new C.ShowPercent { Val = true },
            new C.ShowBubbleSize { Val = false }));

        series.Append(new C.CategoryAxisData(new C.StringReference(
            new C.Formula(catRef),
            new C.StringCache())));
        series.Append(new C.Values(new C.NumberReference(
            new C.Formula(valRef),
            new C.NumberingCache(new C.FormatCode("General")))));

        pie.Append(series);
        pie.Append(new C.FirstSliceAngle { Val = 0 });
        plotArea.Append(pie);
    }

    private static Xdr.TwoCellAnchor BuildTwoCellAnchor(ChartRequest req, string chartRelId, Xdr.WorksheetDrawing wsDrawing)
    {
        uint id = (uint)(wsDrawing.Elements<Xdr.TwoCellAnchor>().Count() + 1);
        var anchor = new Xdr.TwoCellAnchor(
            new Xdr.FromMarker(
                new Xdr.ColumnId(req.FromCol.ToString()),
                new Xdr.ColumnOffset("0"),
                new Xdr.RowId(req.FromRow.ToString()),
                new Xdr.RowOffset("0")),
            new Xdr.ToMarker(
                new Xdr.ColumnId(req.ToCol.ToString()),
                new Xdr.ColumnOffset("0"),
                new Xdr.RowId(req.ToRow.ToString()),
                new Xdr.RowOffset("0")),
            new Xdr.GraphicFrame(
                new Xdr.NonVisualGraphicFrameProperties(
                    new Xdr.NonVisualDrawingProperties { Id = id, Name = $"Chart {id}" },
                    new Xdr.NonVisualGraphicFrameDrawingProperties()),
                new Xdr.Transform(
                    new A.Offset { X = 0L, Y = 0L },
                    new A.Extents { Cx = 0L, Cy = 0L }),
                new A.Graphic(new A.GraphicData(
                    new C.ChartReference { Id = chartRelId })
                {
                    Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart"
                })),
            new Xdr.ClientData());
        return anchor;
    }
}
