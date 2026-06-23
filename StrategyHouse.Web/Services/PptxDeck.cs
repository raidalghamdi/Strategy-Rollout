using ShapeCrawler;

namespace StrategyHouse.Web.Services;

// Phase 20.24 — GAC-branded PPTX builder rewritten to match the official
// brand template (`tem.pptx` + guideline-kit.pdf): white body, dark teal→green
// gradient header band with white GAC logo on the right, lime accent strip,
// native PowerPoint bar/pie charts (matching the PDF report). RTL Arabic
// content with Cairo latin hint (graceful fallback).
//
// Built on ShapeCrawler 0.79.3 — Slide canvas is 960 × 540 pt (widescreen).
internal sealed class PptxDeck
{
    // ---- GAC brand palette (guideline-kit.pdf + GAC-Brand-Manual.pdf) ----
    public const string Lime    = "9DC41A"; // primary accent
    public const string Blue    = "0069A7"; // corporate blue
    public const string Cyan    = "46BCCD";
    public const string Green   = "009845"; // GAC green
    public const string GreenDk = "006B30"; // header gradient end
    public const string TealDk  = "0B3D40"; // header gradient start (matches tem.pptx)
    public const string Gray    = "76777A"; // cool gray
    public const string Navy    = "0E2A47"; // body title text
    public const string Gold    = "FAC126"; // legacy gold (kept for back-compat callers)
    public const string White   = "FFFFFF";
    public const string Surface = "F7FAFC"; // light card background
    public const string Border  = "E5E7EB"; // soft card border
    public const string TextDk  = "1F2937"; // body text on white
    public const string TextMd  = "4B5563"; // muted body text
    public const string PaleBlue = "CFE2F0"; // legacy back-compat

    private const int CanvasW = 960;
    private const int CanvasH = 540;
    private const int BlankLayout = 6;

    // Header geometry
    private const int HeaderH = 96;       // header band height
    private const int LimeH   = 4;        // lime accent strip beneath header
    private const int LogoW   = 150;      // white logo width on header
    private const int LogoH   = 48;
    private const int LogoX   = 36;       // logo x (LEFT — RTL flips visual order)
    private const int LogoY   = 24;       // logo y inside header

    private readonly Presentation _pres = new();
    private readonly string? _whiteLogoPath;
    private readonly string? _colorLogoPath;

