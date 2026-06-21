using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StrategyHouse.Domain.Entities;

// =====================================================================
// Phase 1 — Department Strategy Journey
// Access code → session → members → contribution pledges → strategy map
// → typed signatures → locked PDF. Ink/moderation tables are provisioned
// now (Phase 3) so we don't re-migrate later.
// No cascade deletes anywhere — all history is preserved.
// =====================================================================

// Admin-generated code that lets a department team enter the journey.
[Table("DepartmentAccessCodes")]
public class DepartmentAccessCode
{
    [Key, MaxLength(15)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(15)] public string DeptCode { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public int UsedCount { get; set; }
    public string? CreatedByUserId { get; set; }
}

// One per department journey instance (multiple sessions per dept allowed over time).
[Table("StrategySessions")]
public class StrategySession
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required, MaxLength(15)] public string DeptCode { get; set; } = string.Empty;
    [MaxLength(15)] public string? AccessCodeUsed { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? MembersSubmittedAt { get; set; } // Phase 7 — set when the team roster is first saved
    public DateTime? CompletedAt { get; set; }
    public DateTime? SignedAt { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "InProgress"; // InProgress / Signed / Locked
    public int CurrentStage { get; set; } = 1; // Phase 9 — furthest journey stage reached (1..5 since Phase 13), for anti-skip + live dashboard
    public DateTime? LastActivityAt { get; set; } // Phase 9 — bumped on each stage advance, for the live dashboard
    public int? AttendeeCount { get; set; } // Phase 13 — number of department employees present, entered by the team on the Map stage
    public int? OwnerUserId { get; set; } // Phase 20 — AppUser who created this session (used to scope/delete test data); no cascade
    public ICollection<SessionMember> Members { get; set; } = new List<SessionMember>();
}

// Team members participating in a session.
[Table("SessionMembers")]
public class SessionMember
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    [Required, MaxLength(255)] public string NameAr { get; set; } = string.Empty;
    [MaxLength(255)] public string? Role { get; set; }
    [MaxLength(255)] public string? TypedSignature { get; set; } // Phase 1 = typed only
    public DateTime? SignedAt { get; set; }

    [ForeignKey(nameof(SessionId))]
    public StrategySession? Session { get; set; }
}

// Stage 3 volunteer links — a department pledges to support a strategy element.
[Table("ContributionPledges")]
public class ContributionPledge
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    [Required, MaxLength(15)] public string DeptCode { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string ElementType { get; set; } = string.Empty; // OBJ / INIT / KPI / PRJ
    [Required, MaxLength(50)] public string ElementCode { get; set; } = string.Empty;
    [MaxLength(50)] public string? ContributionKind { get; set; } // تنفيذ مباشر / دعم فني / موارد بشرية / بيانات / استشاري
    [MaxLength(1000)] public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(SessionId))]
    public StrategySession? Session { get; set; }
}

// Phase 16 — the single Big Picture value a team feels best represents them,
// captured on the Stage 3 (Contribute) screen. One active selection per session.
[Table("TeamValueSelections")]
public class TeamValueSelection
{
    [Key] public int Id { get; set; }
    public Guid? SessionId { get; set; }
    [Required, MaxLength(20)] public string JourneyCode { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    [Required, MaxLength(80)] public string SelectedValueKey { get; set; } = string.Empty;
    [Required, MaxLength(255)] public string SelectedValueText { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(SessionId))]
    public StrategySession? Session { get; set; }
}

// The artifact — one per session. Typed text in Phase 1; ink assets in Phase 3.
[Table("DepartmentStrategyMaps")]
public class DepartmentStrategyMap
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    [Required, MaxLength(15)] public string DeptCode { get; set; } = string.Empty;
    [Column(TypeName = "longtext")] public string? MapLayoutJson { get; set; }
    [Column(TypeName = "longtext")] public string? OpinionsText { get; set; }
    [Column(TypeName = "longtext")] public string? CommitmentsText { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SignedAt { get; set; }
    [Column(TypeName = "longblob")] public byte[]? PdfBlob { get; set; } // generated print-ready PDF

    [ForeignKey(nameof(SessionId))]
    public StrategySession? Session { get; set; }
}

// Phase-3-ready table for any image asset attached to a map.
[Table("MapInkAssets")]
public class MapInkAsset
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MapId { get; set; }
    [Required, MaxLength(30)] public string AssetKind { get; set; } = string.Empty; // opinion / commitment / signature / supporting
    [Column(TypeName = "longblob")] public byte[]? PngBlob { get; set; }
    [Column(TypeName = "longtext")] public string? StrokesJson { get; set; } // Phase 3 raw strokes
    [MaxLength(2000)] public string? TypedText { get; set; } // Phase 10.1 — free-text alongside/instead of pen ink; widened to 2000 in Phase 13 for the group comment
    [MaxLength(255)] public string? AuthorName { get; set; }
    public Guid? MemberId { get; set; } // Phase 3 — links a signature asset to a SessionMember
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    [MaxLength(20)] public string ModerationStatus { get; set; } = "Pending"; // Pending / Approved / Rejected / Hidden
    public DateTime? ModeratedAt { get; set; }
    [MaxLength(450)] public string? ModeratedBy { get; set; }
    [MaxLength(1000)] public string? ModerationNote { get; set; }

    [ForeignKey(nameof(MapId))]
    public DepartmentStrategyMap? Map { get; set; }
}

// Audit trail for every moderation action.
[Table("ModerationAuditLogs")]
public class ModerationAuditLog
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required, MaxLength(50)] public string TargetType { get; set; } = string.Empty; // MapInkAsset / DepartmentStrategyMap
    public Guid TargetId { get; set; }
    [Required, MaxLength(20)] public string Action { get; set; } = string.Empty; // Approve / Reject / Hide / Reactivate
    [MaxLength(450)] public string? ActorUserId { get; set; }
    [MaxLength(1000)] public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// =====================================================================
// Phase 18 — Journey redesign capture tables.
// Two optional, free-text capture points in the redesigned 5-stage flow.
// No cascade deletes; rows are kept for analytics. Both store the journey
// (department) code as a plain string so they survive even if a session is
// pruned. Mapped to SQLite TEXT columns, same as the rest of the journey.
// =====================================================================

// Stage 1 (الصورة الكبرى) — the team's free-text answer to the opening
// reflection question. Optional; the journey continues whether or not it is set.
[Table("Phase18_OpeningReflections")]
public class OpeningReflection
{
    [Key] public int Id { get; set; }
    public Guid? SessionId { get; set; }
    [Required, MaxLength(20)] public string JourneyCode { get; set; } = string.Empty;
    [MaxLength(20)] public string? DepartmentCode { get; set; }
    [Column(TypeName = "longtext")] public string? ReflectionText { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Stage 3 (دوري في الاستراتيجية) — the employee's chosen initiative and the
// impact they perceive it contributes to. Optional free text.
[Table("Phase18_RoleContributions")]
public class RoleContribution
{
    [Key] public int Id { get; set; }
    public Guid? SessionId { get; set; }
    [Required, MaxLength(20)] public string JourneyCode { get; set; } = string.Empty;
    [MaxLength(20)] public string? DepartmentCode { get; set; }
    [MaxLength(50)] public string? SelectedInitiativeCode { get; set; }
    [Column(TypeName = "longtext")] public string? PerceivedImpact { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
