using StrategyHouse.Domain.Enums;
using static StrategyHouse.Web.Services.SurveyAnalyticsService;

namespace StrategyHouse.Web.Models;

// Phase 12 — view models for the official survey analytics, categorisation and report.

public class QuestionCard
{
    public Guid QuestionId { get; set; }
    public int Order { get; set; }
    public string QuestionAr { get; set; } = "";
    public QuestionType Type { get; set; }
    public string Metric { get; set; } = "";
    public string Formula { get; set; } = "";

    public LikertResults? Likert { get; set; }
    public List<ChoiceResult>? Choices { get; set; }
    public OpenTextResults? OpenText { get; set; }
}

public class SurveyAnalyticsViewModel
{
    public Guid SurveyId { get; set; }
    public string SurveyTitle { get; set; } = "";
    public string PublicToken { get; set; } = "";
    public int TotalResponses { get; set; }
    public List<QuestionCard> Cards { get; set; } = new();
}

public record OpenTextQuestionLink(Guid QuestionId, int Order, string QuestionAr, int Total, int Uncategorized);

public class CategorizeViewModel
{
    public Guid QuestionId { get; set; }
    public int QuestionOrder { get; set; }
    public string QuestionAr { get; set; } = "";
    public List<string> Categories { get; set; } = new();
    public List<OpenTextVerbatim> Answers { get; set; } = new();
}

public class FinalReportViewModel
{
    public Guid SurveyId { get; set; }
    public string SurveyTitle { get; set; } = "";
    public int TotalResponses { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public List<string> Takeaways { get; set; } = new();
    public List<string> Insights { get; set; } = new();
    public List<QuestionCard> Cards { get; set; } = new();
    public Dictionary<Guid, string> Interpretations { get; set; } = new();
}
