using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 19.5 — single source of truth for strategy data (pillars → objectives →
// initiatives → projects + KPIs) used by views and the Sankey API. Resolves data
// through a resilient priority chain so the UI never breaks and hardcoded strategy
// text is eliminated:
//   1. Live external MSSQL (ExternalDbContext) — when reachable within a short timeout.
//   2. Local SQLite Mirror_* tables — populated by the admin push button.
//   3. Hardcoded 3×3 dummy — last resort, flagged so the UI can warn the operator.
// Survey/quiz/journey-CMS text is out of scope and untouched.

public enum StrategyDataSource { Live, Mirror, Dummy }

public class StrategyNode
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ParentCode { get; set; }
}

public class StrategyDataSet
{
    public StrategyDataSource Source { get; set; }
    public List<StrategyNode> Pillars { get; set; } = new();
    public List<StrategyNode> Objectives { get; set; } = new();
    public List<StrategyNode> Initiatives { get; set; } = new();
    public List<StrategyNode> Projects { get; set; } = new();
    public bool IsDummy => Source == StrategyDataSource.Dummy;
}

public interface IStrategyDataProvider
{
    Task<StrategyDataSet> GetStrategyAsync(CancellationToken ct = default);
    Task<object> GetSankeyDataAsync(CancellationToken ct = default);
}

public class StrategyDataProvider : IStrategyDataProvider
{
    private const string DummyWarning = "البيانات تجريبية — يرجى الضغط على زر دفع البيانات في صفحة الإدارة.";
    private static readonly TimeSpan LiveTimeout = TimeSpan.FromSeconds(3);

    private readonly ApplicationDbContext _db;
    private readonly ExternalDbContext? _external;
    private readonly IConfiguration _config;
    private readonly ILogger<StrategyDataProvider> _log;
    private readonly StrategyContentService? _content;

    public StrategyDataProvider(
        ApplicationDbContext db,
        IConfiguration config,
        ILogger<StrategyDataProvider> log,
        ExternalDbContext? external = null,
        StrategyContentService? content = null)
    {
        _db = db;
        _config = config;
        _log = log;
        _external = external;
        _content = content;
    }

    public async Task<StrategyDataSet> GetStrategyAsync(CancellationToken ct = default)
    {
        var live = await TryLiveAsync(ct);
        if (live != null) return live;

        var mirror = await TryMirrorAsync(ct);
        if (mirror != null) return mirror;

        return BuildDummy();
    }

