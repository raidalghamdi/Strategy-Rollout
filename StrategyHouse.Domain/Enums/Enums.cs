namespace StrategyHouse.Domain.Enums;

public enum LayerType
{
    Vision = 1,
    Mission = 2,
    Values = 3,
    Pillars = 4,
    Objectives = 5,
    Projects = 6,
    Kpis = 7,
    Custom = 99
}

public enum CommitmentLinkType
{
    Value = 1,
    Pillar = 2,
    Objective = 3,
    Project = 4
}

public enum SessionStatus
{
    Scheduled = 0,
    InProgress = 1,
    Completed = 2,
    Cancelled = 3
}

public enum MovementType
{
    BaselineCheck = 1,
    Map = 2,
    Commitment = 3,
    Survey = 4,
    Quiz = 5
}

public enum SurveyQuestionType
{
    Rating = 1,
    SingleChoice = 2,
    MultiChoice = 3,
    OpenText = 4
}

// Phase 12 — the three measurable question types of the official 8-question survey.
public enum QuestionType
{
    Likert5 = 1,
    MultipleChoice = 2,
    OpenText = 3
}

public enum QuizQuestionType
{
    SingleChoice = 1,
    MultiChoice = 2
}

public enum WallAccessLevel
{
    StrategyOfficeOnly = 0,
    SessionAttendees = 1,
    AllEmployees = 2
}

public enum UserRole
{
    Admin = 1,
    Facilitator = 2,
    Viewer = 3,
    // Phase 20.33 (Comment 8) — CX role: survey + executive report access, no DB import/export
    CX = 4
}
