using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StrategyHouse.Domain.Entities;

// Phase 9 — mini CMS. Admin-editable Arabic strings for user-facing pages, keyed by a
// dotted path (e.g. "home.hero.title"). Views read via the @Html.Content("key", default)
// helper, which caches in memory and falls back to the supplied default.
[Table("PageContents")]
public class PageContent
{
    [Key, MaxLength(120)] public string Key { get; set; } = string.Empty;
    [Column(TypeName = "longtext")] public string ValueAr { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
