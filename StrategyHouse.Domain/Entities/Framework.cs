using StrategyHouse.Domain.Enums;

namespace StrategyHouse.Domain.Entities;

/// <summary>
/// A strategic framework (e.g., the GAC Strategy House). Configurable structure,
/// not hardcoded. Enables future strategies to use different shapes (tree, wheel,
/// roadmap, etc.) without code changes.
/// </summary>
public class Framework
{
    public int Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }

    /// <summary>Visual shape identifier (e.g., "house", "tree", "wheel"). Drives the renderer.</summary>
    public string Shape { get; set; } = "house";

    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<FrameworkLayer> Layers { get; set; } = new List<FrameworkLayer>();
}

/// <summary>
/// A layer within the framework — e.g., Vision, Mission, Values, Pillars, Objectives.
/// Order matters: layers render top-to-bottom or center-out depending on Shape.
/// </summary>
public class FrameworkLayer
{
    public int Id { get; set; }
    public int FrameworkId { get; set; }
    public Framework? Framework { get; set; }

    public LayerType Type { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public int Order { get; set; }

    /// <summary>Optional visual hints for the renderer (color, icon, role).</summary>
    public string? VisualKey { get; set; }

    public ICollection<FrameworkElement> Elements { get; set; } = new List<FrameworkElement>();
}

/// <summary>
/// An individual element within a layer — e.g., a single pillar, value, or objective.
/// </summary>
public class FrameworkElement
{
    public int Id { get; set; }
    public int LayerId { get; set; }
    public FrameworkLayer? Layer { get; set; }

    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }

    /// <summary>For pillars, references the objectives under it via ParentElementId.</summary>
    public int? ParentElementId { get; set; }
    public FrameworkElement? ParentElement { get; set; }

    public int Order { get; set; }
    public string? IconKey { get; set; }
    public string? ColorHex { get; set; }
}
