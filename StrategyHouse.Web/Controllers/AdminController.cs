using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Domain.Enums;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

[Authorize(Roles = "Admin,Facilitator")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly JourneyMapService _journey;
    private readonly EmailComposer _email;

    public AdminController(ApplicationDbContext db, JourneyMapService journey, EmailComposer email)
    {
        _db = db; _journey = journey; _email = email;
    }

    public IActionResult Index() => View();

    // === Framework management ===
    public async Task<IActionResult> Frameworks()
        => View(await _db.Frameworks.Include(f => f.Layers).ThenInclude(l => l.Elements).ToListAsync());

    public async Task<IActionResult> Framework(int id)
    {
        var f = await _db.Frameworks
            .Include(x => x.Layers).ThenInclude(l => l.Elements)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (f == null) return NotFound();
        return View(f);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateElement(int id, string nameAr, string? descAr)
    {
        var el = await _db.FrameworkElements.FindAsync(id);
        if (el == null) return NotFound();
        el.NameAr = nameAr;
        el.DescriptionAr = descAr;
        await _db.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // === Departments ===
    public async Task<IActionResult> Departments()
        => View(await _db.Departments
            .Include(d => d.Projects)
            .Include(d => d.Kpis)
            .Include(d => d.Roles)
            .ToListAsync());

    public async Task<IActionResult> Department(int id)
    {
        var d = await _db.Departments
            .Include(x => x.Projects)
            .Include(x => x.Kpis)
            .Include(x => x.Roles)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (d == null) return NotFound();
        return View(d);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDepartment(int id, string nameAr, string? nameEn)
    {
        var d = await _db.Departments.FindAsync(id);
        if (d == null) return NotFound();
        d.NameAr = nameAr;
        if (nameEn != null) d.NameEn = nameEn;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Department), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddProject(int departmentId, string nameAr, string kind)
    {
        _db.DepartmentProjects.Add(new DepartmentProject { DepartmentId = departmentId, NameAr = nameAr, Kind = kind });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Department), new { id = departmentId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddKpi(int departmentId, string nameAr, string? unit, string? target)
    {
        _db.DepartmentKpis.Add(new DepartmentKpi { DepartmentId = departmentId, NameAr = nameAr, Unit = unit, Target = target });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Department), new { id = departmentId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRole(int departmentId, string titleAr)
    {
        _db.DepartmentRoles.Add(new DepartmentRole { DepartmentId = departmentId, TitleAr = titleAr });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Department), new { id = departmentId });
    }

    // === Sessions ===
    public async Task<IActionResult> Sessions()
        => View(await _db.Sessions
            .Include(s => s.SessionDepartments).ThenInclude(sd => sd.Department)
            .OrderByDescending(s => s.ScheduledAt)
            .ToListAsync());

    public async Task<IActionResult> CreateSession()
    {
        ViewBag.Frameworks = await _db.Frameworks.Where(f => f.IsActive).ToListAsync();
        ViewBag.Departments = await _db.Departments.Where(d => d.IsActive).ToListAsync();
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSession(int frameworkId, string title, DateTime scheduledAt, string? venue, int[] departmentIds)
    {
        var code = "S-" + Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper();
        var s = new Session
        {
            FrameworkId = frameworkId,
            TitleAr = title,
            ScheduledAt = scheduledAt,
            VenueAr = venue,
            Status = SessionStatus.Scheduled,
            AccessCode = code,
        };
        _db.Sessions.Add(s);
        await _db.SaveChangesAsync();
        foreach (var did in departmentIds ?? Array.Empty<int>())
            _db.SessionDepartments.Add(new SessionDepartment { SessionId = s.Id, DepartmentId = did });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(SessionDetail), new { id = s.Id });
    }

    public async Task<IActionResult> SessionDetail(int id)
    {
        var s = await _db.Sessions
            .Include(x => x.SessionDepartments).ThenInclude(sd => sd.Department)
            .Include(x => x.Attendees).ThenInclude(a => a.Department)
            .Include(x => x.Maps).ThenInclude(m => m.Department)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound();
        return View(s);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAttendee(int sessionId, int departmentId, string fullName, string? email, bool isHead)
    {
        _db.SessionAttendees.Add(new SessionAttendee
        {
            SessionId = sessionId, DepartmentId = departmentId,
            FullNameAr = fullName, Email = email, IsDepartmentHead = isHead,
        });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(SessionDetail), new { id = sessionId });
    }

    // === Commitments catalog ===
    public async Task<IActionResult> Commitments()
        => View(await _db.CommitmentTemplates.Include(c => c.Department).OrderBy(c => c.Order).ToListAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCommitment(string textAr, int? departmentId, CommitmentLinkType? linkType)
    {
        var max = await _db.CommitmentTemplates.AnyAsync() ? await _db.CommitmentTemplates.MaxAsync(c => c.Order) : 0;
        _db.CommitmentTemplates.Add(new CommitmentTemplate
        {
            TextAr = textAr,
            DepartmentId = departmentId,
            SuggestedLinkType = linkType,
            Order = max + 1,
            IsActive = true,
        });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Commitments));
    }

    // === Surveys / Quizzes — read-only listing in admin; editing via add-question forms ===
    public async Task<IActionResult> Survey()
    {
        var s = await _db.Surveys.Include(x => x.Questions).FirstOrDefaultAsync(x => x.IsActive);
        return View(s);
    }

    public async Task<IActionResult> Quiz()
    {
        var q = await _db.Quizzes.Include(x => x.Questions).FirstOrDefaultAsync(x => x.IsActive);
        return View(q);
    }

    // === Department head Journey Map briefing artifact ===
    public async Task<IActionResult> Journey(int sessionId, int departmentId)
    {
        var brief = await _journey.BuildAsync(sessionId, departmentId);
        return View(brief);
    }

    // === Email package preview ===
    public async Task<IActionResult> EmailPreview(int attendeeId)
    {
        var pkg = await _email.ComposeForAttendeeAsync(attendeeId);
        return Content(pkg.HtmlBody, "text/html");
    }
}
