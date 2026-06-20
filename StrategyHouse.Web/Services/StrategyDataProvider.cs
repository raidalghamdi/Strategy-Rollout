using StrategyHouse.Web.Services.Dtos;

namespace StrategyHouse.Web.Services;

// Phase 19.5 / 19.23 — single source of truth for strategy data (pillars → objectives
// → initiatives → projects) used by views and the Sankey API. As of Phase 19.23 it
// delegates all reads to IStrategyDataSource (MSSQL mirror → SQLite → empty); the
// hardcoded 3×3 dummy fallback has been removed. When neither source has rows the
// dataset is Empty and the UI shows an explicit "no data" notice.

// Phase 19.23 — Dummy removed; Empty added. Strategy data resolves
// Mirror → SQLite, and surfaces Empty when neither has rows (UI warns the operator).
public enum StrategyDataSource { Live, Mirror, Sqlite, Empty }

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
    public bool IsEmpty => Source == StrategyDataSource.Empty;
}

public interface IStrategyDataProvider
{
    Task<StrategyDataSet> GetStrategyAsync(CancellationToken ct = default);
    Task<object> GetSankeyDataAsync(CancellationToken ct = default);
}

public class StrategyDataProvider : IStrategyDataProvider
{
    private const string EmptyWarning = "لا توجد بيانات استراتيجية. يرجى مزامنة MSSQL أو التواصل مع المسؤول.";

    private readonly IStrategyDataSource _source;
    private readonly StrategyContentService? _content;

    public StrategyDataProvider(IStrategyDataSource source, StrategyContentService? content = null)
    {
        _source = source;
        _content = content;
    }

    public async Task<StrategyDataSet> GetStrategyAsync(CancellationToken ct = default)
    {
        var pillars = await _source.GetPillarsAsync(ct);
        var objectives = await _source.GetObjectivesAsync(ct);
        var initiatives = await _source.GetInitiativesAsync(null, ct);
        var projects = await _source.GetProjectsAsync(null, ct);
        var counts = await _source.GetCountsAsync(ct);

        return new StrategyDataSet
        {
            Source = counts.Source,
            Pillars = pillars.Select(p => new StrategyNode { Code = p.Code, Name = p.Name }).ToList(),
            Objectives = objectives.Select(o => new StrategyNode { Code = o.Code, Name = o.Name, ParentCode = o.PillarCode }).ToList(),
            Initiatives = initiatives.Select(i => new StrategyNode { Code = i.Code, Name = i.Name, ParentCode = i.ObjectiveCode }).ToList(),
            Projects = projects.Select(p => new StrategyNode { Code = p.Code, Name = p.Name, ParentCode = p.InitiativeCode }).ToList(),
        };
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

        // Phase 19.20 (Fix 2/3) — group-by-first so duplicate codes in the source data
        // can't throw an ArgumentException here (which previously surfaced in the UI as
        // "تعذّر تحميل مخطط التدفق").
        var pillarByCode = data.Pillars.GroupBy(p => p.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        var objByCode = data.Objectives.GroupBy(o => o.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var initByCode = data.Initiatives.GroupBy(i => i.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

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
            empty = data.IsEmpty,
            warning = data.IsEmpty ? EmptyWarning : null,
            nodes,
            links,
        };
    }
}
