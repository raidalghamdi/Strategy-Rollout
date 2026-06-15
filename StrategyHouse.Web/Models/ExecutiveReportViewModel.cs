namespace StrategyHouse.Web.Models;

// Phase 13 — the comprehensive executive report aggregating the whole rollout event:
// sessions, attendees, quiz, survey, contributions, group signatures and maps.
public class ExecutiveReportViewModel
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    public ExecOverview Overview { get; set; } = new();
    public List<ExecDepartmentRow> DepartmentBreakdown { get; set; } = new();
    public ExecQuizAnalytics QuizAnalytics { get; set; } = new();
    public List<ExecSurveyMetric> SurveyMetrics { get; set; } = new();
    public ExecContributionsSummary Contributions { get; set; } = new();
    public ExecGroupSignaturesSummary GroupSignatures { get; set; } = new();
    public int MapsCount { get; set; }
}

public class ExecOverview
{
    public int TotalSessions { get; set; }
    public int TotalCompletedSessions { get; set; }
    public int TotalAttendees { get; set; }
    public int TotalDepartmentsEngaged { get; set; }
    public double AvgQuizScore { get; set; }      // mean score out of the attempt total
    public double AvgSurveyClarity { get; set; }   // Q1 Likert mean (1..5)
    public double AvgContributionCapability { get; set; } // Q8 Likert mean (1..5)
    public int[] SurveyClarityDistribution { get; set; } = new int[5]; // Q1 Likert distribution (score 1..5)
}

public class ExecDepartmentRow
{
    public string DeptCode { get; set; } = "";
    public string DeptName { get; set; } = "";
    public int SessionsCount { get; set; }
    public int AttendeesCount { get; set; }
    public double CompletionRate { get; set; } // 0..100
}

public class ExecQuizAnalytics
{
    public int TotalAttempts { get; set; }
    public double AvgScore { get; set; }
    // Buckets over the 0-5 scale used by the demo quiz (10-question attempts are scaled by ratio elsewhere).
    public int Bucket0to2 { get; set; }
    public int Bucket3to4 { get; set; }
    public int Bucket5 { get; set; }
    public List<ExecMissedQuestion> Top3MostMissed { get; set; } = new();
}

public class ExecMissedQuestion
{
    public string QuestionAr { get; set; } = "";
    public double MissRate { get; set; } // 0..100
    public int Attempts { get; set; }
}

// One per official survey question we surface in the report (Likert mean / top choice / top category).
public class ExecSurveyMetric
{
    public int Order { get; set; }
    public string QuestionAr { get; set; } = "";
    public string Type { get; set; } = "";       // ليكرت / اختيار / نص
    public string Headline { get; set; } = "";    // human-readable key value
}

public class ExecContributionsSummary
{
    public int TotalPledges { get; set; }
    public List<ExecNameCount> TopObjectives { get; set; } = new();
    public List<ExecNameCount> TopInitiatives { get; set; } = new();
}

public class ExecNameCount
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

public class ExecGroupSignaturesSummary
{
    public int TotalCount { get; set; }
    public List<ExecRecentComment> RecentComments { get; set; } = new();
}

public class ExecRecentComment
{
    public string DeptName { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CapturedAt { get; set; }
}
