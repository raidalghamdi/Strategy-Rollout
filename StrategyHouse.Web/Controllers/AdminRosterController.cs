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
    // Accepted line formats (one per line, comma OR tab separated):
    //   email,deptcode                          → updates existing row matched
    //                                             by DeptCode + a UNIQUE NameAr
    //                                             match OR inserts a new row
    //                                             with NameAr = email local-part
    //   email,deptcode,namear                   → upsert by (deptcode, namear)
    //   email,deptcode,namear,role              → same + role
    //
    // Lines starting with # or // are treated as comments and skipped.
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

        // Pre-load roster + dept codes for fast lookups.
        var rosterByDept = await _db.DepartmentRoster
            .ToListAsync();
        var deptCodes = new HashSet<string>(depts.Select(d => d.Code), StringComparer.OrdinalIgnoreCase);
        var emailsInDb = rosterByDept
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

            // Split on comma OR tab.
            var parts = line.Split(new[] { ',', '\t' }, StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                vm.Errors.Add($"السطر {lineNo}: تنسيق غير صالح — يجب أن يحتوي على البريد ورمز الإدارة على الأقل.");
                continue;
            }

            var email = parts[0].Trim();
            var deptCode = parts[1].Trim();
            var nameAr = parts.Length >= 3 ? parts[2].Trim() : string.Empty;
            var role = parts.Length >= 4 ? parts[3].Trim() : null;

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

            // Validate dept exists.
            if (!deptCodes.Contains(deptCode))
            {
                vm.Errors.Add($"السطر {lineNo}: رمز الإدارة \"{deptCode}\" غير موجود.");
                continue;
            }

            // Locate target row.
            DepartmentRoster? target = null;

            // 1) If NameAr provided, match by (DeptCode, NameAr).
            if (!string.IsNullOrWhiteSpace(nameAr))
            {
                target = rosterByDept.FirstOrDefault(r =>
                    string.Equals(r.DeptCode, deptCode, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.NameAr, nameAr, StringComparison.Ordinal));
            }

            // 2) If no NameAr OR no match, AND the email already exists somewhere,
            //    update that row in place (and verify dept matches).
            if (target == null)
            {
                var byEmail = rosterByDept.FirstOrDefault(r =>
                    string.Equals(r.EmailNormalized, emailNorm, StringComparison.OrdinalIgnoreCase));
                if (byEmail != null)
                {
                    if (!string.Equals(byEmail.DeptCode, deptCode, StringComparison.OrdinalIgnoreCase))
                    {
                        vm.Errors.Add($"السطر {lineNo}: البريد \"{email}\" مرتبط بإدارة أخرى ({byEmail.DeptCode}).");
                        continue;
                    }
                    target = byEmail;
                }
            }

            // 3) Otherwise: insert a brand-new row.
            if (target == null)
            {
                if (string.IsNullOrWhiteSpace(nameAr))
                {
                    // Use email local-part as a placeholder name when none provided.
                    nameAr = email.Split('@')[0];
                }
                target = new DepartmentRoster
                {
                    MemberId = Guid.NewGuid(),
                    DeptCode = deptCode,
                    NameAr = nameAr,
                    Role = role,
                    IsDefaultAttending = true,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                };
                if (!dryRun) _db.DepartmentRoster.Add(target);
                rosterByDept.Add(target);
                vm.InsertedCount++;
            }
            else
            {
                // Update path: warn if email is changing on top of another existing one.
                if (!string.IsNullOrEmpty(target.EmailNormalized)
                    && !string.Equals(target.EmailNormalized, emailNorm, StringComparison.OrdinalIgnoreCase))
                {
                    vm.Warnings.Add($"السطر {lineNo}: تم استبدال البريد القديم \"{target.Email}\" بـ \"{email}\" للعضو \"{target.NameAr}\".");
                }
                if (!string.IsNullOrWhiteSpace(role)) target.Role = role;
                vm.UpdatedCount++;
            }

            // Cross-row uniqueness check (excluding self).
            if (emailsInDb.Contains(emailNorm)
                && !string.Equals(target.EmailNormalized, emailNorm, StringComparison.OrdinalIgnoreCase))
            {
                vm.Errors.Add($"السطر {lineNo}: البريد \"{email}\" مستخدم بالفعل لعضو آخر.");
                continue;
            }

            target.Email = email;
            target.EmailNormalized = emailNorm;
            emailsInDb.Add(emailNorm);
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
