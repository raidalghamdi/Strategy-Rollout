using StrategyHouse.Domain.Enums;

namespace StrategyHouse.Domain.Entities;

/// <summary>
/// A 90-minute rollout session. 8 sessions across 4 days, each containing 1-3
/// departments. The session is the unit around which all rollout data is organized.
/// </summary>
public class Session
{
    public int Id { get; set; }
    public int FrameworkId { get; set; }
    public Framework? Framework { get; set; }

    public string TitleAr { get; set; } = string.Empty;
    public DateTime ScheduledAt { get; set; }
    public string? VenueAr { get; set; }
    public string? LeadFacilitator { get; set; }
    public string? CoFacilitator { get; set; }
    public SessionStatus Status { get; set; }

    /// <summary>Public access code (e.g., for the in-room QR / baseline / survey).</summary>
    public string AccessCode { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SessionDepartment> SessionDepartments { get; set; } = new List<SessionDepartment>();
    public ICollection<SessionAttendee> Attendees { get; set; } = new List<SessionAttendee>();
    public ICollection<BaselineResponse> BaselineResponses { get; set; } = new List<BaselineResponse>();
    public ICollection<SurveyResponse> SurveyResponses { get; set; } = new List<SurveyResponse>();
    public ICollection<QuizResponse> QuizResponses { get; set; } = new List<QuizResponse>();
    public ICollection<StrategyMap> Maps { get; set; } = new List<StrategyMap>();
}

/// <summary>
/// Many-to-many: which departments attend this session.
/// </summary>
public class SessionDepartment
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public Session? Session { get; set; }
    public int DepartmentId { get; set; }
    public Department? Department { get; set; }
}

/// <summary>
/// An individual attendee. Anonymity is preserved in surveys/quiz; this entity
/// only exists to drive same-day email delivery and attendance counts.
/// </summary>
public class SessionAttendee
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public Session? Session { get; set; }
    public int DepartmentId { get; set; }
    public Department? Department { get; set; }

    public string FullNameAr { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool Attended { get; set; }
    public bool IsDepartmentHead { get; set; }
}
