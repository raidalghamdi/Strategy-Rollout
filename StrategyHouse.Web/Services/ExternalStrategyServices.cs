using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StrategyHouse.Domain.Entities.External;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 17 — read services for the Option A strategy tables on the external MSSQL
// warehouse. All five share the same contract:
//   * UseExternalDb=true + ExternalDbContext registered → query the warehouse.
//   * Otherwise (dev / empty connection string) → return an empty list and log a
//     warning. Callers must treat an empty result as "no strategy data available"
//     and render the appropriate placeholder, never crash.
// The external context is read-only; nothing here writes.

public abstract class ExternalReadServiceBase
{
    protected readonly ExternalDbContext? External;
    protected readonly bool UseExternal;
    private readonly ILogger _log;
    private readonly string _name;

    protected ExternalReadServiceBase(IConfiguration config, ILogger log, string name, ExternalDbContext? external)
    {
        External = external;
        _log = log;
        _name = name;
        UseExternal = config.GetValue<bool>("Features:UseExternalDb") && external != null;
    }

    public bool Available => UseExternal && External != null;

    protected List<T> Empty<T>()
    {
        _log.LogWarning("{Service}: external DB unavailable (UseExternalDb off or no connection); returning empty list.", _name);
        return new List<T>();
    }
}

public class PillarsService : ExternalReadServiceBase
{
    public PillarsService(IConfiguration config, ILogger<PillarsService> log, ExternalDbContext? external = null)
        : base(config, log, nameof(PillarsService), external) { }

    public async Task<List<ExternalPillar>> GetAllAsync()
        => Available ? await External!.Pillars.AsNoTracking().OrderBy(p => p.PlrCode).ToListAsync() : Empty<ExternalPillar>();

    public async Task<ExternalPillar?> GetByCodeAsync(string code)
        => Available ? await External!.Pillars.AsNoTracking().FirstOrDefaultAsync(p => p.PlrCode == code) : null;
}

public class ObjectivesService : ExternalReadServiceBase
{
    public ObjectivesService(IConfiguration config, ILogger<ObjectivesService> log, ExternalDbContext? external = null)
        : base(config, log, nameof(ObjectivesService), external) { }

    public async Task<List<ExternalObjective>> GetAllAsync()
        => Available ? await External!.Objectives.AsNoTracking().OrderBy(o => o.ObjectiveCode).ToListAsync() : Empty<ExternalObjective>();

    public async Task<ExternalObjective?> GetByCodeAsync(string code)
        => Available ? await External!.Objectives.AsNoTracking().FirstOrDefaultAsync(o => o.ObjectiveCode == code) : null;

    public async Task<List<ExternalObjective>> GetByPillarAsync(string plrCode)
        => Available
            ? await External!.Objectives.AsNoTracking().Where(o => o.PlrCode == plrCode).OrderBy(o => o.ObjectiveCode).ToListAsync()
            : Empty<ExternalObjective>();
}

public class KpisService : ExternalReadServiceBase
{
    public KpisService(IConfiguration config, ILogger<KpisService> log, ExternalDbContext? external = null)
        : base(config, log, nameof(KpisService), external) { }

    public async Task<List<ExternalKpi>> GetAllAsync()
        => Available ? await External!.Kpis.AsNoTracking().OrderBy(k => k.KpiCode).ToListAsync() : Empty<ExternalKpi>();

    public async Task<ExternalKpi?> GetByCodeAsync(string code)
        => Available ? await External!.Kpis.AsNoTracking().FirstOrDefaultAsync(k => k.KpiCode == code) : null;

    public async Task<List<ExternalKpi>> GetByObjectiveAsync(string objectiveCode)
        => Available
            ? await External!.Kpis.AsNoTracking().Where(k => k.ObjectiveCode == objectiveCode).OrderBy(k => k.KpiCode).ToListAsync()
            : Empty<ExternalKpi>();

    public async Task<List<ExternalKpi>> GetByDivisionAsync(string division)
        => Available
            ? await External!.Kpis.AsNoTracking().Where(k => k.Division == division).OrderBy(k => k.KpiCode).ToListAsync()
            : Empty<ExternalKpi>();

    public async Task<List<ExternalKpi>> GetActiveAsync()
        => Available
            ? await External!.Kpis.AsNoTracking().Where(k => k.ActivationStatus == "Active").OrderBy(k => k.KpiCode).ToListAsync()
            : Empty<ExternalKpi>();
}

public class InitiativesService : ExternalReadServiceBase
{
    public InitiativesService(IConfiguration config, ILogger<InitiativesService> log, ExternalDbContext? external = null)
        : base(config, log, nameof(InitiativesService), external) { }

    public async Task<List<ExternalInitiative>> GetAllAsync()
        => Available ? await External!.Initiatives.AsNoTracking().OrderBy(i => i.InitiativeCode).ToListAsync() : Empty<ExternalInitiative>();

    public async Task<List<ExternalInitiative>> GetByObjectiveAsync(string objectiveCode)
        => Available
            ? await External!.Initiatives.AsNoTracking().Where(i => i.ObjectiveCode == objectiveCode).OrderBy(i => i.InitiativeCode).ToListAsync()
            : Empty<ExternalInitiative>();
}

public class ProjectsService : ExternalReadServiceBase
{
    public ProjectsService(IConfiguration config, ILogger<ProjectsService> log, ExternalDbContext? external = null)
        : base(config, log, nameof(ProjectsService), external) { }

    public async Task<List<ExternalProject>> GetAllAsync()
        => Available ? await External!.Projects.AsNoTracking().OrderBy(p => p.ProjectCode).ToListAsync() : Empty<ExternalProject>();

    public async Task<List<ExternalProject>> GetByInitiativeAsync(string initiativeCode)
        => Available
            ? await External!.Projects.AsNoTracking().Where(p => p.InitiativeCode == initiativeCode).OrderBy(p => p.ProjectCode).ToListAsync()
            : Empty<ExternalProject>();

    public async Task<List<ExternalProject>> GetByPillarAsync(string plrCode)
        => Available
            ? await External!.Projects.AsNoTracking().Where(p => p.PlrCode == plrCode).OrderBy(p => p.ProjectCode).ToListAsync()
            : Empty<ExternalProject>();

    public async Task<List<ExternalProject>> GetByDivisionAsync(string division)
        => Available
            ? await External!.Projects.AsNoTracking().Where(p => p.Division == division).OrderBy(p => p.ProjectCode).ToListAsync()
            : Empty<ExternalProject>();
}
