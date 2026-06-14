using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Aggregates contribution pledges for the leadership analytics dashboard.
public class PledgeAggregateService
{
    private readonly ApplicationDbContext _db;

    public PledgeAggregateService(ApplicationDbContext db) { _db = db; }

    public async Task<PledgeAggregate> BuildAsync()
    {
        var pledges = await _db.ContributionPledges.AsNoTracking().ToListAsync();
        var deptNames = await _db.Departments.AsNoTracking()
            .ToDictionaryAsync(d => d.DeptCode, d => d.NameAr ?? d.DeptCode);

        // Friendly labels for element codes (best-effort across element types).
        var objNames = await _db.Objectives.AsNoTracking().ToDictionaryAsync(o => o.ObjectiveCode, o => o.ObjectiveName ?? o.ObjectiveCode);
        var initNames = await _db.Initiatives.AsNoTracking().ToDictionaryAsync(i => i.InitiativeCode, i => i.InitiativeName ?? i.InitiativeCode);
        var kpiNames = await _db.Kpis.AsNoTracking().ToDictionaryAsync(k => k.KpiCode, k => k.KpiName ?? k.KpiCode);
        var prjNames = await _db.Projects.AsNoTracking().ToDictionaryAsync(p => p.ProjectCode, p => p.ProjectName ?? p.ProjectCode);

        string Label(string type, string code) => type switch
        {
            "OBJ" => objNames.TryGetValue(code, out var n) ? n : code,
            "INIT" => initNames.TryGetValue(code, out var n) ? n : code,
            "KPI" => kpiNames.TryGetValue(code, out var n) ? n : code,
            "PRJ" => prjNames.TryGetValue(code, out var n) ? n : code,
            _ => code,
        };

        var byType = pledges.GroupBy(p => p.ElementType)
            .ToDictionary(g => g.Key, g => g.Count());
        var byKind = pledges.GroupBy(p => p.ContributionKind ?? "غير محدد")
            .ToDictionary(g => g.Key, g => g.Count());

        var topElements = pledges
            .GroupBy(p => new { p.ElementType, p.ElementCode })
            .Select(g => new TopElement(
                g.Key.ElementType,
                g.Key.ElementCode,
                Label(g.Key.ElementType, g.Key.ElementCode),
                g.Count()))
            .OrderByDescending(e => e.Count)
            .Take(10)
            .ToList();

        var rows = pledges
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PledgeRow(
                p.DeptCode,
                deptNames.TryGetValue(p.DeptCode, out var dn) ? dn : p.DeptCode,
                p.ElementType,
                p.ElementCode,
                Label(p.ElementType, p.ElementCode),
                p.ContributionKind ?? "",
                p.Notes ?? "",
                p.CreatedAt))
            .ToList();

        return new PledgeAggregate
        {
            Total = pledges.Count,
            ByType = byType,
            ByKind = byKind,
            TopElements = topElements,
            Rows = rows,
        };
    }
}

public record TopElement(string ElementType, string ElementCode, string Label, int Count);
public record PledgeRow(string DeptCode, string DeptName, string ElementType, string ElementCode, string ElementLabel, string Kind, string Notes, DateTime CreatedAt);

public class PledgeAggregate
{
    public int Total { get; set; }
    public Dictionary<string, int> ByType { get; set; } = new();
    public Dictionary<string, int> ByKind { get; set; } = new();
    public List<TopElement> TopElements { get; set; } = new();
    public List<PledgeRow> Rows { get; set; } = new();
}
