namespace StrategyHouse.Web.Configuration;

// Bound from the "Features" section of appsettings.json. Reloadable via
// IOptionsMonitor so the UseExternalDb flag can be toggled at runtime (the JSON
// file is added with reloadOnChange:true) without an application restart.
public class FeaturesOptions
{
    public bool UseExternalDb { get; set; }
}