    public async Task<object> GetSankeyDataAsync(CancellationToken ct = default)
    {
        var data = await GetStrategyAsync(ct);

        var nodes = new List<object>();
        var seen = new HashSet<string>();
        var links = new List<object>();

        void AddNode(string name, string category)
        {
            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name)) return;
            nodes.Add(new { name, category });
        }
        void AddLink(string? source, string? target)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target) || source == target) return;
            links.Add(new { source, target, value = 1 });
        }

        var pillarByCode = data.Pillars.ToDictionary(p => p.Code, p => p.Name, StringComparer.Ordinal);
        var objByCode = data.Objectives.ToDictionary(o => o.Code, o => o, StringComparer.Ordinal);
        var initByCode = data.Initiatives.ToDictionary(i => i.Code, i => i, StringComparer.Ordinal);

        // Phase 19.13 — add the Vision as the left-most root node so the flow
        // reads end-to-end: Vision → Pillars → Objectives → Initiatives → Projects.
        // Falls back to a generic label when the content service isn't wired.
        var visionLabel = _content?.Vision.Ar;
        if (string.IsNullOrWhiteSpace(visionLabel)) visionLabel = "الرؤية";
        AddNode(visionLabel, "vision");

        foreach (var p in data.Pillars)
        {
            AddNode(p.Name, "pillar");
            AddLink(visionLabel, p.Name);
        }
        foreach (var o in data.Objectives)
        {
            AddNode(o.Name, "objective");
            if (o.ParentCode != null && pillarByCode.TryGetValue(o.ParentCode, out var pName)) AddLink(pName, o.Name);
        }
        foreach (var i in data.Initiatives)
        {
            AddNode(i.Name, "initiative");
            if (i.ParentCode != null && objByCode.TryGetValue(i.ParentCode, out var o)) AddLink(o.Name, i.Name);
        }
        foreach (var pr in data.Projects)
        {
            AddNode(pr.Name, "project");
            if (pr.ParentCode != null && initByCode.TryGetValue(pr.ParentCode, out var i)) AddLink(i.Name, pr.Name);
        }

        return new
        {
            ok = true,
            live = data.Source == StrategyDataSource.Live,
            source = data.Source.ToString().ToLowerInvariant(),
            dummy = data.IsDummy,
            warning = data.IsDummy ? DummyWarning : null,
            nodes,
            links,
        };
    }

    private async Task<StrategyDataSet?> TryLiveAsync(CancellationToken ct)
    {
        var useExternal = _config.GetValue<bool>("Features:UseExternalDb");
        if (!useExternal || _external == null) return null;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(LiveTimeout);
            var token = cts.Token;

            if (!await _external.Database.CanConnectAsync(token)) return null;

            var pillars = await _external.Pillars.AsNoTracking().ToListAsync(token);
            var objectives = await _external.Objectives.AsNoTracking().ToListAsync(token);
            var initiatives = await _external.Initiatives.AsNoTracking().ToListAsync(token);
            var projects = await _external.Projects.AsNoTracking().ToListAsync(token);

            // Phase 19.8 — if the live source has no pillars, treat it as "not live"
            // and fall back to the mirror rather than returning empty live data.
            if (pillars.Count == 0) return null;

            return new StrategyDataSet
            {
                Source = StrategyDataSource.Live,
                Pillars = pillars.Select(p => new StrategyNode { Code = p.PlrCode, Name = p.PillarName ?? p.PlrCode }).ToList(),
                Objectives = objectives.Select(o => new StrategyNode { Code = o.ObjectiveCode, Name = o.ObjectiveName ?? o.ObjectiveCode, ParentCode = o.PlrCode }).ToList(),
                Initiatives = initiatives.Select(i => new StrategyNode { Code = i.InitiativeCode, Name = i.InitiativeName ?? i.InitiativeCode, ParentCode = i.ObjectiveCode }).ToList(),
                Projects = projects.Select(p => new StrategyNode { Code = p.ProjectCode, Name = p.ProjectName ?? p.ProjectCode, ParentCode = p.InitiativeCode }).ToList(),
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Live MSSQL strategy read failed; falling back to mirror.");
            return null;
        }
    }

    private async Task<StrategyDataSet?> TryMirrorAsync(CancellationToken ct)
    {
        try
        {
            var pillars = await _db.MirrorPillars.AsNoTracking().ToListAsync(ct);
            if (pillars.Count == 0) return null;

            var objectives = await _db.MirrorObjectives.AsNoTracking().ToListAsync(ct);
            var initiatives = await _db.MirrorInitiatives.AsNoTracking().ToListAsync(ct);
            var projects = await _db.MirrorProjects.AsNoTracking().ToListAsync(ct);

            return new StrategyDataSet
            {
                Source = StrategyDataSource.Mirror,
                Pillars = pillars.Select(p => new StrategyNode { Code = p.PlrCode, Name = p.PillarName ?? p.PlrCode }).ToList(),
                Objectives = objectives.Select(o => new StrategyNode { Code = o.ObjectiveCode, Name = o.ObjectiveName ?? o.ObjectiveCode, ParentCode = o.PlrCode }).ToList(),
                Initiatives = initiatives.Select(i => new StrategyNode { Code = i.InitiativeCode, Name = i.InitiativeName ?? i.InitiativeCode, ParentCode = i.ObjectiveCode }).ToList(),
                Projects = projects.Select(p => new StrategyNode { Code = p.ProjectCode, Name = p.ProjectName ?? p.ProjectCode, ParentCode = p.InitiativeCode }).ToList(),
            };
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Mirror strategy read failed; falling back to dummy.");
            return null;
        }
    }

    private static StrategyDataSet BuildDummy()
    {
        var set = new StrategyDataSet { Source = StrategyDataSource.Dummy };
        var pillarNames = new[] { "تمكين المنافسة", "حماية المنافسة", "التميز المؤسسي" };
        for (var p = 0; p < pillarNames.Length; p++)
        {
            var pCode = "P" + (p + 1);
            set.Pillars.Add(new StrategyNode { Code = pCode, Name = pillarNames[p] });
            for (var o = 1; o <= 3; o++)
            {
                var oCode = pCode + "-O" + o;
                set.Objectives.Add(new StrategyNode { Code = oCode, Name = "هدف " + (p + 1) + "." + o, ParentCode = pCode });
                for (var n = 1; n <= 3; n++)
                {
                    var iCode = oCode + "-I" + n;
                    set.Initiatives.Add(new StrategyNode { Code = iCode, Name = "مبادرة " + (p + 1) + "." + o + "." + n, ParentCode = oCode });
                    var prCode = iCode + "-PR1";
                    set.Projects.Add(new StrategyNode { Code = prCode, Name = "مشروع " + (p + 1) + "." + o + "." + n, ParentCode = iCode });
                }
            }
        }
        return set;
    }
}
