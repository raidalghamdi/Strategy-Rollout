using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StrategyHouse.Domain.Entities;

// =====================================================================
// Phase 20 — journey administration audit trail.
// Every privileged journey action (reset, test-data delete, user change)
// writes one immutable row here. No cascade; rows are kept forever.
// =====================================================================

[Table("JourneyAuditLog")]
public class JourneyAuditLog
{
    [Key] public int Id { get; set; }
    [MaxLength(255)] public string? Actor { get; set; }
    [MaxLength(50)] public string? ActionType { get; set; }   // JOURNEY_RESET / TEST_DELETE / USER_CREATE / ...
    [MaxLength(50)] public string? TargetType { get; set; }   // Department / Session / User
    [MaxLength(100)] public string? TargetId { get; set; }
    [Column(TypeName = "longtext")] public string? DetailsJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Phase 20 — record of a selective stage reset (which stages were cleared for a dept).
[Table("JourneyStageResets")]
public class JourneyStageReset
{
    [Key] public int Id { get; set; }
    [MaxLength(15)] public string? DeptCode { get; set; }
    public Guid? SessionId { get; set; }
    [MaxLength(50)] public string? StagesResetCsv { get; set; }
    [MaxLength(255)] public string? ResetBy { get; set; }
    public DateTime ResetAt { get; set; } = DateTime.UtcNow;
}
