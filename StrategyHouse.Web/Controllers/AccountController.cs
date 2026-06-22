using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<AppUser> _signInManager;
    private readonly UserManager<AppUser> _userManager;
    private readonly ApplicationDbContext _db;

    public AccountController(SignInManager<AppUser> signInManager, UserManager<AppUser> userManager, ApplicationDbContext db)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _db = db;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    // Phase 20 — a single field accepts either a department access code (QR) or a
    // username. If it matches an active DepartmentAccessCode, route into the journey;
    // otherwise fall back to username + password sign-in.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
    {
        var input = (email ?? "").Trim();

        // QR / access-code path — no password required.
        if (!string.IsNullOrEmpty(input) && string.IsNullOrEmpty(password))
        {
            var code = await _db.DepartmentAccessCodes
                .FirstOrDefaultAsync(c => c.Code == input && c.IsActive && c.RevokedAt == null);
            if (code != null)
                return RedirectToAction("Index", "Journey", new { code = code.Code });
        }

        var result = await _signInManager.PasswordSignInAsync(input, password, true, false);
        if (result.Succeeded)
        {
            var user = await _userManager.FindByNameAsync(input);
            if (user != null)
            {
                user.LastLoginAt = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);
            }
            return Redirect(returnUrl ?? "/");
        }
        ModelState.AddModelError("", "اسم المستخدم أو كلمة المرور غير صحيحة");
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Login", "Account");
    }

    public IActionResult AccessDenied() => View();

    // Phase 20 — self-service profile: view identity fields, edit display name, change password.
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var u = await _userManager.GetUserAsync(User);
        if (u == null) return Challenge();
        return View(new ProfileViewModel
        {
            UserName = u.UserName ?? "",
            FullNameAr = u.FullNameAr,
            AppRole = u.AppRole.ToString(),
            JourneyScopeKey = u.JourneyScopeKey,
            LastLoginAt = u.LastLoginAt,
        });
    }

    [Authorize]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel m)
    {
        var u = await _userManager.GetUserAsync(User);
        if (u == null) return Challenge();

        // Always reflect immutable fields back to the view.
        m.UserName = u.UserName ?? "";
        m.AppRole = u.AppRole.ToString();
        m.JourneyScopeKey = u.JourneyScopeKey;
        m.LastLoginAt = u.LastLoginAt;

        // Display-name update.
        if (!string.IsNullOrWhiteSpace(m.FullNameAr) && m.FullNameAr != u.FullNameAr)
        {
            u.FullNameAr = m.FullNameAr;
            await _userManager.UpdateAsync(u);
        }

        // Optional password change.
        if (!string.IsNullOrEmpty(m.NewPassword) || !string.IsNullOrEmpty(m.CurrentPassword))
        {
            if (m.NewPassword != m.ConfirmPassword)
            {
                ModelState.AddModelError("", "كلمتا المرور الجديدتان غير متطابقتين.");
                return View(m);
            }
            var res = await _userManager.ChangePasswordAsync(u, m.CurrentPassword ?? "", m.NewPassword ?? "");
            if (!res.Succeeded)
            {
                ModelState.AddModelError("", string.Join(" / ", res.Errors.Select(e => e.Description)));
                return View(m);
            }
            await _signInManager.RefreshSignInAsync(u);
        }

        TempData["Success"] = "تم تحديث الملف الشخصي.";
        return RedirectToAction(nameof(Profile));
    }
}

public class ProfileViewModel
{
    public string UserName { get; set; } = "";
    public string FullNameAr { get; set; } = "";
    public string AppRole { get; set; } = "";
    public string? JourneyScopeKey { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
    public string? ConfirmPassword { get; set; }
}
