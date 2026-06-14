using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Computes the coverage heat-map matrix: strategy elements (rows) × departments (cols).
// A cell counts how much a department "touches" a strategy element via:
//   - pledges (ContributionPledge) targeting that element
//   - native ownership: KPIs / Projects whose Department_Code == dept and that roll up to the element
public class CoverageService
{
    private readonly ApplicationDbContext _db;

    public CoverageService(ApplicationDbContext db) { _db = db; }

    public enum ElementDimension { Pillar, Objective, Initiative }

    public async Task<CoverageMatrix> BuildAsync(ElementDimension dimension)
    {
        var departments = await _db.Departments.OrderBy(d => d.DeptCode).ToListAsync();
        var pledges = await _db.ContributionPledges.AsNoTracking().ToListAsync();
        var kpis = await _db.Kpis.AsNoTracking().ToListAsync();
        var projects = await _db.Projects.AsNoTracking().ToListAsync();
        var objectives = await _db.Objectives.AsNoTracking().ToListAsync();
        var initiatives = await _db.Initiatives.AsNoTracking().ToListAsync();
        var pillars = await _db.Pillars.AsNoTracking().OrderBy(p => p.PlrCode).ToListAsync();

        // Build the row elements with code+label according to the chosen dimension.
        var rows = dimension switch
        {
            ElementDimension.Pillar => pillars.Select(p => new CoverageRow(p.PlrCode, p.PillarName ?? p.PlrCode)).ToList(),
            ElementDimension.Objective => objectives.OrderBy(o => o.ObjectiveCode)
                .Select(o => new CoverageRow(o.ObjectiveCode, o.ObjectiveName ?? o.ObjectiveCode)).ToList(),
            _ => initiatives.OrderBy(i => i.InitiativeCode)
                .Select(i => new CoverageRow(i.InitiativeCode, i.InitiativeName ?? i.InitiativeCode)).ToList(),
        };

        // Lookups for rolling KPIs/Projects up to the chosen dimension.
        var objToPlr = objectives.ToDictionary(o => o.ObjectiveCode, o => o.PlrCode ?? "");
        var initToObj = initiatives.ToDictionary(i => i.InitiativeCode, i => i.ObjectiveCode ?? "");
        var prjToInit = projects.Where(p => p.InitiativeCode != null)
            .ToDictionary(p => p.ProjectCode, p => p.InitiativeCode!);

        // Resolve which row-code a KPI / Project / Pledge belongs to under the dimension.
        string? KpiRowCode(Kpi k) => dimension switch
        {
            ElementDimension.Pillar => k.PlrCode ?? (k.ObjectiveCode != null && objToPlr.TryGetValue(k.ObjectiveCode, out var pl) ? pl : null),
            ElementDimension.Objective => k.ObjectiveCode,
            _ => null, // KPIs don't link to initiatives directly
        };
        string? ProjectRowCode(Project p) => dimension switch
        {
            ElementDimension.Pillar => p.PlrCode,
            ElementDimension.Objective => p.InitiativeCode != null && initToObj.TryGetValue(p.InitiativeCode, out var oc) ? oc : null,
            ElementDimension.Initiative => p.InitiativeCode,
            _ => null,
        };
        string? PledgeRowCode(ContributionPledge pl)
        {
            // Pledge ElementType: OBJ / INIT / KPI / PRJ, ElementCode is that element's code.
            switch (dimension)
            {
                case ElementDimension.Objective:
                    return pl.ElementType switch
                    {
                        "OBJ" => pl.ElementCode,
                        "INIT" => initToObj.TryGetValue(pl.ElementCode, out var oc) ? oc : null,
                        "PRJ" => prjToInit.TryGetValue(pl.ElementCode, out var ic) && initToObj.TryGetValue(ic, out var oc2) ? oc2 : null,
                        "KPI" => kpis.FirstOrDefault(k => k.KpiCode == pl.ElementCode)?.ObjectiveCode,
                        _ => null,
                    };
                case ElementDimension.Initiative:
                    return pl.ElementType switch
                    {
                        "INIT" => pl.ElementCode,
                        "PRJ" => prjToInit.TryGetValue(pl.ElementCode, out var ic) ? ic : null,
                        _ => null,
                    };
                case ElementDimension.Pillar:
                    return pl.ElementType switch
                    {
                        "OBJ" => objToPlr.TryGetValue(pl.ElementCode, out var pp) ? pp : null,
                        "INIT" => initToObj.TryGetValue(pl.ElementCode, out var oc) && objToPlr.TryGetValue(oc, out var pp2) ? pp2 : null,
                        "PRJ" => projects.FirstOrDefault(p => p.ProjectCode == pl.ElementCode)?.PlrCode,
                        "KPI" => kpis.FirstOrDefault(k => k.KpiCode == pl.ElementCode)?.PlrCode,
                        _ => null,
                    };
                default: return null;
            }
        }

        var rowIndex = rows.Select((r, i) => (r.Code, i)).ToDictionary(x => x.Code, x => x.i);
        var deptIndex = departments.Select((d, i) => (d.DeptCode, i)).ToDictionary(x => x.DeptCode, x => x.i);
        var cells = new CoverageCell[rows.Count, departments.Count];
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < departments.Count; c++)
                cells[r, c] = new CoverageCell();

        void Add(string? rowCode, string? deptCode, string kind, string label)
        {
            if (rowCode == null || deptCode == null) return;
            if (!rowIndex.TryGetValue(rowCode, out var ri)) return;
            if (!deptIndex.TryGetValue(deptCode, out var ci)) return;
            var cell = cells[ri, ci];
            switch (kind)
            {
                case "KPI": cell.KpiCount++; break;
                case "PRJ": cell.ProjectCount++; break;
                case "PLG": cell.PledgeCount++; break;
            }
            if (cell.Details.Count < 12) cell.Details.Add($"[{kind}] {label}");
        }

        foreach (var k in kpis)
            Add(KpiRowCode(k), k.DepartmentCode, "KPI", k.KpiName ?? k.KpiCode);
        foreach (var p in projects)
            Add(ProjectRowCode(p), p.DepartmentCode, "PRJ", p.ProjectName ?? p.ProjectCode);
        foreach (var pl in pledges)
            Add(PledgeRowCode(pl), pl.DeptCode, "PLG", $"{pl.ElementCode} — {pl.ContributionKind}");

        return new CoverageMatrix
        {
            Dimension = dimension,
            Rows = rows,
            Departments = departments,
            Cells = cells,
        };
    }
}

public record CoverageRow(string Code, string Label);

public class CoverageCell
{
    public int KpiCount { get; set; }
    public int ProjectCount { get; set; }
    public int PledgeCount { get; set; }
    public List<string> Details { get; } = new();
    public int Total => KpiCount + ProjectCount + PledgeCount;
}

public class CoverageMatrix
{
    public CoverageService.ElementDimension Dimension { get; set; }
    public List<CoverageRow> Rows { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public CoverageCell[,] Cells { get; set; } = new CoverageCell[0, 0];
}
