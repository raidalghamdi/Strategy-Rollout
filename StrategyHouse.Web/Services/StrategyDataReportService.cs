using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities.External;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 19.21 (Fix 4) — strategy-data executive report, rebuilt on the Survey
// analysis pattern (the page the user confirmed works "بشكل ممتاز"): a robust
// service that reads the External strategy entities (Pillars / Objectives /
// Initiatives / Projects / KPIs), produces an on-page summary AND a downloadable
// .xlsx, and never throws. Every row is written inside its own try/catch so one
// malformed record is skipped and counted instead of breaking the whole export.
//
// Data source: prefers the live External MSSQL warehouse (via the *Service read
// layer); when UseExternalDb is off it falls back to the local SQLite mirror so the
// report still renders offline. No schema/migration changes.
public class StrategyDataReportService
{
    private readonly ApplicationDbContext _db;
    private readonly PillarsService _pillars;
    private readonly ObjectivesService _objectives;
    private readonly InitiativesService _initiatives;
    private readonly ProjectsService _projects;
    private readonly KpisService _kpis;
    private readonly ILogger<StrategyDataReportService> _log;

    public StrategyDataReportService(
        ApplicationDbContext db,
        PillarsService pillars,
        ObjectivesService objectives,
        InitiativesService initiatives,
        ProjectsService projects,
        KpisService kpis,
        ILogger<StrategyDataReportService> log)
    {
        _db = db;
        _pillars = pillars;
        _objectives = objectives;
        _initiatives = initiatives;
        _projects = projects;
        _kpis = kpis;
        _log = log;
    }

    public class Report
    {
        public int PillarCount { get; set; }
        public int ObjectiveCount { get; set; }
        public int InitiativeCount { get; set; }
        public int ProjectCount { get; set; }
        public int KpiCount { get; set; }
        public decimal TotalProjectBudget { get; set; }
        public decimal TotalProjectLiquidity { get; set; }
        public int SkippedRows { get; set; }
        public string Source { get; set; } = "";
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    private async Task<(List<ExternalPillar> Pillars, List<ExternalObjective> Objectives,
        List<ExternalInitiative> Initiatives, List<ExternalProject> Projects, List<ExternalKpi> Kpis, string Source)> LoadAsync()
    {
        if (_pillars.Available)
        {
            return (await _pillars.GetAllAsync(), await _objectives.GetAllAsync(),
                await _initiatives.GetAllAsync(), await _projects.GetAllAsync(), await _kpis.GetAllAsync(),
                "قاعدة البيانات الخارجية (MSSQL)");
        }

        // Local mirror fallback — project Mirror_* rows onto the External shape so the
        // rest of the report logic is source-agnostic.
        var mp = await _db.MirrorPillars.AsNoTracking().ToListAsync();
        var mo = await _db.MirrorObjectives.AsNoTracking().ToListAsync();
        var mi = await _db.MirrorInitiatives.AsNoTracking().ToListAsync();
        var mpr = await _db.MirrorProjects.AsNoTracking().ToListAsync();
        var mk = await _db.MirrorKpis.AsNoTracking().ToListAsync();

        return (
            mp.Select(p => new ExternalPillar { PlrCode = p.PlrCode, PillarName = p.PillarName, Budget = p.Budget, Liquidity = p.Liquidity }).ToList(),
            mo.Select(o => new ExternalObjective { ObjectiveCode = o.ObjectiveCode, ObjectiveName = o.ObjectiveName, PlrCode = o.PlrCode, Budget = o.Budget, Liquidity = o.Liquidity }).ToList(),
            mi.Select(i => new ExternalInitiative { InitiativeCode = i.InitiativeCode, InitiativeName = i.InitiativeName, ObjectiveCode = i.ObjectiveCode, Owners = i.Owners, Budget = i.Budget, Liquidity = i.Liquidity }).ToList(),
            mpr.Select(p => new ExternalProject { ProjectCode = p.ProjectCode, ProjectName = p.ProjectName, InitiativeCode = p.InitiativeCode, PlrCode = p.PlrCode, ProjectType = p.ProjectType, ProjectStatus = p.ProjectStatus, Budget = p.BudgetLiquidity, GacBudget = p.GacBudget, Division = p.Division }).ToList(),
            mk.Select(k => new ExternalKpi { KpiCode = k.KpiCode, KpiName = k.KpiName, ActivationStatus = k.ActivationStatus, KpiType = k.KpiType, ObjectiveCode = k.ObjectiveCode, PlrCode = k.PlrCode, Division = k.Division }).ToList(),
            "النسخة المحلية (SQLite mirror)");
    }

