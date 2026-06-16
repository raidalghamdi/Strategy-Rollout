namespace StrategyHouse.Web.Models;

// Phase 14 — the canonical list of executive-report sections, their stable keys and Arabic
// titles, plus a helper to parse the user's selection from the ?sections= query string (or
// the saved cookie). All sections are selected by default.
public sealed class ExecReportSections
{
    public const string CookieName = "exec_report_sections";

    public const string Overview = "overview";
    public const string Departments = "departments";
    public const string Quiz = "quiz";
    public const string Survey = "survey";
    public const string Contributions = "contributions";
    public const string Signatures = "signatures";
    public const string LeadershipAlignment = "leadership_alignment";
    public const string LeadershipCulture = "leadership_culture";
    public const string LeadershipRisks = "leadership_risks";
    public const string LeadershipMaturity = "leadership_maturity";
    public const string LeadershipRecommendations = "leadership_recommendations";

    // Ordered (key, Arabic title) — drives the checkbox list and PPTX/PDF/XLSX section order.
    public static readonly IReadOnlyList<(string Key, string Title)> All = new (string, string)[]
    {
        (Overview, "النظرة العامة التنفيذية"),
        (Departments, "تفصيل الإدارات"),
        (Quiz, "تحليلات الاختبار"),
        (Survey, "تحليلات الاستبيان"),
        (Contributions, "المساهمات"),
        (Signatures, "التوقيعات والتعليقات"),
        (LeadershipAlignment, "الاتساق الاستراتيجي"),
        (LeadershipCulture, "الثقافة والمشاركة"),
        (LeadershipRisks, "المخاطر والفرص"),
        (LeadershipMaturity, "النضج التنظيمي"),
        (LeadershipRecommendations, "توصيات القيادة"),
    };

    private static readonly HashSet<string> AllKeys = All.Select(x => x.Key).ToHashSet();

    private readonly HashSet<string> _selected;

    private ExecReportSections(IEnumerable<string> selected) => _selected = selected.ToHashSet();

    public bool Has(string key) => _selected.Contains(key);

    public IEnumerable<string> SelectedKeys => All.Select(x => x.Key).Where(_selected.Contains);

    public string ToQueryValue() => string.Join(",", SelectedKeys);

    public static ExecReportSections AllSelected() => new(AllKeys);

    // Parse a comma-separated value. Unknown keys are ignored; empty/null → all sections.
    public static ExecReportSections Parse(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return AllSelected();
        var picked = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(AllKeys.Contains)
            .ToHashSet();
        return picked.Count == 0 ? AllSelected() : new ExecReportSections(picked);
    }
}
