using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Domain.Enums;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 20 — user management. Admin-only. Soft-delete only (IsActive=false) so audit
// history is never orphaned.
[Authorize(Roles = "Admin")]
[Route("Admin/Users")]
public class AdminUsersController : Controller
{
    private readonly UserManager<AppUser> _users;
    private readonly AuditLogService _audit;

    // Phase 20.28 — protected admin accounts cannot be renamed, edited, disabled,
    // or have their password reset from this UI. SeedData re-asserts these accounts
    // on every startup so they are always recoverable.
    private static readonly HashSet<string> ProtectedAdminUserNames =
        new(StringComparer.OrdinalIgnoreCase) { "admin@gac.gov.sa", "admin", "gac.admin" };

    private static bool IsProtected(AppUser u) =>
        u.UserName != null && ProtectedAdminUserNames.Contains(u.UserName);

    public AdminUsersController(UserManager<AppUser> users, AuditLogService audit)
    {
        _users = users;
        _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        // Phase 20.19 — show ALL accounts (platform + journey/VP) so an admin can
        // edit and reset passwords from one place. Journey accounts are flagged
        // visually in the view via JourneyScopeKey.
        var users = await _users.Users
            .OrderBy(u => u.JourneyScopeKey != null)
            .ThenBy(u => u.UserName)
            .ToListAsync();
        return View(users.Select(u => new UserRow
        {
            Id = u.Id,
            UserName = u.UserName ?? "",
            FullNameAr = u.FullNameAr,
            AppRole = u.AppRole.ToString(),
            JourneyScopeKey = u.JourneyScopeKey,
            IsActive = u.IsActive,
            CreatedAt = u.CreatedAt,
            LastLoginAt = u.LastLoginAt,
            IsProtected = IsProtected(u),
        }).ToList());
    }

    [HttpGet("Create")]
    public IActionResult Create() => View(new UserEditModel());