    public PptxDeck(string? whiteLogoPath = null, string? colorLogoPath = null)
    {
        _whiteLogoPath = whiteLogoPath;
        _colorLogoPath = colorLogoPath;
    }

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        _pres.Save(ms);
        return ms.ToArray();
    }

    public record Line(string Text, int Size, bool Bold, string Color);

    public static Line L(string text, int size, bool bold, string color) => new(text, size, bold, color);

    // ============================================================
    // Public slide types
    // ============================================================

    // Cover slide — dark gradient band, white logo on the right (RTL),
    // hairline lime accent, large title + subtitle on white body.
    public void TitleSlide(string title, IEnumerable<Line> subtitle)
    {
        var slide = NewSlide();
        FillBackground(slide, White);

        // Full-bleed gradient header (top half look — emulates `tem.pptx`).
        DrawGradientBand(slide, 0, 0, CanvasW, 240);
        // Lime accent strip beneath the band.
        Paint(AddShape(slide, 0, 240, CanvasW, LimeH), Lime);

        EmbedLogo(slide, white: true);

        // Title + subtitle below the band.
        Style(AddTextBox(slide, 60, 280, 840, 80),
            new[] { L(title, 34, true, Navy) },
            TextHorizontalAlignment.Center);
        Style(AddTextBox(slide, 60, 370, 840, 140),
            subtitle, TextHorizontalAlignment.Center);

        // Subtle footer rule.
        Paint(AddShape(slide, 60, 500, CanvasW - 120, 1), Border);
    }

    // Standard content slide — branded header band + lime strip + RTL body.
    public void ContentSlide(string title, IEnumerable<Line> body)
    {
        var slide = NewSlide();
        FillBackground(slide, White);
        Header(slide, title);

        Style(AddTextBox(slide, 50, HeaderH + LimeH + 16, 860, CanvasH - HeaderH - LimeH - 40),
            body, TextHorizontalAlignment.Right);
    }

    // KPI grid — branded header + 2x3 lime/blue cards on white surface.
    public void KpiSlide(string title, IReadOnlyList<(string Value, string Label)> cards)
    {
        var slide = NewSlide();
        FillBackground(slide, White);
        Header(slide, title);

        const int cols = 3;
        const int cardW = 260, cardH = 130, gapX = 24, gapY = 28;
        const int gridW = cols * cardW + (cols - 1) * gapX; // 828
        int startX = (CanvasW - gridW) / 2 + gridW - cardW; // RTL: right-most column first
        int startY = HeaderH + LimeH + 32;

        for (int i = 0; i < cards.Count; i++)
        {
            int row = i / cols, col = i % cols;
            int x = startX - col * (cardW + gapX);
            int y = startY + row * (cardH + gapY);

            // Card surface
            var card = AddShape(slide, x, y, cardW, cardH, Geometry.RoundedRectangle);
            card.Fill!.SetColor(Surface);
            card.Outline!.SetHexColor(Border);

            // Top accent stripe (lime)
            Paint(AddShape(slide, x, y, cardW, 4), Lime);

            // Value (large) + label (muted)
            Style(AddTextBox(slide, x + 12, y + 22, cardW - 24, 60),
                new[] { L(cards[i].Value, 32, true, Blue) },
                TextHorizontalAlignment.Center);
            Style(AddTextBox(slide, x + 12, y + 80, cardW - 24, 40),
                new[] { L(cards[i].Label, 13, false, TextMd) },
                TextHorizontalAlignment.Center);
        }
    }

    // Slide with a single bar chart (horizontal categories) — branded header,
    // optional supporting body lines beneath chart.
    public void BarChartSlide(string title, string chartTitle,
        IDictionary<string, double> data, IEnumerable<Line>? body = null)
    {
        var slide = NewSlide();
        FillBackground(slide, White);
        Header(slide, title);

        int chartY = HeaderH + LimeH + 24;
        int chartH = 280;

        if (data.Count == 0)
        {
            Style(AddTextBox(slide, 60, chartY + 80, 840, 60),
                new[] { L("لا توجد بيانات لعرضها بعد.", 16, false, TextMd) },
                TextHorizontalAlignment.Center);
            if (body != null)
                Style(AddTextBox(slide, 50, chartY + chartH + 16, 860, 100),
                    body, TextHorizontalAlignment.Right);
            return;
        }

        // ShapeCrawler AddBarChart takes Dictionary<string,double>.
        var dict = new Dictionary<string, double>(data);
        slide.Shapes.AddBarChart(60, chartY, 840, chartH, dict, chartTitle);

        if (body != null)
        {
            Style(AddTextBox(slide, 50, chartY + chartH + 12, 860, CanvasH - chartY - chartH - 40),
                body, TextHorizontalAlignment.Right);
        }
    }

    // Slide with a pie chart (e.g. pillar shares) on the left + supporting
    // lines on the right.
    public void PieChartSlide(string title, string chartTitle,
        IDictionary<string, double> data, IEnumerable<Line>? body = null)
    {
        var slide = NewSlide();
        FillBackground(slide, White);
        Header(slide, title);

        int chartY = HeaderH + LimeH + 24;
        int chartH = 320;

        if (data.Count == 0)
        {
            Style(AddTextBox(slide, 60, chartY + 80, 840, 60),
                new[] { L("لا توجد بيانات لعرضها بعد.", 16, false, TextMd) },
                TextHorizontalAlignment.Center);
            return;
        }

        var dict = new Dictionary<string, double>(data);
        // Pie on the LEFT (ShapeCrawler doubles)
        slide.Shapes.AddPieChart(60, chartY, 380, chartH, dict, chartTitle);

        if (body != null)
        {
            Style(AddTextBox(slide, 460, chartY, 440, chartH),
                body, TextHorizontalAlignment.Right);
        }
    }

    // Section divider — full-bleed dark gradient slide with large white title.
    public void SectionDivider(string title, string? subtitle = null)
    {
        var slide = NewSlide();
        DrawGradientBand(slide, 0, 0, CanvasW, CanvasH);
        Paint(AddShape(slide, 0, CanvasH - 6, CanvasW, 6), Lime);

        Style(AddTextBox(slide, 60, 220, 840, 80),
            new[] { L(title, 40, true, White) },
            TextHorizontalAlignment.Center);

        if (!string.IsNullOrWhiteSpace(subtitle))
            Style(AddTextBox(slide, 60, 310, 840, 60),
                new[] { L(subtitle!, 18, false, "C7E0D2") }, // pale-green tint
                TextHorizontalAlignment.Center);
    }

    // ============================================================
    // Internals
    // ============================================================

    private IUserSlide NewSlide()
    {
        _pres.Slides.Add(BlankLayout);
        var slide = _pres.Slides[_pres.Slides.Count - 1];
        // Drop the empty Title placeholder shipped with layout 6.
        foreach (var ph in slide.Shapes.Where(s => s.PlaceholderType != null).ToList())
            ph.Remove();
        return slide;
    }

    private static ITextBox AddTextBox(IUserSlide slide, int x, int y, int w, int h)
    {
        slide.Shapes.AddTextBox(x, y, w, h, " ");
        var shape = slide.Shapes[slide.Shapes.Count - 1];
        shape.Outline!.SetNoOutline();
        shape.Fill!.SetNoFill();
        return shape.TextBox!;
    }

    private static IShape AddShape(IUserSlide slide, int x, int y, int w, int h)
    {
        slide.Shapes.AddShape(x, y, w, h);
        return slide.Shapes[slide.Shapes.Count - 1];
    }

    private static IShape AddShape(IUserSlide slide, int x, int y, int w, int h, Geometry geometry)
    {
        slide.Shapes.AddShape(x, y, w, h, geometry);
        return slide.Shapes[slide.Shapes.Count - 1];
    }

    private static void Paint(IShape shape, string hex)
    {
        shape.Fill!.SetColor(hex);
        shape.Outline!.SetNoOutline();
    }

    private static void FillBackground(IUserSlide slide, string hex)
        => Paint(AddShape(slide, 0, 0, CanvasW, CanvasH), hex);

    // Approximate the navy→green gradient with 18 thin vertical bands stacked
    // horizontally. ShapeCrawler 0.79 has no gradient API — this reproduces
    // the look in plain shape fills, which PowerPoint and LibreOffice both
    // render identically.
    private static void DrawGradientBand(IUserSlide slide, int x, int y, int w, int h)
    {
        const int steps = 18;
        for (int i = 0; i < steps; i++)
        {
            int sx = x + (int)(w * (i / (double)steps));
            int sw = (int)Math.Ceiling(w / (double)steps) + 1; // overlap to hide seams
            string hex = LerpHex(TealDk, GreenDk, i / (double)(steps - 1));
            Paint(AddShape(slide, sx, y, sw, h), hex);
        }
    }

    private static string LerpHex(string fromHex, string toHex, double t)
    {
        int fr = int.Parse(fromHex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
        int fg = int.Parse(fromHex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
        int fb = int.Parse(fromHex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
        int tr = int.Parse(toHex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
        int tg = int.Parse(toHex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
        int tb = int.Parse(toHex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
        int r = (int)Math.Round(fr + (tr - fr) * t);
        int g = (int)Math.Round(fg + (tg - fg) * t);
        int b = (int)Math.Round(fb + (tb - fb) * t);
        return $"{r:X2}{g:X2}{b:X2}";
    }

    private void Header(IUserSlide slide, string title)
    {
        DrawGradientBand(slide, 0, 0, CanvasW, HeaderH);
        Paint(AddShape(slide, 0, HeaderH, CanvasW, LimeH), Lime);
        EmbedLogo(slide, white: true);

        // Title — RTL-right-aligned, leaves room for the logo on the left.
        Style(AddTextBox(slide, 36 + LogoW + 24, 28, CanvasW - 36 - LogoW - 24 - 36, HeaderH - 36),
            new[] { L(title, 22, true, White) },
            TextHorizontalAlignment.Right);
    }

    private void EmbedLogo(IUserSlide slide, bool white)
    {
        var path = white ? _whiteLogoPath : _colorLogoPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            using var stream = File.OpenRead(path);
            slide.Shapes.AddPicture(stream);
            // The picture lands at default position — re-position to top-left.
            var pic = slide.Shapes[slide.Shapes.Count - 1];
            pic.X = LogoX;
            pic.Y = LogoY;
            pic.Width = LogoW;
            pic.Height = LogoH;
        }
        catch
        {
            // logo embed failure is non-fatal — slide stays usable without it.
        }
    }

    private static void Style(ITextBox tb, IEnumerable<Line> lines, TextHorizontalAlignment align)
    {
        var list = lines.ToList();
        if (list.Count == 0) list.Add(L(" ", 12, false, TextDk));

        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) tb.Paragraphs.Add();
            var p = tb.Paragraphs[i];
            p.Text = string.IsNullOrEmpty(list[i].Text) ? " " : list[i].Text;
            p.HorizontalAlignment = align;
            var portion = p.Portions[0];
            portion.Font!.Size = list[i].Size;
            portion.Font.IsBold = list[i].Bold;
            portion.Font.Color.Set(list[i].Color);
            portion.Font.LatinName = "Cairo";
        }
    }
}
