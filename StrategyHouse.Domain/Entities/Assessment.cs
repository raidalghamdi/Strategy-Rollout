using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StrategyHouse.Domain.Entities;

// =====================================================================
// Phase 4 — Assessment: auto-generated quiz bank + programme surveys
// All quiz questions start unapproved; admin curates before they go live.
// Surveys are public/anonymous via a per-survey token.
// =====================================================================

[Table("QuizQuestions")]
public class QuizQuestion
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(20)] public string Scope { get; set; } = "General"; // General / Department
    [MaxLength(15)] public string? DeptCodeFilter { get; set; }
    [MaxLength(20)] public string QuestionType { get; set; } = "MCQ"; // MCQ / TrueFalse
    [MaxLength(500)] public string QuestionAr { get; set; } = "";
    [Column(TypeName = "longtext")] public string OptionsJson { get; set; } = "[]"; // JSON array of strings
    public int CorrectIndex { get; set; }
    [MaxLength(500)] public string? ExplanationAr { get; set; }
    public bool IsApproved { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(50)] public string Source { get; set; } = "AutoGen"; // AutoGen / Hand
}

[Table("QuizAttempts")]
public class QuizAttempt
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? SessionId { get; set; }
    public Guid? MemberId { get; set; }
    [MaxLength(255)] public string? RespondentName { get; set; }
    [MaxLength(20)] public string Scope { get; set; } = "General";
    [MaxLength(15)] public string? DeptCode { get; set; }
    public int Score { get; set; }
    public int Total { get; set; }
    [Column(TypeName = "longtext")] public string AnswersJson { get; set; } = "[]"; // [{qid, picked, correct}]
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}

[Table("Surveys")]
public class Survey
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(255)] public string TitleAr { get; set; } = "";
    [MaxLength(1000)] public string? DescriptionAr { get; set; }
    [MaxLength(50)] public string Audience { get; set; } = "Public"; // Public / Internal
    public DateTime? OpensAt { get; set; }
    public DateTime? ClosesAt { get; set; }
    public bool IsActive { get; set; } = true;
    [MaxLength(64)] public string PublicToken { get; set; } = ""; // for QR url
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<SurveyQuestion> Questions { get; set; } = new List<SurveyQuestion>();
}

[Table("SurveyQuestions")]
public class SurveyQuestion
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SurveyId { get; set; }
    public int Order { get; set; }
    [MaxLength(20)] public string Type { get; set; } = "Likert5"; // Likert5 / MCQ / Text / YesNo
    [MaxLength(500)] public string QuestionAr { get; set; } = "";
    [Column(TypeName = "longtext")] public string? OptionsJson { get; set; } // for MCQ
    public bool IsRequired { get; set; } = true;

    [ForeignKey(nameof(SurveyId))]
    public Survey? Survey { get; set; }
}

[Table("SurveyResponses")]
public class SurveyResponse
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SurveyId { get; set; }
    [MaxLength(255)] public string? RespondentName { get; set; }
    [MaxLength(50)] public string? RespondentRole { get; set; }
    [MaxLength(15)] public string? DeptCode { get; set; }
    [Column(TypeName = "longtext")] public string AnswersJson { get; set; } = "[]";
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(60)] public string? ClientFingerprint { get; set; } // optional anti-spam

    [ForeignKey(nameof(SurveyId))]
    public Survey? Survey { get; set; }
}
