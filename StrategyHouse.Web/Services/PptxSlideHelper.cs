using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace StrategyHouse.Web.Services;

// Phase 13.1 — small OpenXML helper that builds clean, branded GAC slides (navy/gold,
// white text, RTL paragraphs) without embedded charts. Used by both PowerPoint builders.
// Slides are 13.33" x 7.5" widescreen (12192000 x 6858000 EMU). A fresh instance is created
// per build so the shape-id counter is never shared across concurrent requests.
internal sealed class PptxSlideHelper
{
    public const string Navy = "00192B";
    public const string Gold = "FAC126";
    public const string White = "FFFFFF";
    public const string LightNavy = "0E2A47";

    public const long SlideWidth = 12192000L;
    public const long SlideHeight = 6858000L;

    private uint _shapeId = 100U;

    // Creates the presentation skeleton (presentation part + master + layout) and returns
    // the SlideIdList the caller appends slides to.
    public static (PresentationPart pres, SlideLayoutPart layout) Init(PresentationDocument doc)
    {
        var presentationPart = doc.AddPresentationPart();
        presentationPart.Presentation = new Presentation();

        var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>();
        var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();

        // Minimal master.
        slideMasterPart.SlideMaster = new SlideMaster(
            new CommonSlideData(new ShapeTree(
                new P.NonVisualGroupShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                    new P.NonVisualGroupShapeDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties()),
                new P.GroupShapeProperties(new A.TransformGroup()))),
            new P.ColorMap
            {
                Background1 = A.ColorSchemeIndexValues.Light1,
                Text1 = A.ColorSchemeIndexValues.Dark1,
                Background2 = A.ColorSchemeIndexValues.Light2,
                Text2 = A.ColorSchemeIndexValues.Dark2,
                Accent1 = A.ColorSchemeIndexValues.Accent1,
                Accent2 = A.ColorSchemeIndexValues.Accent2,
                Accent3 = A.ColorSchemeIndexValues.Accent3,
                Accent4 = A.ColorSchemeIndexValues.Accent4,
                Accent5 = A.ColorSchemeIndexValues.Accent5,
                Accent6 = A.ColorSchemeIndexValues.Accent6,
                Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink
            },
            new P.SlideLayoutIdList(new P.SlideLayoutId { Id = 2147483649U, RelationshipId = slideMasterPart.GetIdOfPart(slideLayoutPart) }));

        slideLayoutPart.SlideLayout = new SlideLayout(
            new CommonSlideData(new ShapeTree(
                new P.NonVisualGroupShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                    new P.NonVisualGroupShapeDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties()),
                new P.GroupShapeProperties(new A.TransformGroup()))))
        { Type = SlideLayoutValues.Blank };

        presentationPart.Presentation.Append(
            new P.SlideMasterIdList(new P.SlideMasterId { Id = 2147483648U, RelationshipId = presentationPart.GetIdOfPart(slideMasterPart) }),
            new P.SlideIdList(),
            new P.SlideSize { Cx = (Int32Value)(int)SlideWidth, Cy = (Int32Value)(int)SlideHeight, Type = SlideSizeValues.Custom },
            new P.NotesSize { Cx = 6858000L, Cy = 9144000L });

        return (presentationPart, slideLayoutPart);
    }

    // Appends a slide with the given background fill and shapes, wiring it to the layout.
    public static void AddSlide(PresentationPart pres, SlideLayoutPart layout, string bgHex, IEnumerable<OpenXmlElement> shapes)
    {
        var slidePart = pres.AddNewPart<SlidePart>();
        var tree = new ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1U, Name = "" },
                new P.NonVisualGroupShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(new A.TransformGroup()));
        foreach (var s in shapes) tree.Append(s);

        slidePart.Slide = new Slide(
            new CommonSlideData(new Background(new BackgroundProperties(
                new A.SolidFill(new A.RgbColorModelHex { Val = bgHex }))), tree));
        slidePart.Slide.AddNamespaceDeclaration("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        slidePart.AddPart(layout);

        var idList = pres.Presentation!.SlideIdList!;
        uint maxId = 255U;
        foreach (var sid in idList.Elements<P.SlideId>())
            if (sid.Id != null && sid.Id.Value > maxId) maxId = sid.Id.Value;
        idList.Append(new P.SlideId { Id = maxId + 1U, RelationshipId = pres.GetIdOfPart(slidePart) });
    }

    // A text box shape. emuX/Y/W/H define the frame; lines are (text, sizePt, bold, colorHex).
    public P.Shape TextBox(long x, long y, long w, long h, IEnumerable<(string Text, int Size, bool Bold, string Color)> lines, bool center = false)
    {
        var body = new P.TextBody(new A.BodyProperties { Anchor = A.TextAnchoringTypeValues.Top, Wrap = A.TextWrappingValues.Square }, new A.ListStyle());
        foreach (var (text, size, bold, color) in lines)
        {
            var para = new A.Paragraph(new A.ParagraphProperties { RightToLeft = true, Alignment = center ? A.TextAlignmentTypeValues.Center : A.TextAlignmentTypeValues.Right });
            foreach (var part in (text ?? "").Split('\n'))
            {
                var runProps = new A.RunProperties(new A.SolidFill(new A.RgbColorModelHex { Val = color }))
                {
                    Language = "ar-SA",
                    FontSize = size * 100,
                    Bold = bold,
                    Dirty = false
                };
                runProps.Append(new A.LatinFont { Typeface = "Cairo" });
                var run = new A.Run(runProps, new A.Text(part));
                if (para.Elements<A.Run>().Any())
                    para.Append(new A.Break());
                para.Append(run);
            }
            body.Append(para);
        }

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = ++_shapeId, Name = "tb" + _shapeId },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(new A.Offset { X = x, Y = y }, new A.Extents { Cx = w, Cy = h }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }),
            body);
    }

    // A solid filled rectangle (used for the gold accent line and navy bands).
    public P.Shape Rect(long x, long y, long w, long h, string fillHex)
    {
        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = ++_shapeId, Name = "rect" + _shapeId },
                new P.NonVisualShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                new A.Transform2D(new A.Offset { X = x, Y = y }, new A.Extents { Cx = w, Cy = h }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                new A.SolidFill(new A.RgbColorModelHex { Val = fillHex })),
            new P.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph()));
    }
}
