namespace StrategyHouse.Web.Configuration;

// Bound from the "StrategyContent" section of appsettings.json.
public class StrategyContentOptions
{
    // Strategy period label (e.g. "2026-2030"), bound from StrategyContent:PeriodLabel.
    // Used wherever the period appears in user-facing/runtime-composed text so it is
    // no longer hardcoded. Defaults to "2026-2030" when the key is absent.
    public string PeriodLabel { get; set; } = "2026-2030";

    // Optional fixed seed for the roster/quiz shuffles. Null (default) means use
    // Random.Shared (non-deterministic). Set an int only when deterministic output
    // is required (e.g. reproducible tests).
    public int? RandomSeed { get; set; }

    public LocalizedText Vision { get; set; } = new();
    public LocalizedText Mission { get; set; } = new();
    public List<LocalizedText> Values { get; set; } = new();
}

public class LocalizedText
{
    public string Ar { get; set; } = string.Empty;
    public string En { get; set; } = string.Empty;
}
