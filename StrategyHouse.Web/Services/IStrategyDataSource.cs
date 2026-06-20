using StrategyHouse.Web.Services.Dtos;

namespace StrategyHouse.Web.Services;

// Phase 19.23 — the single entry point for all strategy-data reads.
// Every method resolves MSSQL mirror first, then SQLite, then empty. No dummy data.
public interface IStrategyDataSource
{
    Task<IReadOnlyList<StrategyPillarDto>> GetPillarsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<StrategyObjectiveDto>> GetObjectivesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<StrategyInitiativeDto>> GetInitiativesAsync(string? deptCode = null, CancellationToken ct = default);
    Task<IReadOnlyList<StrategyProjectDto>> GetProjectsAsync(string? deptCode = null, CancellationToken ct = default);
    Task<IReadOnlyList<StrategyKpiDto>> GetKpisAsync(string? deptCode = null, CancellationToken ct = default);
    Task<StrategyCountsDto> GetCountsAsync(CancellationToken ct = default);
    Task<StrategyDataSourceTrace> GetLastTraceAsync();
}
