using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services.Dtos;

namespace StrategyHouse.Web.Services;

// Phase 19.23 — the one place strategy data is read. Resolution order for every
// entity: MSSQL mirror (Mirror* SQLite tables, fed by the admin push) → canonical
// SQLite tables → empty. No hardcoded/dummy data ever. Each read records which
// source it used so /Admin/DataSources can show operators what is live.
//
// Department filtering bridges SQLite Departments (DeptCode) to MSSQL's free-text
// Division field through a PageContent JSON map: { "DeptCode": ["Division A", ...] }.
// Mirror rows are filtered by Division IN (mapped names); SQLite rows by DepartmentCode.
public class UnifiedStrategyDataSource : IStrategyDataSource
{
    public const string DivisionMapKey = "department.divisions.json";

    private readonly ApplicationDbContext _db;
    private readonly PageContentService _content;
    private readonly ILogger<UnifiedStrategyDataSource> _log;

    private string _tracePillars = "—";
    private string _traceObjectives = "—";
    private string _traceInitiatives = "—";
    private string _traceProjects = "—";
    private string _traceKpis = "—";

    public UnifiedStrategyDataSource(
        ApplicationDbContext db,
        PageContentService content,
        ILogger<UnifiedStrategyDataSource> log)
    {
        _db = db;
        _content = content;
        _log = log;
    }

    public async Task<IReadOnlyList<StrategyPillarDto>> GetPillarsAsync(CancellationToken ct = default)
    {
        var mirror = await _db.MirrorPillars.AsNoTracking().ToListAsync(ct);
        if (mirror.Count > 0)
        {
            _tracePillars = nameof(StrategyDataSource.Mirror);
            return mirror.Select(p => new StrategyPillarDto(p.PlrCode, p.PillarName ?? p.PlrCode, p.Budget, p.Liquidity)).ToList();
        }

        var sqlite = await _db.Pillars.AsNoTracking().ToListAsync(ct);
        if (sqlite.Count > 0)
        {
            _tracePillars = nameof(StrategyDataSource.Sqlite);
            return sqlite.Select(p => new StrategyPillarDto(p.PlrCode, p.PillarName ?? p.PlrCode, p.Budget, p.Liquidity)).ToList();
        }

        _tracePillars = nameof(StrategyDataSource.Empty);
        return Array.Empty<StrategyPillarDto>();
    }

    public async Task<IReadOnlyList<StrategyObjectiveDto>> GetObjectivesAsync(CancellationToken ct = default)
    {
        var mirror = await _db.MirrorObjectives.AsNoTracking().ToListAsync(ct);
        if (mirror.Count > 0)
        {
            _traceObjectives = nameof(StrategyDataSource.Mirror);
            return mirror.Select(o => new StrategyObjectiveDto(o.ObjectiveCode, o.ObjectiveName ?? o.ObjectiveCode, o.PlrCode, o.Budget, o.Liquidity)).ToList();
        }

        var sqlite = await _db.Objectives.AsNoTracking().ToListAsync(ct);
        if (sqlite.Count > 0)
        {
            _traceObjectives = nameof(StrategyDataSource.Sqlite);
            return sqlite.Select(o => new StrategyObjectiveDto(o.ObjectiveCode, o.ObjectiveName ?? o.ObjectiveCode, o.PlrCode, o.Budget, o.Liquidity)).ToList();
        }

        _traceObjectives = nameof(StrategyDataSource.Empty);
        return Array.Empty<StrategyObjectiveDto>();
    }

    public async Task<IReadOnlyList<StrategyInitiativeDto>> GetInitiativesAsync(string? deptCode = null, CancellationToken ct = default)
    {
        // Initiatives carry no Division/DepartmentCode, so deptCode does not filter them.
        var mirror = await _db.MirrorInitiatives.AsNoTracking().ToListAsync(ct);
        if (mirror.Count > 0)
        {
            _traceInitiatives = nameof(StrategyDataSource.Mirror);
            return mirror.Select(i => new StrategyInitiativeDto(i.InitiativeCode, i.InitiativeName ?? i.InitiativeCode, i.ObjectiveCode, i.Owners, i.Budget, i.Liquidity)).ToList();
        }

        var sqlite = await _db.Initiatives.AsNoTracking().ToListAsync(ct);
        if (sqlite.Count > 0)
        {
            _traceInitiatives = nameof(StrategyDataSource.Sqlite);
            return sqlite.Select(i => new StrategyInitiativeDto(i.InitiativeCode, i.InitiativeName ?? i.InitiativeCode, i.ObjectiveCode, i.Owners, i.Budget, i.Liquidity)).ToList();
        }

        _traceInitiatives = nameof(StrategyDataSource.Empty);
        return Array.Empty<StrategyInitiativeDto>();
    }

