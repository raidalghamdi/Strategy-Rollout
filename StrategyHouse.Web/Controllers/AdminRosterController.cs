using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// =====================================================================
// Phase 20.29 — Admin Roster management with bulk email assignment.
//
// Two parallel ways to add/update roster members exist:
//   1. Excel import via /Admin/DbImport (the DepartmentRoster sheet,
//      now including the new Email column, is auto-imported by the
//      generic DbImportService).
//   2. THIS controller (/Admin/Roster) for quick paste-and-go bulk add
//      from a textarea — supports adding emails to existing members OR
//      inserting brand-new roster rows in one shot.
//
// User instruction (verbatim): "Continue doing it and make sure there
// is a feature to add bulk at once."
// =====================================================================
[Authorize(Roles = "Admin")]
[Route("Admin/Roster")]
public class AdminRosterController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly AuditLogService _audit;
    private readonly ILogger<AdminRosterController> _logger;

    public AdminRosterController(
        ApplicationDbContext db,
        AuditLogService audit,
        ILogger<AdminRosterController> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    // GET /Admin/Roster — list everyone, grouped by DeptCode, show emails.
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var roster = await _db.DepartmentRoster
            .OrderBy(r => r.DeptCode)
            .ThenBy(r => r.NameAr)
            .ToListAsync();

        var depts = await _db.Departments
            .OrderBy(d => d.DeptCode)
            .Select(d => new RosterDeptOption { Code = d.DeptCode, NameAr = d.NameAr ?? string.Empty })
            .ToListAsync();

        var vm = new RosterIndexVm
        {
            Members = roster,
            Departments = depts,
            TotalCount = roster.Count,
            WithEmailCount = roster.Count(r => !string.IsNullOrWhiteSpace(r.EmailNormalized)),
        };
        return View(vm);
    }

    // GET /Admin/Roster/BulkEmails — paste-area bulk add/update.
    [HttpGet("BulkEmails")]
    public async Task<IActionResult> BulkEmails()
    {
        var depts = await _db.Departments
            .OrderBy(d => d.DeptCode)
            .Select(d => new RosterDeptOption { Code = d.DeptCode, NameAr = d.NameAr ?? string.Empty })
            .ToListAsync();
        var vm = new BulkEmailsVm { Departments = depts };
        return View(vm);
    }

    // POST /Admin/Roster/BulkEmails — parse the textarea, apply, report stats.
    //
    // Phase 20.31 — USERS-only input. Admin pastes email lines, optionally with
    // a display name. No DeptCode required.
    //
    // Accepted line formats (one per line, comma OR tab separated):
    //   email                  → if email already on a roster row → no-op (already added).
    //                            Otherwise insert a brand-new UNASSIGNED roster member
    //                            (DeptCode = "UNASSIGNED", NameAr = email local-part).
    //                            Admin needs to link them to a department later.
    //   email, namear          → same, but use the provided display name.
    //
    // Lines starting with # or // are treated as comments and skipped.
    //
    // The UNASSIGNED sentinel lets new members still log in by email; the admin
    // gets a visible "pending assignment" badge on /Admin/Roster.
    [HttpPost("BulkEmails")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkEmailsSubmit(string? payload, bool dryRun = false)
    {
        var depts = await _db.Departments
            .OrderBy(d => d.DeptCode)
            .Select(d => new RosterDeptOption { Code = d.DeptCode, NameAr = d.NameAr ?? string.Empty })
            .ToListAsync();
        var vm = new BulkEmailsVm { Departments = depts, Payload = payload };

        if (string.IsNullOrWhiteSpace(payload))
        {
            vm.Errors.Add("الرجاء لصق سطور البريد الإلكتروني أولاً.");
            return View("BulkEmails", vm);
        }

        // Phase 20.31 — preload existing roster for email lookups only.
        var rosterAll = await _db.DepartmentRoster
            .ToListAsync();
        var emailsInDb = rosterAll
            .Where(r => !string.IsNullOrWhiteSpace(r.EmailNormalized))
            .Select(r => r.EmailNormalized!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Parse lines.
        var lines = payload.Replace("\r\n", "\n").Split('\n');
        int lineNo = 0;
        var seenEmailsInPayload = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            lineNo++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;

            // Split on comma OR tab. First token = email. Second token (optional) = name.
            var parts = line.Split(new[] { ',', '\t' }, StringSplitOptions.TrimEntries);
            var email = parts.Length >= 1 ? parts[0].Trim() : string.Empty;
            var nameAr = parts.Length >= 2 ? parts[1].Trim() : string.Empty;

            // Validate email.
            if (!IsValidEmail(email))
            {
                vm.Errors.Add($"السطر {lineNo}: البريد \"{email}\" غير صالح.");
                continue;
            }
            var emailNorm = email.ToLowerInvariant();

            // Detect duplicates within the payload itself.
            if (!seenEmailsInPayload.Add(emailNorm))
            {
                vm.Errors.Add($"السطر {lineNo}: البريد \"{email}\" مكرّر داخل المدخلات.");
                continue;
            }

            // If the email already exists on a roster row → no-op (already added).
            var existing = rosterAll.FirstOrDefault(r =>
                string.Equals(r.EmailNormalized, emailNorm, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                vm.Warnings.Add($"السطر {lineNo}: البريد \"{email}\" موجود بالفعل للعضو \"{existing.NameAr}\" (إدارة {existing.DeptCode}).");
                continue;
            }

            // Insert a brand-new UNASSIGNED roster member. The admin will link
            // them to a department later from /Admin/Roster.
            if (string.IsNullOrWhiteSpace(nameAr))
            {
                nameAr = email.Split('@')[0];
            }
            var target = new DepartmentRoster
            {
                MemberId = Guid.NewGuid(),
                DeptCode = "UNASSIGNED",
                NameAr = nameAr,
                Role = null,
                IsDefaultAttending = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                Email = email,
                EmailNormalized = emailNorm,
            };
            if (!dryRun) _db.DepartmentRoster.Add(target);
            rosterAll.Add(target);
            emailsInDb.Add(emailNorm);
            vm.InsertedCount++;
        }

        if (vm.Errors.Count > 0)
        {
            vm.Errors.Insert(0, "تم رفض جميع التغييرات بسبب وجود أخطاء. صحّح الأسطر ثم أعد المحاولة.");
            // Roll back any tracked inserts.
            foreach (var entry in _db.ChangeTracker.Entries<DepartmentRoster>().ToList())
            {
                if (entry.State == EntityState.Added) entry.State = EntityState.Detached;
                else if (entry.State == EntityState.Modified) entry.State = EntityState.Unchanged;
            }
            return View("BulkEmails", vm);
        }

        if (dryRun)
        {
            // Roll back so dry-run never persists.
            foreach (var entry in _db.ChangeTracker.Entries<DepartmentRoster>().ToList())
            {
                if (entry.State == EntityState.Added) entry.State = EntityState.Detached;
                else if (entry.State == EntityState.Modified) entry.State = EntityState.Unchanged;
            }
            vm.DryRun = true;
            return View("BulkEmails", vm);
        }

        await _db.SaveChangesAsync();
        await _audit.LogAsync(
            actor: User.Identity?.Name ?? "unknown",
            actionType: "ROSTER_BULK_EMAILS",
            targetType: "DepartmentRoster",
            targetId: null,
            details: new { vm.InsertedCount, vm.UpdatedCount, Lines = lineNo });

        TempData["Success"] = $"تم بنجاح: {vm.InsertedCount} إضافة جديدة و {vm.UpdatedCount} تحديث.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Admin/Roster/AssignDept — Phase 20.31: assign an UNASSIGNED member
    // to a real department from the roster index dropdown.
    [HttpPost("AssignDept")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignDept(Guid memberId, string deptCode)
    {
        var row = await _db.DepartmentRoster.FindAsync(memberId);
        if (row == null)
        {
            TempData["Error"] = "العضو غير موجود.";
            return RedirectToAction(nameof(Index));
        }
        var code = (deptCode ?? string.Empty).Trim();
        if (code.Length == 0 || string.Equals(code, "UNASSIGNED", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "الرجاء اختيار إدارة صالحة.";
            return RedirectToAction(nameof(Index));
        }
        var deptExists = await _db.Departments.AnyAsync(d => d.DeptCode == code);
        if (!deptExists)
        {
            TempData["Error"] = $"رمز الإدارة \"{code}\" غير موجود.";
            return RedirectToAction(nameof(Index));
        }
        var oldDept = row.DeptCode;
        row.DeptCode = code;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(
            actor: User.Identity?.Name ?? "unknown",
            actionType: "ROSTER_ASSIGN_DEPT",
            targetType: "DepartmentRoster",
            targetId: row.MemberId.ToString(),
            details: new { FromDept = oldDept, ToDept = code, row.NameAr, row.Email });
        TempData["Success"] = $"تم ربط \"{row.NameAr}\" بالإدارة {code}.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Admin/Roster/ClearEmail — quick single-row email removal.
    [HttpPost("ClearEmail")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearEmail(Guid memberId)
    {
        var row = await _db.DepartmentRoster.FindAsync(memberId);
        if (row == null)
        {
            TempData["Error"] = "العضو غير موجود.";
            return RedirectToAction(nameof(Index));
        }
        row.Email = null;
        row.EmailNormalized = null;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(
            actor: User.Identity?.Name ?? "unknown",
            actionType: "ROSTER_CLEAR_EMAIL",
            targetType: "DepartmentRoster",
            targetId: row.MemberId.ToString(),
            details: new { row.DeptCode, row.NameAr });
        TempData["Success"] = $"تم حذف بريد العضو \"{row.NameAr}\".";
        return RedirectToAction(nameof(Index));
    }

    private static bool IsValidEmail(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        try
        {
            var addr = new System.Net.Mail.MailAddress(s);
            return addr.Address == s && s.Length <= 320 && s.Contains('@');
        }
        catch { return false; }
    }
}

// ---- View models -----------------------------------------------------

public class RosterDeptOption
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
}

public class RosterIndexVm
{
    public List<DepartmentRoster> Members { get; set; } = new();
    public List<RosterDeptOption> Departments { get; set; } = new();
    public int TotalCount { get; set; }
    public int WithEmailCount { get; set; }
}

public class BulkEmailsVm
{
    public List<RosterDeptOption> Departments { get; set; } = new();
    public string? Payload { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public int InsertedCount { get; set; }
    public int UpdatedCount { get; set; }
    public bool DryRun { get; set; }
}
