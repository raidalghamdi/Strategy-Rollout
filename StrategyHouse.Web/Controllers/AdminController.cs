using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

[Authorize(Roles = "Admin,Facilitator")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly PillarsService _pillars;
    private readonly ObjectivesService _objectives;
    private readonly KpisService _kpis;
    private readonly InitiativesService _initiatives;
    private readonly ProjectsService _projects;
    private readonly IStrategyDataSource _source;

    public AdminController(
        ApplicationDbContext db,
        PillarsService pillars,
        ObjectivesService objectives,
        KpisService kpis,
        InitiativesService initiatives,
        ProjectsService projects,
        IStrategyDataSource source)
    {
        _db = db;
        _pillars = pillars;
        _objectives = objectives;
        _kpis = kpis;
        _initiatives = initiatives;
        _projects = projects;
        _source = source;
    }

    public IActionResult Index() => View();

    // Phase 17 — these strategy listings prefer the external MSSQL warehouse (Option A)
    // when UseExternalDb is on, projecting external rows onto the local entity shape the
    // views already expect. When the flag is off (dev), they read local SQLite as before.
    public async Task<IActionResult> Pillars()
    {
        if (_pillars.Available)
        {
            var ext = await _pillars.GetAllAsync();
            return View(ext.Select(p => new Pillar
            {
                PlrCode = p.PlrCode,
                PillarName = p.PillarName,
                Budget = p.Budget,
                Liquidity = p.Liquidity,
                StartDates = p.StartDates,
                EndDates = p.EndDates,
                PlrPeriods = p.PlrPeriods,
            }).ToList());
        }
        return View(await _db.Pillars.OrderBy(p => p.PlrCode).ToListAsync());
    }

    public async Task<IActionResult> Objectives()
    {
        if (_objectives.Available)
        {
            var ext = await _objectives.GetAllAsync();
            return View(ext.Select(o => new Objective
            {
                ObjectiveCode = o.ObjectiveCode,
                ObjectiveName = o.ObjectiveName,
                PlrCode = o.PlrCode,
                Budget = o.Budget,
                Liquidity = o.Liquidity,
                StartDates = o.StartDates,
                EndDates = o.EndDates,
                ObjPeriod = o.ObjPeriod,
            }).ToList());
        }
        return View(await _db.Objectives.OrderBy(o => o.ObjectiveCode).ToListAsync());
    }

    public async Task<IActionResult> Departments()
        => View(await _db.Departments.OrderBy(d => d.DeptCode).ToListAsync());

    public async Task<IActionResult> Initiatives()
    {
        if (_initiatives.Available)
        {
            var ext = await _initiatives.GetAllAsync();
            return View(ext.Select(i => new Initiative
            {
                InitiativeCode = i.InitiativeCode,
                InitiativeName = i.InitiativeName,
                ObjectiveCode = i.ObjectiveCode,
                ObjectiveName = i.ObjectiveName,
                Owners = i.Owners,
                Budget = i.Budget,
                Liquidity = i.Liquidity,
                StartDates = i.StartDates,
                EndDates = i.EndDates,
            }).ToList());
        }
        return View(await _db.Initiatives.OrderBy(i => i.InitiativeCode).ToListAsync());
    }

    public async Task<IActionResult> Projects()
    {
        if (_projects.Available)
        {
            var ext = await _projects.GetAllAsync();
            return View(ext.Take(300).Select(p => new Project
            {
                ProjectCode = p.ProjectCode,
                ProjectName = p.ProjectName,
                InitiativeCode = p.InitiativeCode,
                PlrCode = p.PlrCode,
                ProjectType = p.ProjectType,
                ProjectStatus = p.ProjectStatus,
                Budget = p.Budget,
                Liquidity = p.Liquidity,
                GacBudget = p.GacBudget,
                ProjectSponsor = p.ProjectSponsor,
                ProjectManager = p.ProjectManager,
                Division = p.Division,
                ProjectPhase = p.ProjectPhase,
            }).ToList());
        }
        return View(await _db.Projects.OrderBy(p => p.ProjectCode).Take(300).ToListAsync());
    }

    public async Task<IActionResult> Kpis()
    {
        if (_kpis.Available)
        {
            var ext = await _kpis.GetAllAsync();
            return View(ext.Select(k => new Kpi
            {
                KpiCode = k.KpiCode,
                KpiName = k.KpiName,
                ActivationStatus = k.ActivationStatus,
                KpiType = k.KpiType,
                ObjectiveCode = k.ObjectiveCode,
                PlrCode = k.PlrCode,
                Division = k.Division,
                Frequency = k.Frequency,
                // Phase 19.21 (Fix 1) — external IndexWeight is string? again (the
                // real column is nvarchar(5)); copy it straight across.
                IndexWeight = k.IndexWeight,
                Target2025 = k.Target2025,
                Target2026 = k.Target2026,
                Target2027 = k.Target2027,
                Target2028 = k.Target2028,
                Target2029 = k.Target2029,
                Target2030 = k.Target2030,
                AutomationStatus = k.AutomationStatus,
            }).ToList());
        }
        // Phase 19.23 — when the live external warehouse is off, read through the unified
        // source (MSSQL mirror → SQLite → empty) instead of querying SQLite directly.
        var kpis = (await _source.GetKpisAsync())
            .Select(k => new Kpi
            {
                KpiCode = k.Code,
                KpiName = k.Name,
                KpiType = k.Type,
                ObjectiveCode = k.ObjectiveCode,
                Division = k.Division,
                ActivationStatus = k.Active ? "Active" : "Inactive",
            })
            .OrderBy(k => k.KpiCode)
            .ToList();
        return View(kpis);
    }
}
