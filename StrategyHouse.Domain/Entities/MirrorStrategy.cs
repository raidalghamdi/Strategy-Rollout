using System.ComponentModel.DataAnnotations;

namespace StrategyHouse.Domain.Entities;

// Phase 19.5 — local SQLite mirror of the external MSSQL (Option A) strategy
// warehouse. Populated by the admin "push" action (IMssqlMirrorService) and read
// as a resilient fallback when the live MSSQL connection is unavailable.
//
// These tables are owned by the app's ApplicationDbContext (SQLite). Each row
// carries the warehouse's natural code in a *Code column plus a surrogate int PK
// so a full TRUNCATE+INSERT refresh is cheap. Column shapes follow the External*
// entities but use plain CLR types (SQLite has no fixed-width string/decimal).

public class MirrorPillar
{
    public int Id { get; set; }
    [MaxLength(15)] public string PlrCode { get; set; } = string.Empty;
    public string? PillarName { get; set; }
    public decimal? Budget { get; set; }
    public decimal? Liquidity { get; set; }
    public DateTime? StartDates { get; set; }
    public DateTime? EndDates { get; set; }
    public string? PlrPeriods { get; set; }
}

public class MirrorObjective
{
    public int Id { get; set; }
    [MaxLength(15)] public string ObjectiveCode { get; set; } = string.Empty;
    public string? ObjectiveName { get; set; }
    public string? PlrCode { get; set; }
    public decimal? Budget { get; set; }
    public decimal? Liquidity { get; set; }
    public DateTime? StartDates { get; set; }
    public DateTime? EndDates { get; set; }
    public string? ObjPeriod { get; set; }
}

public class MirrorKpi
{
    public int Id { get; set; }
    [MaxLength(50)] public string KpiCode { get; set; } = string.Empty;
    public string? KpiName { get; set; }
    public string? ActivationStatus { get; set; }
    public string? KpiType { get; set; }
    public string? ObjectiveCode { get; set; }
    public string? PlrCode { get; set; }
    public string? Division { get; set; }
    public string? Frequency { get; set; }
    public string? UnitDirection { get; set; }
    public string? IndexWeight { get; set; }
    public decimal? MinimumMaximum { get; set; }
    public string? Target2025 { get; set; }
    public string? Target2026 { get; set; }
    public string? Target2027 { get; set; }
    public string? Target2028 { get; set; }
    public string? Target2029 { get; set; }
    public string? Target2030 { get; set; }
    public string? AutomationStatus { get; set; }
}

public class MirrorInitiative
{
    public int Id { get; set; }
    [MaxLength(15)] public string InitiativeCode { get; set; } = string.Empty;
    public string? InitiativeName { get; set; }
    public string? ObjectiveCode { get; set; }
    public string? ObjectiveName { get; set; }
    public string? Owners { get; set; }
    public decimal? Budget { get; set; }
    public decimal? Liquidity { get; set; }
    public DateTime? StartDates { get; set; }
    public DateTime? EndDates { get; set; }
}

public class MirrorProject
{
    public int Id { get; set; }
    [MaxLength(15)] public string ProjectCode { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string? InitiativeCode { get; set; }
    public string? PlrCode { get; set; }
    public string? ProjectType { get; set; }
    public string? ProjectStatus { get; set; }
    public decimal? BudgetLiquidity { get; set; }
    public decimal? Liquidity2025 { get; set; }
    public decimal? Liquidity2026 { get; set; }
    public decimal? Liquidity2027 { get; set; }
    public decimal? Liquidity2028 { get; set; }
    public decimal? Liquidity2029 { get; set; }
    public decimal? Liquidity2030 { get; set; }
    public decimal? Liquidity2031 { get; set; }
    public decimal? GacBudget { get; set; }
    public string? ProjectSponsor { get; set; }
    public string? ProjectManager { get; set; }
    public string? Division { get; set; }
    public string? ProjectPhase { get; set; }
}

// Single-row table tracking the most recent push from MSSQL into the mirror.
public class MirrorMetadata
{
    public int Id { get; set; }
    public DateTime? LastPushAt { get; set; }
    public int RecordCount { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public double DurationSeconds { get; set; }
}
