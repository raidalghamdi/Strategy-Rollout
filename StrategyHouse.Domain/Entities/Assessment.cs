using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using StrategyHouse.Domain.Enums;

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

    // Phase 12 — official survey measurement metadata.
    public QuestionType QuestionType { get; set; } = QuestionType.Likert5;
    [MaxLength(500)] public string? MeasurementMetric { get; set; }
    [Column(TypeName = "longtext")] public string? MeasurementFormula { get; set; }

    // Phase 20.35 — gate that controls whether this question's results show in FinalReport.
    // Admins must explicitly mark a question ready (after reviewing categorization) before it is
    // populated into the published final report. Defaults to true to preserve existing behaviour
    // for non-open-text questions; the auto-categorizer flips it to false for OpenText until a
    // human signs off.
    public bool ReadyForReport { get; set; } = true;
    public DateTime? ReadyForReportAt { get; set; }
    public int? ReadyForReportByUserId { get; set; }

    [ForeignKey(nameof(SurveyId))]
    public Survey? Survey { get; set; }

    // Phase 12 — predefined categories for open-text questions.
    public ICollection<SurveyQuestionCategory> Categories { get; set; } = new List<SurveyQuestionCategory>();
}

// Phase 12 — predefined categories an analyst can tag open-text answers with.
// Phase 20.35 — categories now carry their own keyword dictionary (JSON array of Arabic terms)
// so analysts can edit auto-categorisation rules from the admin UI instead of editing source code.
[Table("SurveyQuestionCategories")]
public class SurveyQuestionCategory
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SurveyQuestionId { get; set; }
    [MaxLength(120)] public string Name { get; set; } = "";
    public int Order { get; set; }

    // Phase 20.35 — admin-managed keyword list. Stored as JSON array of strings; an empty list
    // means the category is matched only by manual assignment. Existing rows back-fill to "[]".
    [Column(TypeName = "longtext")] public string KeywordsJson { get; set; } = "[]";

    // Active = participates in auto-categorisation and shows in dropdowns. Admins can deactivate
    // a category without losing its historical assignments.
    public bool IsActive { get; set; } = true;

    // Builtin = seeded by the platform (e.g., Q4 challenges, Q5 values, Q7 aspirations). Admins
    // can edit keywords on builtin rows but cannot delete them; non-builtin rows are fully
    // editable and deletable.
    public bool IsBuiltin { get; set; } = false;

    [MaxLength(500)] public string? DescriptionAr { get; set; }

    [ForeignKey(nameof(SurveyQuestionId))]
    public SurveyQuestion? SurveyQuestion { get; set; }
}

// Phase 12 — an analyst's category tag for one open-text answer cell.
// Answers are stored denormalised in SurveyResponse.AnswersJson keyed by question id,
// so an answer cell is uniquely identified by (SurveyResponseId, SurveyQuestionId).
[Table("OpenTextCategoryAssignments")]
public class OpenTextCategoryAssignment
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SurveyResponseId { get; set; }
    public Guid SurveyQuestionId { get; set; }
    [MaxLength(120)] public string Category { get; set; } = "";
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public int? AssignedByUserId { get; set; }

    [ForeignKey(nameof(SurveyResponseId))]
    public SurveyResponse? SurveyResponse { get; set; }
    [ForeignKey(nameof(SurveyQuestionId))]
    public SurveyQuestion? SurveyQuestion { get; set; }
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