    // Build the on-page summary. Never throws; counts any per-row failure as skipped.
    public async Task<Report> BuildSummaryAsync()
    {
        var report = new Report();
        try
        {
            var (pillars, objectives, initiatives, projects, kpis, source) = await LoadAsync();
            report.Source = source;
            report.PillarCount = pillars.Count;
            report.ObjectiveCount = objectives.Count;
            report.InitiativeCount = initiatives.Count;
            report.ProjectCount = projects.Count;
            report.KpiCount = kpis.Count;
            foreach (var p in projects)
            {
                try
                {
                    report.TotalProjectBudget += p.Budget ?? 0m;
                    report.TotalProjectLiquidity += p.Liquidity ?? 0m;
                }
                catch (Exception ex)
                {
                    report.SkippedRows++;
                    _log.LogWarning(ex, "Strategy report: skipped project {Code} during summary.", p.ProjectCode);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Strategy report summary build failed; returning empty summary.");
        }
        return report;
    }

    // Build the .xlsx workbook (one sheet per entity). Returns the bytes and writes
    // the skipped-row count into the passed report. Never throws — on a hard failure
    // it returns a single-sheet workbook with an Arabic notice.
    public async Task<byte[]> BuildExcelAsync(Report report)
    {
        try
        {
            var (pillars, objectives, initiatives, projects, kpis, source) = await LoadAsync();
            report.Source = source;
            var skipped = 0;

            using var wb = new XLWorkbook();

            var wsP = wb.Worksheets.Add("المرتكزات");
            WriteHeader(wsP, "الرمز", "الاسم", "الميزانية", "السيولة");
            var rp = 2;
            foreach (var p in pillars)
            {
                try { wsP.Cell(rp, 1).Value = p.PlrCode; wsP.Cell(rp, 2).Value = p.PillarName ?? ""; wsP.Cell(rp, 3).Value = p.Budget ?? 0m; wsP.Cell(rp, 4).Value = p.Liquidity ?? 0m; rp++; }
                catch (Exception ex) { skipped++; _log.LogWarning(ex, "xlsx skip pillar {Code}", p.PlrCode); }
            }

            var wsO = wb.Worksheets.Add("الأهداف");
            WriteHeader(wsO, "الرمز", "الاسم", "رمز المرتكز", "الميزانية", "السيولة");
            var ro = 2;
            foreach (var o in objectives)
            {
                try { wsO.Cell(ro, 1).Value = o.ObjectiveCode; wsO.Cell(ro, 2).Value = o.ObjectiveName ?? ""; wsO.Cell(ro, 3).Value = o.PlrCode ?? ""; wsO.Cell(ro, 4).Value = o.Budget ?? 0m; wsO.Cell(ro, 5).Value = o.Liquidity ?? 0m; ro++; }
                catch (Exception ex) { skipped++; _log.LogWarning(ex, "xlsx skip objective {Code}", o.ObjectiveCode); }
            }

            var wsI = wb.Worksheets.Add("المبادرات");
            WriteHeader(wsI, "الرمز", "الاسم", "رمز الهدف", "المالك", "الميزانية", "السيولة");
            var ri = 2;
            foreach (var i in initiatives)
            {
                try { wsI.Cell(ri, 1).Value = i.InitiativeCode; wsI.Cell(ri, 2).Value = i.InitiativeName ?? ""; wsI.Cell(ri, 3).Value = i.ObjectiveCode ?? ""; wsI.Cell(ri, 4).Value = i.Owners ?? ""; wsI.Cell(ri, 5).Value = i.Budget ?? 0m; wsI.Cell(ri, 6).Value = i.Liquidity ?? 0m; ri++; }
                catch (Exception ex) { skipped++; _log.LogWarning(ex, "xlsx skip initiative {Code}", i.InitiativeCode); }
            }

            var wsPr = wb.Worksheets.Add("المشاريع");
            WriteHeader(wsPr, "الرمز", "الاسم", "رمز المبادرة", "النوع", "الحالة", "الميزانية", "السيولة", "الإدارة");
            var rpr = 2;
            foreach (var p in projects)
            {
                try { wsPr.Cell(rpr, 1).Value = p.ProjectCode; wsPr.Cell(rpr, 2).Value = p.ProjectName ?? ""; wsPr.Cell(rpr, 3).Value = p.InitiativeCode ?? ""; wsPr.Cell(rpr, 4).Value = p.ProjectType ?? ""; wsPr.Cell(rpr, 5).Value = p.ProjectStatus ?? ""; wsPr.Cell(rpr, 6).Value = p.Budget ?? 0m; wsPr.Cell(rpr, 7).Value = p.Liquidity ?? 0m; wsPr.Cell(rpr, 8).Value = p.Division ?? ""; rpr++; }
                catch (Exception ex) { skipped++; _log.LogWarning(ex, "xlsx skip project {Code}", p.ProjectCode); }
            }

            var wsK = wb.Worksheets.Add("المؤشرات");
            WriteHeader(wsK, "الرمز", "الاسم", "النوع", "رمز الهدف", "الإدارة", "حالة التفعيل");
            var rk = 2;
            foreach (var k in kpis)
            {
                try { wsK.Cell(rk, 1).Value = k.KpiCode; wsK.Cell(rk, 2).Value = k.KpiName ?? ""; wsK.Cell(rk, 3).Value = k.KpiType ?? ""; wsK.Cell(rk, 4).Value = k.ObjectiveCode ?? ""; wsK.Cell(rk, 5).Value = k.Division ?? ""; wsK.Cell(rk, 6).Value = k.ActivationStatus ?? ""; rk++; }
                catch (Exception ex) { skipped++; _log.LogWarning(ex, "xlsx skip kpi {Code}", k.KpiCode); }
            }

            // Summary sheet (first).
            var wsS = wb.Worksheets.Add("ملخص", 1);
            wsS.Cell(1, 1).Value = "التقرير التنفيذي — بيانات الاستراتيجية";
            wsS.Cell(1, 1).Style.Font.Bold = true;
            wsS.Cell(2, 1).Value = "المصدر"; wsS.Cell(2, 2).Value = source;
            wsS.Cell(3, 1).Value = "تاريخ الإنشاء"; wsS.Cell(3, 2).Value = report.GeneratedAt.ToString("yyyy-MM-dd HH:mm");
            wsS.Cell(4, 1).Value = "عدد المرتكزات"; wsS.Cell(4, 2).Value = pillars.Count;
            wsS.Cell(5, 1).Value = "عدد الأهداف"; wsS.Cell(5, 2).Value = objectives.Count;
            wsS.Cell(6, 1).Value = "عدد المبادرات"; wsS.Cell(6, 2).Value = initiatives.Count;
            wsS.Cell(7, 1).Value = "عدد المشاريع"; wsS.Cell(7, 2).Value = projects.Count;
            wsS.Cell(8, 1).Value = "عدد المؤشرات"; wsS.Cell(8, 2).Value = kpis.Count;
            wsS.Cell(9, 1).Value = "صفوف تم تخطّيها"; wsS.Cell(9, 2).Value = skipped;

            foreach (var ws in wb.Worksheets) ws.Columns().AdjustToContents();

            report.SkippedRows = skipped;
            report.PillarCount = pillars.Count;
            report.ObjectiveCount = objectives.Count;
            report.InitiativeCount = initiatives.Count;
            report.ProjectCount = projects.Count;
            report.KpiCount = kpis.Count;

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Strategy report Excel build failed; returning notice workbook.");
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("تنبيه");
            ws.Cell(1, 1).Value = "تعذّر إنشاء التقرير حالياً. حاول مرة أخرى لاحقاً.";
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }
    }

    private static void WriteHeader(IXLWorksheet ws, params string[] headers)
    {
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#00192B");
            cell.Style.Font.FontColor = XLColor.White;
        }
    }
}
