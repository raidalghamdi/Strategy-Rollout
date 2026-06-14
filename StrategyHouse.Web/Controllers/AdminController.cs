using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Controllers;

[Authorize(Roles = "Admin,Facilitator")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;

    public AdminController(ApplicationDbContext db) { _db = db; }

    public IActionResult Index() => View();

    public async Task<IActionResult> Pillars()
        => View(await _db.Pillars.OrderBy(p => p.PlrCode).ToListAsync());

    public async Task<IActionResult> Objectives()
        => View(await _db.Objectives.OrderBy(o => o.ObjectiveCode).ToListAsync());

    public async Task<IActionResult> Departments()
        => View(await _db.Departments.OrderBy(d => d.DeptCode).ToListAsync());

    public async Task<IActionResult> Initiatives()
        => View(await _db.Initiatives.OrderBy(i => i.InitiativeCode).ToListAsync());

    public async Task<IActionResult> Projects()
        => View(await _db.Projects.OrderBy(p => p.ProjectCode).Take(300).ToListAsync());

    public async Task<IActionResult> Kpis()
        => View(await _db.Kpis.OrderBy(k => k.KpiCode).ToListAsync());
}
