using StrategyHouse.Domain.Entities;

namespace StrategyHouse.Web.Services;

// Phase 19.20 (Fix 2) — read-layer deduplication for strategy elements.
// Seed / warehouse imports can contain duplicate pillar / objective / initiative
// rows (the same code appearing more than once). User-facing listings, counts,
// dropdowns and lookups should treat each code as a single element. These helpers
// dedup by code (case-insensitive, trimmed); when the code is blank they fall back
// to the Arabic name so genuinely-coded-but-unnamed rows still collapse correctly.
// Results are ordered deterministically by code so the UI is stable across requests.
// NOTE: this is for READ/display paths only — admin CRUD listings are left untouched
// so editors can still see and fix the duplicate rows.
public static class StrategyDedup
{
    private static List<T> Dedup<T>(IEnumerable<T> source, Func<T, string?> code, Func<T, string?> name)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<T>();
        foreach (var item in source)
        {
            var raw = code(item);
            var key = string.IsNullOrWhiteSpace(raw) ? name(item) : raw;
            key = (key ?? string.Empty).Trim();
            if (key.Length == 0) { result.Add(item); continue; }
            if (seen.Add(key)) result.Add(item);
        }
        return result
            .OrderBy(x => (code(x) ?? name(x) ?? string.Empty).Trim(), StringComparer.Ordinal)
            .ToList();
    }

    public static List<Pillar> ByPillarCode(IEnumerable<Pillar> source)
        => Dedup(source, p => p.PlrCode, p => p.PillarName);

    public static List<Objective> ByObjectiveCode(IEnumerable<Objective> source)
        => Dedup(source, o => o.ObjectiveCode, o => o.ObjectiveName);

    public static List<Initiative> ByInitiativeCode(IEnumerable<Initiative> source)
        => Dedup(source, i => i.InitiativeCode, i => i.InitiativeName);

    public static List<CoverageRow> ByRowCode(IEnumerable<CoverageRow> source)
        => Dedup(source, r => r.Code, r => r.Label);
}
