using Microsoft.Extensions.Options;
using StrategyHouse.Web.Configuration;

namespace StrategyHouse.Web.Services;

// Exposes strategy content (Vision/Mission/Values) to controllers and views via DI.
//
// Phase 19.21 (Fix 6) — Vision/Mission/Values are now admin-editable. The appsettings
// "StrategyContent" section still supplies the seed copy and the English text, but any
// value saved at /Admin/Content (keys strategy.vision.ar / strategy.mission.ar /
// strategy.values.ar) overlays the Arabic. Existing journey "بيت الاستراتيجية" views
// read content?.Vision.Ar etc. unchanged, so edits surface with zero per-view changes.
public class StrategyContentService
{
    private readonly StrategyContentOptions _options;
    private readonly PageContentService _pageContent;

    public StrategyContentService(IOptions<StrategyContentOptions> options, PageContentService pageContent)
    {
        _options = options.Value;
        _pageContent = pageContent;
    }

    public StrategyContentOptions Content => _options;

    public LocalizedText Vision => new()
    {
        Ar = _pageContent.Get("strategy.vision.ar", _options.Vision.Ar),
        En = _options.Vision.En,
    };

    public LocalizedText Mission => new()
    {
        Ar = _pageContent.Get("strategy.mission.ar", _options.Mission.Ar),
        En = _options.Mission.En,
    };

    // Values are edited as one comma-separated Arabic line ("الشفافية، التعاون، ...").
    // When a stored line exists we split it back into LocalizedText items (Arabic only);
    // otherwise we fall back to the structured appsettings list (which also carries En).
    public IReadOnlyList<LocalizedText> Values
    {
        get
        {
            var stored = _pageContent.Get("strategy.values.ar", "");
            if (string.IsNullOrWhiteSpace(stored))
                return _options.Values;

            return stored
                .Split(new[] { '،', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => new LocalizedText { Ar = v.Trim() })
                .ToList();
        }
    }
}
