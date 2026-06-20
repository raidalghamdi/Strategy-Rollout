using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Services.Dtos;

// Phase 19.23 — flat, storage-agnostic DTOs returned by IStrategyDataSource.
// Consumers depend on these, not on the SQLite/Mirror entity shapes, so the
// underlying source (MSSQL mirror → SQLite fallback) can change freely.

public record StrategyPillarDto(string Code, string Name, decimal? Budget, decimal? Liquidity);

public record StrategyObjectiveDto(string Code, string Name, string? PillarCode, decimal? Budget, decimal? Liquidity);

public record StrategyInitiativeDto(string Code, string Name, string? ObjectiveCode, string? Owners, decimal? Budget, decimal? Liquidity);

public record StrategyProjectDto(string Code, string Name, string? InitiativeCode, string? Division, string? Type, string? Status, decimal? Budget, decimal? Liquidity, decimal? GacBudget);

public record StrategyKpiDto(string Code, string Name, string? ObjectiveCode, string? Division, string? Type, bool Active);

public record StrategyCountsDto(int Pillars, int Objectives, int Initiatives, int Projects, int Kpis, StrategyDataSource Source);

public record StrategyDataSourceTrace(string Pillars, string Objectives, string Initiatives, string Projects, string Kpis);
