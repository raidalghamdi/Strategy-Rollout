using StrategyHouse.Domain.Enums;

namespace StrategyHouse.Domain.Entities;

/// <summary>
/// A Department Strategy Map produced during Movement 2 of a session.
/// One Map per department per session. Holds the placements, commitments,
/// and signatures. Becomes the printed artifact delivered later.
/// </summary>
public class StrategyMap
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public Session? Session { get; set; }
    public int DepartmentId { get; set; }
    public Department? Department { get; set; }

    public bool IsFinalized { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinalizedAt { get; set; }

    /// <summary>JSON snapshot of the canvas state for fast rendering / archival.</summary>
    public string? CanvasSnapshotJson { get; set; }

    public ICollection<MapPlacement> Placements { get; set; } = new List<MapPlacement>();
    public ICollection<MapCommitment> Commitments { get; set; } = new List<MapCommitment>();
    public ICollection<MapSignature> Signatures { get; set; } = new List<MapSignature>();
}

/// <summary>
/// A placement of a department project, KPI, or role onto a framework element.
/// Produced collaboratively in Movement 2.
/// </summary>
public class MapPlacement
{
    public int Id { get; set; }
    public int StrategyMapId { get; set; }
    public StrategyMap? StrategyMap { get; set; }

    public int FrameworkElementId { get; set; }
    public FrameworkElement? FrameworkElement { get; set; }

    /// <summary>One of: "project", "kpi", "role".</summary>
    public string PlacementKind { get; set; } = "project";

    public int? ProjectId { get; set; }
    public DepartmentProject? Project { get; set; }
    public int? KpiId { get; set; }
    public DepartmentKpi? Kpi { get; set; }
    public int? RoleId { get; set; }
    public DepartmentRole? Role { get; set; }

    /// <summary>Free-text label if the placement is bespoke (not from catalog).</summary>
    public string? CustomLabelAr { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A team commitment selected from the curated list, linked to a framework element.
/// </summary>
public class MapCommitment
{
    public int Id { get; set; }
    public int StrategyMapId { get; set; }
    public StrategyMap? StrategyMap { get; set; }

    public int? CommitmentTemplateId { get; set; }
    public CommitmentTemplate? CommitmentTemplate { get; set; }

    /// <summary>If team writes their own commitment instead of selecting.</summary>
    public string? CustomTextAr { get; set; }

    /// <summary>Snapshot of the selected commitment text at the time of selection.</summary>
    public string TextAr { get; set; } = string.Empty;

    public CommitmentLinkType LinkType { get; set; }
    public int LinkedElementId { get; set; }
    public FrameworkElement? LinkedElement { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A voluntary digital signature added to the completed Map.
/// </summary>
public class MapSignature
{
    public int Id { get; set; }
    public int StrategyMapId { get; set; }
    public StrategyMap? StrategyMap { get; set; }

    public string SignerNameAr { get; set; } = string.Empty;

    /// <summary>Base64-encoded PNG of the signature stroke.</summary>
    public string SignaturePngBase64 { get; set; } = string.Empty;

    public DateTime SignedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A curated commitment available for departments to select during Movement 3.
/// Scoped to a specific department; phrased as a concrete team action.
/// </summary>
public class CommitmentTemplate
{
    public int Id { get; set; }
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public string TextAr { get; set; } = string.Empty;
    public string? HintAr { get; set; }

    /// <summary>Suggested link type (team can change at link-time).</summary>
    public CommitmentLinkType? SuggestedLinkType { get; set; }

    /// <summary>Optional default linked element.</summary>
    public int? SuggestedElementId { get; set; }
    public FrameworkElement? SuggestedElement { get; set; }

    public int Order { get; set; }
    public bool IsActive { get; set; } = true;
}
