using StrategyHouse.Domain.Enums;

namespace StrategyHouse.Domain.Entities;

/// <summary>
/// Configurable end-of-session survey. Strategy office authors questions through
/// the admin UI; the survey is rendered on attendees' phones via QR.
/// </summary>
public class Survey
{
    public int Id { get; set; }
    public string NameAr { get; set; } = "استبيان نهاية الجلسة";
    public string? IntroAr { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SurveyQuestion> Questions { get; set; } = new List<SurveyQuestion>();
}

public class SurveyQuestion
{
    public int Id { get; set; }
    public int SurveyId { get; set; }
    public Survey? Survey { get; set; }

    public string TextAr { get; set; } = string.Empty;
    public SurveyQuestionType Type { get; set; }
    public int Order { get; set; }
    public bool IsRequired { get; set; }
    public int? MinValue { get; set; }
    public int? MaxValue { get; set; }

    /// <summary>JSON array of options for single/multi choice questions.</summary>
    public string? OptionsJson { get; set; }
}

/// <summary>
/// An anonymous survey submission. Tagged only by session and department —
/// never by individual.
/// </summary>
public class SurveyResponse
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public Session? Session { get; set; }
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public int SurveyId { get; set; }
    public Survey? Survey { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SurveyAnswer> Answers { get; set; } = new List<SurveyAnswer>();
}

public class SurveyAnswer
{
    public int Id { get; set; }
    public int SurveyResponseId { get; set; }
    public SurveyResponse? SurveyResponse { get; set; }

    public int SurveyQuestionId { get; set; }
    public SurveyQuestion? SurveyQuestion { get; set; }

    public int? RatingValue { get; set; }
    public string? ChoiceValue { get; set; }
    public string? OpenText { get; set; }
}

/// <summary>
/// Baseline check (Movement 1 start): 3-question anonymous pre-session check.
/// </summary>
public class BaselineResponse
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public Session? Session { get; set; }
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public string? Q1_PillarNamed { get; set; }
    public string? Q2_ValueNamed { get; set; }
    public int? Q3_RoleAwarenessRating { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Optional post-session quiz. Sent in the same-day email; learning + retention.
/// Anonymous, aggregate-only.
/// </summary>
public class Quiz
{
    public int Id { get; set; }
    public string NameAr { get; set; } = "اختبار ما بعد الجلسة";
    public string? IntroAr { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<QuizQuestion> Questions { get; set; } = new List<QuizQuestion>();
}

public class QuizQuestion
{
    public int Id { get; set; }
    public int QuizId { get; set; }
    public Quiz? Quiz { get; set; }

    public string TextAr { get; set; } = string.Empty;
    public QuizQuestionType Type { get; set; }
    public int Order { get; set; }

    /// <summary>JSON array of options.</summary>
    public string OptionsJson { get; set; } = "[]";

    /// <summary>Correct option key(s), JSON array.</summary>
    public string CorrectOptionsJson { get; set; } = "[]";

    /// <summary>Short explanation shown after answer; reinforces learning.</summary>
    public string? FeedbackAr { get; set; }
}

public class QuizResponse
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public Session? Session { get; set; }
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public int QuizId { get; set; }
    public Quiz? Quiz { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public ICollection<QuizAnswer> Answers { get; set; } = new List<QuizAnswer>();
}

public class QuizAnswer
{
    public int Id { get; set; }
    public int QuizResponseId { get; set; }
    public QuizResponse? QuizResponse { get; set; }

    public int QuizQuestionId { get; set; }
    public QuizQuestion? QuizQuestion { get; set; }

    /// <summary>JSON array of selected option keys.</summary>
    public string SelectedOptionsJson { get; set; } = "[]";

    public bool IsCorrect { get; set; }
}
