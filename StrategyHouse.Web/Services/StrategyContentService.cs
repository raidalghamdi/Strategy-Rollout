using Microsoft.Extensions.Options;
using StrategyHouse.Web.Configuration;

namespace StrategyHouse.Web.Services;

// Exposes strategy content (Vision/Mission/Values) to controllers and views via DI.
public class StrategyContentService
{
    private readonly StrategyContentOptions _options;

    public StrategyContentService(IOptions<StrategyContentOptions> options)
    {
        _options = options.Value;
    }

    public StrategyContentOptions Content => _options;
    public LocalizedText Vision => _options.Vision;
    public LocalizedText Mission => _options.Mission;
    public IReadOnlyList<LocalizedText> Values => _options.Values;
}
