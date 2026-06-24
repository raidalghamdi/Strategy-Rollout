using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Encodings.Web;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<AppUser> _signInManager;
    private readonly UserManager<AppUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _email;
    private readonly ILogger<AccountController> _log;

    // Phase 20.27 — admin accounts that are EXCLUDED from password reset / first-time
    // self-service flows. These accounts must only be managed by a database admin.
    private static readonly HashSet<string> ProtectedAdminUserNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin@gac.gov.sa",
        "admin",
        "gac.admin",
    };

    public AccountController(SignInManager<AppUser> signInManager, UserManager<AppUser> userManager,
        ApplicationDbContext db, IEmailSender email, ILogger<AccountController> log)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _db = db;
        _email = email;
        _log = log;
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
    [EnableRateLimiting("login")]
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

        // Phase 20.27 — first-time setup: if the username/email exists but PasswordHash
        // is NULL, route the user to the SetPassword page. Admin-protected accounts are
        // NEVER redirected here.
        if (!string.IsNullOrEmpty(input))
        {
            var existing = await _userManager.FindByNameAsync(input)
                            ?? await _userManager.FindByEmailAsync(input);
            if (existing != null
                && !ProtectedAdminUserNames.Contains(existing.UserName ?? "")
                && string.IsNullOrEmpty(existing.PasswordHash))
            {
                return RedirectToAction(nameof(SetPassword), new { u = existing.UserName });
            }
        }

        var result = await _signInManager.PasswordSignInAsync(input, password, true, true);
        if (result.Succeeded)
        {
            var user = await _userManager.FindByNameAsync(input);
            if (user != null)
            {
                user.LastLoginAt = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);
            }
            // Phase 20.7 — journey-only accounts (any user with JourneyScopeKey set, e.g.
            // gac.admin, vp.support, vp.economic, vp.legal, testing) land on the live
            // executive dashboard scoped to their JourneyScopeKey. The dashboard itself
            // has a department dropdown for drilling into a specific department.
            if (user != null && !string.IsNullOrEmpty(user.JourneyScopeKey))
            {
                return Redirect("/Admin/LiveDashboard");
            }
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
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

    // Phase 20.9 — also sign out on GET so legacy <a href="/Account/Logout"> links
    // and accidental GETs (e.g. when AntiForgery is missing) don't 404 / look like a
    // file download to the browser.
    [HttpGet]
    public async Task<IActionResult> Logout(bool _ = false)
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Login", "Account");
    }

    public IActionResult AccessDenied() => View();

    // ==================================================================================
    // Phase 20.27 — First-time password setup (no email required)
    // ==================================================================================
    [HttpGet]
    public async Task<IActionResult> SetPassword(string u)
    {
        if (string.IsNullOrWhiteSpace(u)) return RedirectToAction(nameof(Login));
        var user = await _userManager.FindByNameAsync(u);
        if (user == null || ProtectedAdminUserNames.Contains(user.UserName ?? ""))
            return RedirectToAction(nameof(Login));
        if (!string.IsNullOrEmpty(user.PasswordHash))
            return RedirectToAction(nameof(Login));
        return View(new SetPasswordViewModel { UserName = user.UserName ?? u });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> SetPassword(SetPasswordViewModel m)
    {
        if (m == null || string.IsNullOrWhiteSpace(m.UserName))
            return RedirectToAction(nameof(Login));

        var user = await _userManager.FindByNameAsync(m.UserName);
        if (user == null || ProtectedAdminUserNames.Contains(user.UserName ?? ""))
            return RedirectToAction(nameof(Login));

        if (!string.IsNullOrEmpty(user.PasswordHash))
        {
            TempData["Error"] = "لديك كلمة مرور بالفعل. استخدم رابط 'نسيت كلمة المرور؟' إذا نسيتها.";
            return RedirectToAction(nameof(Login));
        }

        if (m.NewPassword != m.ConfirmPassword)
        {
            ModelState.AddModelError("", "كلمتا المرور غير متطابقتين.");
            return View(m);
        }

        var res = await _userManager.AddPasswordAsync(user, m.NewPassword ?? "");
        if (!res.Succeeded)
        {
            foreach (var e in res.Errors) ModelState.AddModelError("", e.Description);
            return View(m);
        }

        await _signInManager.SignInAsync(user, isPersistent: true);
        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);
        _log.LogInformation("First-time password set for {User}", user.UserName);

        if (!string.IsNullOrEmpty(user.JourneyScopeKey))
            return Redirect("/Admin/LiveDashboard");
        return Redirect("/");
    }

    // ==================================================================================
    // Phase 20.27 — Forgot password / password reset (email-based)
    // ==================================================================================
    [HttpGet]
    public IActionResult ForgotPassword() => View();

    [HttpPost, ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ForgotPassword(string email)
    {
        var input = (email ?? "").Trim();
        if (!string.IsNullOrEmpty(input))
        {
            var user = await _userManager.FindByEmailAsync(input)
                       ?? await _userManager.FindByNameAsync(input);

            if (user != null
                && !ProtectedAdminUserNames.Contains(user.UserName ?? "")
                && !string.IsNullOrEmpty(user.Email))
            {
                try
                {
                    var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                    var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
                    var url = Url.Action(nameof(ResetPassword), "Account",
                        new { u = user.UserName, t = encoded }, Request.Scheme);
                    var safeName = HtmlEncoder.Default.Encode(user.FullNameAr ?? user.UserName ?? "");
                    var safeUrl = HtmlEncoder.Default.Encode(url ?? "");
                    var html = $@"<div dir='rtl' style='font-family:Tahoma,Arial,sans-serif;font-size:14px;line-height:1.8;'>
                        <p>مرحباً {safeName}،</p>
                        <p>استلمنا طلباً لإعادة تعيين كلمة المرور لحسابك في منصة الاستراتيجية.</p>
                        <p>اضغط على الرابط التالي لإعادة تعيين كلمة المرور (صالح لمدة ساعة):</p>
                        <p><a href='{safeUrl}' style='background:#5F9600;color:#fff;padding:10px 20px;border-radius:6px;text-decoration:none;display:inline-block;'>إعادة تعيين كلمة المرور</a></p>
                        <p style='color:#666;font-size:12px;'>أو انسخ والصق هذا الرابط: {safeUrl}</p>
                        <p style='color:#666;font-size:12px;'>إذا لم تطلب إعادة التعيين، تجاهل هذه الرسالة.</p>
                    </div>";
                    await _email.SendAsync(user.Email, "إعادة تعيين كلمة المرور — منصة الاستراتيجية", html);
                    _log.LogInformation("Password reset email sent to {Email}", user.Email);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to generate / send reset email for {Input}", input);
                }
            }
        }

        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [HttpGet]
    public IActionResult ForgotPasswordConfirmation() => View();

    [HttpGet]
    public async Task<IActionResult> ResetPassword(string u, string t)
    {
        if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(t))
            return RedirectToAction(nameof(Login));

        var user = await _userManager.FindByNameAsync(u);
        if (user == null || ProtectedAdminUserNames.Contains(user.UserName ?? ""))
        {
            TempData["Error"] = "رابط غير صالح.";
            return RedirectToAction(nameof(Login));
        }

        return View(new ResetPasswordViewModel { UserName = u, Token = t });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel m)
    {
        if (m == null || string.IsNullOrWhiteSpace(m.UserName) || string.IsNullOrWhiteSpace(m.Token))
            return RedirectToAction(nameof(Login));

        var user = await _userManager.FindByNameAsync(m.UserName);
        if (user == null || ProtectedAdminUserNames.Contains(user.UserName ?? ""))
        {
            ModelState.AddModelError("", "رابط غير صالح.");
            return View(m);
        }

        if (m.NewPassword != m.ConfirmPassword)
        {
            ModelState.AddModelError("", "كلمتا المرور غير متطابقتين.");
            return View(m);
        }

        string decodedToken;
        try { decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(m.Token)); }
        catch { ModelState.AddModelError("", "رابط غير صالح أو منتهي الصلاحية."); return View(m); }

        var res = await _userManager.ResetPasswordAsync(user, decodedToken, m.NewPassword ?? "");
        if (!res.Succeeded)
        {
            foreach (var e in res.Errors) ModelState.AddModelError("", e.Description);
            return View(m);
        }

        TempData["Success"] = "تمت إعادة تعيين كلمة المرور بنجاح. يمكنك تسجيل الدخول الآن.";
        _log.LogInformation("Password reset succeeded for {User}", user.UserName);
        return RedirectToAction(nameof(Login));
    }

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

// Phase 20.27 — first-time password setup
public class SetPasswordViewModel
{
    public string UserName { get; set; } = "";
    public string? NewPassword { get; set; }
    public string? ConfirmPassword { get; set; }
}

// Phase 20.27 — password reset via email
public class ResetPasswordViewModel
{
    public string UserName { get; set; } = "";
    public string Token { get; set; } = "";
    public string? NewPassword { get; set; }
    public string? ConfirmPassword { get; set; }
}
