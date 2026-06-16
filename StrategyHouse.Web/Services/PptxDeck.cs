using ShapeCrawler;

namespace StrategyHouse.Web.Services;

// Phase 14 — high-level PPTX builder on top of ShapeCrawler. The Phase 13.1 hand-rolled
// OpenXML builder produced files that PowerPoint/Keynote refused to open (missing theme +
// master/layout parts). ShapeCrawler bootstraps a complete, valid package for us; we only
// add branded GAC shapes (navy/gold), RTL paragraphs and a Cairo latin font (graceful
// fallback when Cairo is unavailable on the viewer).
//
// Slides are the ShapeCrawler default 960 x 540 pt widescreen canvas. A fresh deck is
// created per request, so nothing is shared across concurrent exports.
internal sealed class PptxDeck
{
    public const string Navy = "00192B";
    public const string Gold = "FAC126";
    public const string Blue = "0069A7";
    public const string White = "FFFFFF";
    public const string LightNavy = "0E2A47";
    public const string PaleBlue = "CFE2F0";

    private const int CanvasW = 960;
    private const int CanvasH = 540;
    private const int BlankLayout = 6; // default template: a single Title placeholder.

    private readonly Presentation _pres = new();

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        _pres.Save(ms);
        return ms.ToArray();
    }

    public record Line(string Text, int Size, bool Bold, string Color);

    public static Line L(string text, int size, bool bold, string color) => new(text, size, bold, color);

    // Title (cover) slide — navy background, centred white title, gold subtitle lines.
    public void TitleSlide(string title, IEnumerable<Line> subtitle)
    {
        var slide = NewSlide();
        FillBackground(slide, Navy);
        GoldBar(slide, 0, 248, CanvasW, 6);

        Style(AddTextBox(slide, 60, 168, 840, 80), new[] { L(title, 36, true, White) }, TextHorizontalAlignment.Center);
        Style(AddTextBox(slide, 60, 270, 840, 180), subtitle, TextHorizontalAlignment.Center);
    }

    // Content slide — navy background, light-navy header band + gold accent line, RTL body.
    public void ContentSlide(string title, IEnumerable<Line> body)
    {
        var slide = NewSlide();
        FillBackground(slide, Navy);
        Header(slide, title);

        Style(AddTextBox(slide, 50, 110, 860, 400), body, TextHorizontalAlignment.Right);
    }

    // KPI-card slide — navy background, header, then a grid of light-navy cards (value/label).
    public void KpiSlide(string title, IReadOnlyList<(string Value, string Label)> cards)
    {
        var slide = NewSlide();
        FillBackground(slide, Navy);
        Header(slide, title);

        const int cols = 3;
        const int cardW = 270, cardH = 120, gapX = 20, gapY = 24;
        int startX = CanvasW - 40 - cardW; // right-aligned first column (RTL)
        int startY = 120;
        for (int i = 0; i < cards.Count; i++)
        {
            int row = i / cols, col = i % cols;
            int x = startX - col * (cardW + gapX);
            int y = startY + row * (cardH + gapY);

            var card = AddShape(slide, x, y, cardW, cardH);
            Paint(card, LightNavy);

            Style(AddTextBox(slide, x, y + 16, cardW, cardH - 16), new[]
            {
                L(cards[i].Value, 30, true, Gold),
                L(cards[i].Label, 13, false, White),
            }, TextHorizontalAlignment.Center);
        }
    }

    // ---- internals ----

    private IUserSlide NewSlide()
    {
        _pres.Slides.Add(BlankLayout);
        var slide = _pres.Slides[_pres.Slides.Count - 1];
        // The blank layout ships with a single Title placeholder we do not use — drop it so
        // it never paints an empty box over our branded shapes.
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

    private static void Paint(IShape shape, string hex)
    {
        shape.Fill!.SetColor(hex);
        shape.Outline!.SetNoOutline();
    }

    private static void FillBackground(IUserSlide slide, string hex)
        => Paint(AddShape(slide, 0, 0, CanvasW, CanvasH), hex);

    private static void GoldBar(IUserSlide slide, int x, int y, int w, int h)
        => Paint(AddShape(slide, x, y, w, h), Gold);

    private static void Header(IUserSlide slide, string title)
    {
        Paint(AddShape(slide, 0, 0, CanvasW, 88), LightNavy);
        GoldBar(slide, 0, 88, CanvasW, 5);
        Style(AddTextBox(slide, 40, 20, 880, 56), new[] { L(title, 26, true, White) }, TextHorizontalAlignment.Right);
    }

    private static void Style(ITextBox tb, IEnumerable<Line> lines, TextHorizontalAlignment align)
    {
        var list = lines.ToList();
        if (list.Count == 0) list.Add(L(" ", 12, false, White));

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
