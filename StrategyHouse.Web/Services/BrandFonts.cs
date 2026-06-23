namespace StrategyHouse.Web.Services;

// Phase 20.25 — official GAC brand typeface (Frutiger LT Arabic).
// The actual TTF files live at wwwroot/fonts/frutiger/ and are registered
// with QuestPDF in Program.cs. They are also exposed to the website via
// @font-face declarations in wwwroot/css/site.css.
//
// Three weights are licensed per the GAC brand manual:
//   • 45 Light   — captions / disclaimers / very small body text
//   • 55 Roman   — default body text (the workhorse)
//   • 65 Bold    — titles, KPI numbers, emphasis
//
// Each constant is the EXACT internal font family name reported by the TTF —
// inspected with fontTools — so OS-level font matching picks the right file.
internal static class BrandFonts
{
    public const string Light = "Frutiger LT Arabic 45 Light";
    public const string Regular = "Frutiger LT Arabic 55 Roman";
    public const string Bold = "Frutiger LT Arabic 65 Bold";

    // Comma-separated CSS-style fallback chain used wherever a system that
    // accepts a chain is available (e.g. CSS). For Office formats we only get
    // one name per run/cell — but Excel + Office apply OS font substitution
    // automatically, so Cairo → Calibri → Arial still kicks in transparently.
    public const string CssChain = "\"Frutiger LT Arabic 55 Roman\", \"Frutiger LT Arabic\", \"Cairo\", \"Segoe UI\", Tahoma, Arial, sans-serif";
}
