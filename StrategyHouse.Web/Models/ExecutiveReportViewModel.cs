namespace StrategyHouse.Web.Models;

// Phase 13/14 — the comprehensive executive report aggregating the whole rollout event:
// sessions, attendees, quiz, survey, contributions, group signatures, maps — plus the
// Phase 14 leadership analytics (alignment, culture, risks, maturity, recommendations).
public class ExecutiveReportViewModel
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    // Phase 14 — which sections the caller asked for (HTML view + every export honour this).
    public ExecReportSections Sections { get; set; } = ExecReportSections.AllSelected();

    public ExecOverview Overview { get; set; } = new();
    public List<ExecDepartmentRow> DepartmentBreakdown { get; set; } = new();
    public ExecQuizAnalytics QuizAnalytics { get; set; } = new();
    public List<ExecSurveyMetric> SurveyMetrics { get; set; } = new();
    public ExecContributionsSummary Contributions { get; set; } = new();
    public ExecGroupSignaturesSummary GroupSignatures { get; set; } = new();
    public int MapsCount { get; set; }

    // Phase 14 — leadership analytics
    public ExecLeadershipAlignment LeadershipAlignment { get; set; } = new();
    public ExecLeadershipCulture LeadershipCulture { get; set; } = new();
    public ExecLeadershipRisks LeadershipRisks { get; set; } = new();
    public ExecLeadershipMaturity LeadershipMaturity { get; set; } = new();
    public List<string> LeadershipRecommendations { get; set; } = new();
}

public class ExecOverview
{
    public int TotalSessions { get; set; }
    public int TotalCompletedSessions { get; set; }
    public int TotalAttendees { get; set; }
    public int TotalDepartmentsEngaged { get; set; }
    public int TotalDepartments { get; set; }                  // Phase 14 — denominator (active depts)
    public double CompletionPercentage { get; set; }            // Phase 14 — completed / total sessions, 0..100
    public double AvgQuizScore { get; set; }                    // mean score out of 5
    public double AvgSurveyClarity { get; set; }                // Q1 Likert mean (1..5)
    public double AvgContributionCapability { get; set; }       // Q8 Likert mean (1..5)
    public int[] SurveyClarityDistribution { get; set; } = new int[5];
    public DateTime? SessionsFrom { get; set; }                 // Phase 14 — earliest session start
    public DateTime? SessionsTo { get; set; }                   // Phase 14 — latest session activity
    public List<string> NotEngagedDepartments { get; set; } = new(); // Phase 14 — names of depts with no session
}

public class ExecDepartmentRow
{
    public string DeptCode { get; set; } = "";
    public string DeptName { get; set; } = "";
    public int SessionsCount { get; set; }
    public int AttendeesCount { get; set; }
    public double CompletionRate { get; set; } // 0..100
    public int Rank { get; set; }              // Phase 14 — engagement trend rank (1 = most engaged)
}

public class ExecQuizAnalytics
{
    public int TotalAttempts { get; set; }
    public double AvgScore { get; set; }
    public int Bucket0to2 { get; set; }
    public int Bucket3to4 { get; set; }
    public int Bucket5 { get; set; }
    public List<ExecMissedQuestion> Top3MostMissed { get; set; } = new();
    public List<ExecMissedQuestion> Top3Strongest { get; set; } = new(); // Phase 14 — lowest miss rate
}

public class ExecMissedQuestion
{
    public string QuestionAr { get; set; } = "";
    public double MissRate { get; set; }   // 0..100 (correct rate = 100 - MissRate)
    public int Attempts { get; set; }
}

// One per official survey question, fully detailed (Phase 14).
public class ExecSurveyMetric
{
    public int Order { get; set; }
    public string QuestionAr { get; set; } = "";
    public string Type { get; set; } = "";
    public string Headline { get; set; } = "";
    // Likert (orders 1, 8)
    public double LikertMean { get; set; }
    public double LikertPctHigh { get; set; }
    public int[] LikertDistribution { get; set; } = new int[5];
    public int LikertTotal { get; set; }
    // Multiple choice
    public List<ExecChoiceShare> Choices { get; set; } = new();
    // Open text
    public int OpenTextTotal { get; set; }
    public int OpenTextUncategorized { get; set; }
    public List<ExecNameCount> OpenTextCategories { get; set; } = new();
}

public class ExecChoiceShare
{
    public string Text { get; set; } = "";
    public int Count { get; set; }
    public double Percent { get; set; }
}

public class ExecContributionsSummary
{
    public int TotalPledges { get; set; }
    public List<ExecNameCount> TopObjectives { get; set; } = new();
    public List<ExecNameCount> TopInitiatives { get; set; } = new();
    public List<ExecNameCount> ByDepartment { get; set; } = new(); // Phase 14 — heatmap data
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
    public List<ExecNameCount> TopKeywords { get; set; } = new(); // Phase 14 — word frequency
}

public class ExecRecentComment
{
    public string DeptName { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CapturedAt { get; set; }
}

// ----- Phase 14 leadership analytics -----

public class ExecLeadershipAlignment
{
    public int TotalContributions { get; set; }
    public List<ExecPillarShare> PillarShares { get; set; } = new();
    public List<string> Gaps { get; set; } = new();          // pillars receiving < 10%
    public List<string> Recommendations { get; set; } = new();
}

public class ExecPillarShare
{
    public string PillarCode { get; set; } = "";
    public string PillarName { get; set; } = "";
    public int Count { get; set; }
    public double Percent { get; set; } // 0..100
}

public class ExecLeadershipCulture
{
    public List<ExecDeptParticipation> DepartmentParticipation { get; set; } = new();
    public int PositiveComments { get; set; }
    public int NeutralComments { get; set; }
    public int NegativeComments { get; set; }
    public double TeamSpiritScore { get; set; } // composite 0..100
    public string TeamSpiritLabel { get; set; } = "";
}

public class ExecDeptParticipation
{
    public string DeptName { get; set; } = "";
    public int Attendees { get; set; }
    public double ParticipationRatio { get; set; }
    public bool RatioKnown { get; set; }
}

public class ExecLeadershipRisks
{
    public List<ExecCategorisedItem> TopChallenges { get; set; } = new();   // from Q4
    public List<ExecCategorisedItem> TopOpportunities { get; set; } = new(); // from Q7
    public List<ExecNameCount> RiskHeatmap { get; set; } = new();            // dept → challenge mentions
    public List<string> Recommendations { get; set; } = new();
}

public class ExecCategorisedItem
{
    public string Category { get; set; } = "";
    public int Count { get; set; }
    public double Percent { get; set; }
}

public class ExecLeadershipMaturity
{
    public List<ExecDeptMaturity> Departments { get; set; } = new();
    public int MatureCount { get; set; }       // ناضجة ≥ 4
    public int DevelopingCount { get; set; }   // متطورة 3–4
    public int NeedsSupportCount { get; set; } // بحاجة دعم < 3
    public List<string> Recommendations { get; set; } = new();
}

public class ExecDeptMaturity
{
    public string DeptName { get; set; } = "";
    public double Score { get; set; } // 0..5 composite
    public string Tier { get; set; } = ""; // ناضجة / متطورة / بحاجة دعم
}