    public async Task<IReadOnlyList<StrategyProjectDto>> GetProjectsAsync(string? deptCode = null, CancellationToken ct = default)
    {
        var divisions = string.IsNullOrWhiteSpace(deptCode) ? null : DivisionsForDept(deptCode);

        var mirrorQuery = _db.MirrorProjects.AsNoTracking().AsQueryable();
        if (divisions != null)
            mirrorQuery = mirrorQuery.Where(p => p.Division != null && divisions.Contains(p.Division));
        var anyMirror = await _db.MirrorProjects.AsNoTracking().AnyAsync(ct);
        if (anyMirror)
        {
            _traceProjects = nameof(StrategyDataSource.Mirror);
            var rows = await mirrorQuery.ToListAsync(ct);
            return rows.Select(p => new StrategyProjectDto(
                p.ProjectCode, p.ProjectName ?? p.ProjectCode, p.InitiativeCode, p.Division,
                p.ProjectType, p.ProjectStatus, p.BudgetLiquidity, null, p.GacBudget)).ToList();
        }

        var sqliteQuery = _db.Projects.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(deptCode))
            sqliteQuery = sqliteQuery.Where(p => p.DepartmentCode == deptCode);
        var anySqlite = await _db.Projects.AsNoTracking().AnyAsync(ct);
        if (anySqlite)
        {
            _traceProjects = nameof(StrategyDataSource.Sqlite);
            var rows = await sqliteQuery.ToListAsync(ct);
            return rows.Select(p => new StrategyProjectDto(
                p.ProjectCode, p.ProjectName ?? p.ProjectCode, p.InitiativeCode, p.Division,
                p.ProjectType, p.ProjectStatus, p.Budget, p.Liquidity, p.GacBudget)).ToList();
        }

        _traceProjects = nameof(StrategyDataSource.Empty);
        return Array.Empty<StrategyProjectDto>();
    }

    public async Task<IReadOnlyList<StrategyKpiDto>> GetKpisAsync(string? deptCode = null, CancellationToken ct = default)
    {
        var divisions = string.IsNullOrWhiteSpace(deptCode) ? null : DivisionsForDept(deptCode);

        var mirrorQuery = _db.MirrorKpis.AsNoTracking().AsQueryable();
        if (divisions != null)
            mirrorQuery = mirrorQuery.Where(k => k.Division != null && divisions.Contains(k.Division));
        var anyMirror = await _db.MirrorKpis.AsNoTracking().AnyAsync(ct);
        if (anyMirror)
        {
            _traceKpis = nameof(StrategyDataSource.Mirror);
            var rows = await mirrorQuery.ToListAsync(ct);
            return rows.Select(k => new StrategyKpiDto(
                k.KpiCode, k.KpiName ?? k.KpiCode, k.ObjectiveCode, k.Division, k.KpiType,
                string.Equals(k.ActivationStatus, "Active", StringComparison.OrdinalIgnoreCase))).ToList();
        }

        var sqliteQuery = _db.Kpis.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(deptCode))
            sqliteQuery = sqliteQuery.Where(k => k.DepartmentCode == deptCode);
        var anySqlite = await _db.Kpis.AsNoTracking().AnyAsync(ct);
        if (anySqlite)
        {
            _traceKpis = nameof(StrategyDataSource.Sqlite);
            var rows = await sqliteQuery.ToListAsync(ct);
            return rows.Select(k => new StrategyKpiDto(
                k.KpiCode, k.KpiName ?? k.KpiCode, k.ObjectiveCode, k.Division, k.KpiType,
                string.Equals(k.ActivationStatus, "Active", StringComparison.OrdinalIgnoreCase))).ToList();
        }

        _traceKpis = nameof(StrategyDataSource.Empty);
        return Array.Empty<StrategyKpiDto>();
    }

    public async Task<StrategyCountsDto> GetCountsAsync(CancellationToken ct = default)
    {
        var pillars = await GetPillarsAsync(ct);
        var objectives = await GetObjectivesAsync(ct);
        var initiatives = await GetInitiativesAsync(null, ct);
        var projects = await GetProjectsAsync(null, ct);
        var kpis = await GetKpisAsync(null, ct);

        // Overall source = the strongest source actually used (Mirror > Sqlite > Empty).
        var source = StrategyDataSource.Empty;
        var traces = new[] { _tracePillars, _traceObjectives, _traceInitiatives, _traceProjects, _traceKpis };
        if (traces.Any(t => t == nameof(StrategyDataSource.Mirror))) source = StrategyDataSource.Mirror;
        else if (traces.Any(t => t == nameof(StrategyDataSource.Sqlite))) source = StrategyDataSource.Sqlite;

        return new StrategyCountsDto(pillars.Count, objectives.Count, initiatives.Count, projects.Count, kpis.Count, source);
    }

    public Task<StrategyDataSourceTrace> GetLastTraceAsync() =>
        Task.FromResult(new StrategyDataSourceTrace(_tracePillars, _traceObjectives, _traceInitiatives, _traceProjects, _traceKpis));

    // ---- Division bridge -------------------------------------------------

    private List<string> DivisionsForDept(string deptCode)
    {
        var map = ReadDivisionMap();
        return map.TryGetValue(deptCode, out var names) ? names : new List<string>();
    }

    private Dictionary<string, List<string>> ReadDivisionMap()
    {
        var json = _content.Get(DivisionMapKey, "");
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
            return parsed != null
                ? new Dictionary<string, List<string>>(parsed, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to parse {Key}; treating division map as empty.", DivisionMapKey);
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
