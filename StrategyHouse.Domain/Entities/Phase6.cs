using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StrategyHouse.Domain.Entities;

// =====================================================================
// Phase 6 — predefined department roster + chatbot conversation log.
// =====================================================================

// A predefined member of a department, surfaced as a checkbox in journey
// stage 1 (checked by default) and managed via /Admin/Roster.
[Table("DepartmentRoster")]
public class DepartmentRoster
{
    [Key] public Guid MemberId { get; set; } = Guid.NewGuid();
    [Required, MaxLength(15)] public string DeptCode { get; set; } = string.Empty;
    [Required, MaxLength(255)] public string NameAr { get; set; } = string.Empty;
    [MaxLength(255)] public string? Role { get; set; }
    public bool IsDefaultAttending { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Phase 20.29 — email-only access to the journey. Optional; when set, lets the
    // member sign in to the journey by typing their email (no password / no code).
    // Stored normalized (lower-cased + trimmed) for case-insensitive lookup.
    [MaxLength(320)] public string? Email { get; set; }
    [MaxLength(320)] public string? EmailNormalized { get; set; }
}

// Every chatbot question/answer, persisted for analytics.
[Table("ChatbotConversations")]
public class ChatbotConversation
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime AskedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(1000)] public string Question { get; set; } = string.Empty;
    [Column(TypeName = "longtext")] public string Answer { get; set; } = string.Empty;
    public Guid? SessionId { get; set; }
    [MaxLength(60)] public string? MatchedIntent { get; set; }
    public int ResultCount { get; set; }
}
