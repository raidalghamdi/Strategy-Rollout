using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 17 — verification page for the external MSSQL (Option A) wiring.
// Shows the feature flag, a live connection ping, per-table row counts and the
// first few rows of each of the 5 strategy tables so an operator can confirm the
// warehouse is readable before/after going live. Safe when the flag is off: the
// ExternalDbContext is simply not registered and the page reports "disabled".
[Authorize(Roles = "Admin,Facilitator")]
[Route("Admin/ExternalData")]
public class AdminExternalDataController : Controller
{
    private readonly IConfiguration _config;
    private readonly DepartmentDirectoryService _depts;
    private readonly IMssqlMirrorService _mirror;
    private readonly ExternalDbDiagnostics _diagnostics;
    private readonly ExternalDbContext? _external;

    public AdminExternalDataController(
        IConfiguration config,
        DepartmentDirectoryService depts,
        IMssqlMirrorService mirror,
        ExternalDbDiagnostics diagnostics,
        ExternalDbContext? external = null)
    {
        _config = config;
        _depts = depts;
        _mirror = mirror;
        _diagnostics = diagnostics;
        _external = external;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var vm = new ExternalDataViewModel
        {
            FlagEnabled = _config.GetValue<bool>("Features:UseExternalDb"),
            ContextRegistered = _external != null,
            ConnectionConfigured = !string.IsNullOrWhiteSpace(_config.GetConnectionString("ExternalMssql")),
            Mirror = await _mirror.GetMetadataAsync(),
        };

        if (_external == null)
        {
            vm.StatusMessage = vm.FlagEnabled
                ? "العلم مفعّل لكن لا يوجد اتصال (سلسلة الاتصال فارغة) — يعمل الموقع على قاعدة البيانات المحلية."
                : "وضع التطوير: الربط مع قاعدة البيانات الخارجية غير مفعّل (UseExternalDb=false).";
            return View(vm);
        }

        try
        {
            vm.CanConnect = await _external.Database.CanConnectAsync();
        }
        catch (Exception ex)
        {
            vm.CanConnect = false;
            vm.StatusMessage = "تعذّر الاتصال بقاعدة البيانات الخارجية: " + ex.Message;
            return View(vm);
        }

        if (!vm.CanConnect)
        {
            vm.StatusMessage = "تعذّر الاتصال بقاعدة البيانات الخارجية (ping فشل).";
            return View(vm);
        }

        try
        {
            vm.Counts["Pillars"] = await _external.Pillars.CountAsync();
            vm.Counts["Objectives"] = await _external.Objectives.CountAsync();
            vm.Counts["KPIs"] = await _external.Kpis.CountAsync();
            vm.Counts["Initiatives"] = await _external.Initiatives.CountAsync();
            vm.Counts["Projects"] = await _external.Projects.CountAsync();

            vm.SamplePillars = await _external.Pillars.AsNoTracking().OrderBy(p => p.PlrCode).Take(5)
                .Select(p => new[] { p.PlrCode, p.PillarName ?? "", p.PlrPeriods ?? "" }).ToListAsync();
            vm.SampleObjectives = await _external.Objectives.AsNoTracking().OrderBy(o => o.ObjectiveCode).Take(5)
                .Select(o => new[] { o.ObjectiveCode, o.ObjectiveName ?? "", o.PlrCode ?? "" }).ToListAsync();
            vm.SampleKpis = await _external.Kpis.AsNoTracking().OrderBy(k => k.KpiCode).Take(5)
                .Select(k => new[] { k.KpiCode, k.KpiName ?? "", k.Division ?? "" }).ToListAsync();
            vm.SampleInitiatives = await _external.Initiatives.AsNoTracking().OrderBy(i => i.InitiativeCode).Take(5)
                .Select(i => new[] { i.InitiativeCode, i.InitiativeName ?? "", i.ObjectiveCode ?? "" }).ToListAsync();
            vm.SampleProjects = await _external.Projects.AsNoTracking().OrderBy(p => p.ProjectCode).Take(5)
                .Select(p => new[] { p.ProjectCode, p.ProjectName ?? "", p.Division ?? "" }).ToListAsync();

            vm.DistinctDivisions = (await _depts.GetDepartmentsAsync()).Count;
            vm.StatusMessage = "الاتصال ناجح — البيانات قابلة للقراءة.";
        }
        catch (Exception ex)
        {
            vm.StatusMessage = "تم الاتصال لكن فشلت قراءة الجداول: " + ex.Message;
        }

        return View(vm);
    }

    // GET /Admin/ExternalData/TestConnection — Phase 19.7. Admin-only read-only
    // diagnostic. Runs a bounded CanConnect probe against the external MSSQL,
    // masks the password, categorises any failure and returns an Arabic hint as
    // JSON for the admin UI. Never throws to the caller.
    [HttpGet("TestConnection")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> TestConnection()
    {
        var r = await _diagnostics.TestAsync(HttpContext.RequestAborted);
        return Json(new
        {
            useExternalDb = r.UseExternalDb,
            connectionStringMasked = r.ConnectionStringMasked,
            canConnect = r.CanConnect,
            errorMessage = r.ErrorMessage,
            errorCategory = r.ErrorCategory,
            arabicHint = r.ArabicHint,
            latencyMs = r.LatencyMs,
            serverVersion = r.ServerVersion,
        });
    }

    // POST /Admin/ExternalData/PushToSqlite — Phase 19.5. Admin-only. Mirrors all
    // five MSSQL strategy tables into the local SQLite Mirror_* tables so the app
    // has a resilient offline copy. Returns JSON for the AJAX caller.
    [HttpPost("PushToSqlite")]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PushToSqlite()
    {
        var result = await _mirror.PushAllAsync(HttpContext.RequestAborted);
        return Json(new
        {
            success = result.Success,
            recordCount = result.RecordCount,
            durationSeconds = result.DurationSeconds,
            errorMessage = result.ErrorMessage,
        });
    }
}

public class ExternalDataViewModel
{
    public bool FlagEnabled { get; set; }
    public bool ContextRegistered { get; set; }
    public bool ConnectionConfigured { get; set; }
    public bool CanConnect { get; set; }
    public string StatusMessage { get; set; } = "";
    public Dictionary<string, int> Counts { get; set; } = new();
    public int DistinctDivisions { get; set; }
    public MirrorMetadata? Mirror { get; set; }

    // Each sample row is [code, name, extra] for compact display.
    public List<string[]> SamplePillars { get; set; } = new();
    public List<string[]> SampleObjectives { get; set; } = new();
    public List<string[]> SampleKpis { get; set; } = new();
    public List<string[]> SampleInitiatives { get; set; } = new();
    public List<string[]> SampleProjects { get; set; } = new();
}