    [HttpPost("Create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserEditModel m)
    {
        if (string.IsNullOrWhiteSpace(m.UserName) || string.IsNullOrWhiteSpace(m.Password))
        {
            ModelState.AddModelError("", "اسم المستخدم وكلمة المرور مطلوبان.");
            return View(m);
        }
        var user = new AppUser
        {
            UserName = m.UserName,
            Email = m.UserName.Contains('@') ? m.UserName : $"{m.UserName}@gac.gov.sa",
            FullNameAr = m.FullNameAr ?? "",
            AppRole = Enum.TryParse<UserRole>(m.AppRole, out var r) ? r : UserRole.Facilitator,
            JourneyScopeKey = string.IsNullOrWhiteSpace(m.JourneyScopeKey) ? null : m.JourneyScopeKey,
            IsActive = true,
            EmailConfirmed = true,
        };
        var res = await _users.CreateAsync(user, m.Password);
        if (!res.Succeeded)
        {
            ModelState.AddModelError("", string.Join(" / ", res.Errors.Select(e => e.Description)));
            return View(m);
        }
        await _users.AddToRoleAsync(user, user.AppRole.ToString());
        await _audit.LogAsync(User.Identity?.Name ?? "system", "USER_CREATE", "User", user.UserName,
            new { user.FullNameAr, AppRole = user.AppRole.ToString(), user.JourneyScopeKey });
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Edit/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var u = await _users.FindByIdAsync(id.ToString());
        if (u == null) return NotFound();
        return View(new UserEditModel
        {
            Id = u.Id,
            UserName = u.UserName ?? "",
            FullNameAr = u.FullNameAr,
            AppRole = u.AppRole.ToString(),
            JourneyScopeKey = u.JourneyScopeKey,
            IsActive = u.IsActive,
        });
    }

    [HttpPost("Edit/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, UserEditModel m)
    {
        var u = await _users.FindByIdAsync(id.ToString());
        if (u == null) return NotFound();
        if (IsProtected(u))
        {
            TempData["Error"] = "حساب الأدمن المحمي لا يمكن تعديله من الواجهة.";
            return RedirectToAction(nameof(Index));
        }

        var oldRole = u.AppRole;
        u.FullNameAr = m.FullNameAr ?? "";
        u.JourneyScopeKey = string.IsNullOrWhiteSpace(m.JourneyScopeKey) ? null : m.JourneyScopeKey;
        u.IsActive = m.IsActive;
        if (Enum.TryParse<UserRole>(m.AppRole, out var r)) u.AppRole = r;

        var res = await _users.UpdateAsync(u);
        if (!res.Succeeded)
        {
            ModelState.AddModelError("", string.Join(" / ", res.Errors.Select(e => e.Description)));
            return View(m);
        }
        if (oldRole != u.AppRole)
        {
            await _users.RemoveFromRoleAsync(u, oldRole.ToString());
            await _users.AddToRoleAsync(u, u.AppRole.ToString());
        }
        await _audit.LogAsync(User.Identity?.Name ?? "system", "USER_UPDATE", "User", u.UserName,
            new { u.FullNameAr, AppRole = u.AppRole.ToString(), u.JourneyScopeKey, u.IsActive });
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("ResetPassword/{id:int}")]
    public async Task<IActionResult> ResetPassword(int id)
    {
        var u = await _users.FindByIdAsync(id.ToString());
        if (u == null) return NotFound();
        if (IsProtected(u))
        {
            TempData["Error"] = "حساب الأدمن المحمي لا يمكن إعادة تعيين كلمة مروره من الواجهة.";
            return RedirectToAction(nameof(Index));
        }
        ViewBag.UserName = u.UserName;
        return View(new ResetPasswordModel { Id = id });
    }

    [HttpPost("ResetPassword/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordModel m)
    {
        var u = await _users.FindByIdAsync(id.ToString());
        if (u == null) return NotFound();
        if (IsProtected(u))
        {
            TempData["Error"] = "حساب الأدمن المحمي لا يمكن إعادة تعيين كلمة مروره من الواجهة.";
            return RedirectToAction(nameof(Index));
        }
        if (string.IsNullOrWhiteSpace(m.NewPassword) || m.NewPassword != m.ConfirmPassword)
        {
            ViewBag.UserName = u.UserName;
            ModelState.AddModelError("", "كلمتا المرور غير متطابقتين أو فارغتان.");
            return View(m);
        }
        var token = await _users.GeneratePasswordResetTokenAsync(u);
        var res = await _users.ResetPasswordAsync(u, token, m.NewPassword);
        if (!res.Succeeded)
        {
            ViewBag.UserName = u.UserName;
            ModelState.AddModelError("", string.Join(" / ", res.Errors.Select(e => e.Description)));
            return View(m);
        }
        await _audit.LogAsync(User.Identity?.Name ?? "system", "USER_RESET_PASSWORD", "User", u.UserName, null);
        return RedirectToAction(nameof(Index));
    }

    // Soft delete — never hard-delete, to preserve audit references.
    [HttpPost("Delete/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var u = await _users.FindByIdAsync(id.ToString());
        if (u == null) return NotFound();
        if (IsProtected(u))
        {
            TempData["Error"] = "حساب الأدمن المحمي لا يمكن تعطيله.";
            return RedirectToAction(nameof(Index));
        }
        u.IsActive = false;
        await _users.UpdateAsync(u);
        await _audit.LogAsync(User.Identity?.Name ?? "system", "USER_DEACTIVATE", "User", u.UserName, null);
        return RedirectToAction(nameof(Index));
    }

    // Phase 20.28 — rename a user. Changes UserName + Email and refreshes
    // SecurityStamp so existing sessions are invalidated. Blocked for protected admins.
    [HttpGet("Rename/{id:int}")]
    public async Task<IActionResult> Rename(int id)
    {
        var u = await _users.FindByIdAsync(id.ToString());
        if (u == null) return NotFound();
        if (IsProtected(u))
        {
            TempData["Error"] = "حساب الأدمن المحمي لا يمكن إعادة تسميته.";
            return RedirectToAction(nameof(Index));
        }
        return View(new RenameUserModel
        {
            Id = id,
            OldUserName = u.UserName ?? "",
            OldEmail = u.Email ?? "",
            NewUserName = u.UserName ?? "",
            NewEmail = u.Email ?? "",
        });
    }

    [HttpPost("Rename/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename(int id, RenameUserModel m)
    {
        var u = await _users.FindByIdAsync(id.ToString());
        if (u == null) return NotFound();
        if (IsProtected(u))
        {
            TempData["Error"] = "حساب الأدمن المحمي لا يمكن إعادة تسميته.";
            return RedirectToAction(nameof(Index));
        }
        var newName = (m.NewUserName ?? "").Trim();
        m.OldUserName = u.UserName ?? "";
        m.OldEmail = u.Email ?? "";
        if (string.IsNullOrWhiteSpace(newName))
        {
            ModelState.AddModelError("", "اسم المستخدم الجديد مطلوب.");
            return View(m);
        }
        if (ProtectedAdminUserNames.Contains(newName))
        {
            ModelState.AddModelError("", "لا يمكن استخدام اسم مستخدم محجوز للأدمن.");
            return View(m);
        }
        var existing = await _users.FindByNameAsync(newName);
        if (existing != null && existing.Id != u.Id)
        {
            ModelState.AddModelError("", "اسم المستخدم مستخدم مسبقاً.");
            return View(m);
        }

        var oldName = u.UserName ?? "";
        var oldEmail = u.Email ?? "";
        var newEmail = string.IsNullOrWhiteSpace(m.NewEmail)
            ? (newName.Contains('@') ? newName : $"{newName}@gac.gov.sa")
            : m.NewEmail.Trim();

        var rn = await _users.SetUserNameAsync(u, newName);
        if (!rn.Succeeded)
        {
            ModelState.AddModelError("", string.Join(" / ", rn.Errors.Select(e => e.Description)));
            return View(m);
        }
        var re = await _users.SetEmailAsync(u, newEmail);
        if (!re.Succeeded)
        {
            ModelState.AddModelError("", string.Join(" / ", re.Errors.Select(e => e.Description)));
            return View(m);
        }
        await _users.UpdateSecurityStampAsync(u);

        await _audit.LogAsync(User.Identity?.Name ?? "system", "USER_RENAME", "User", newName,
            new { OldUserName = oldName, NewUserName = newName, OldEmail = oldEmail, NewEmail = newEmail });

        TempData["Success"] = $"تم تغيير اسم المستخدم من {oldName} إلى {newName}.";
        return RedirectToAction(nameof(Index));
    }
}

public class UserRow
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public string FullNameAr { get; set; } = "";
    public string AppRole { get; set; } = "";
    public string? JourneyScopeKey { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool IsProtected { get; set; }
}

public class RenameUserModel
{
    public int Id { get; set; }
    public string OldUserName { get; set; } = "";
    public string OldEmail { get; set; } = "";
    public string NewUserName { get; set; } = "";
    public string NewEmail { get; set; } = "";
}

public class UserEditModel
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public string? FullNameAr { get; set; }
    public string? Password { get; set; }
    public string AppRole { get; set; } = "Facilitator";
    public string? JourneyScopeKey { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ResetPasswordModel
{
    public int Id { get; set; }
    public string? NewPassword { get; set; }
    public string? ConfirmPassword { get; set; }
}
