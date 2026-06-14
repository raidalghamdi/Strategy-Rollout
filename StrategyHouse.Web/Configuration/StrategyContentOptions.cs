namespace StrategyHouse.Web.Configuration;

// Bound from the "StrategyContent" section of appsettings.json.
public class StrategyContentOptions
{
    public LocalizedText Vision { get; set; } = new();
    public LocalizedText Mission { get; set; } = new();
    public List<LocalizedText> Values { get; set; } = new();
}

public class LocalizedText
{
    public string Ar { get; set; } = string.Empty;
    public string En { get; set; } = string.Empty;
}
