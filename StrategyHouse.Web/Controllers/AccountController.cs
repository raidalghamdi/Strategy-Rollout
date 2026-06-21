using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using StrategyHouse.Domain.Entities;

namespace StrategyHouse.Web.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<AppUser> _signInManager;
    private readonly UserManager<AppUser> _userManager;

    public AccountController(SignInManager<AppUser> signInManager, UserManager<AppUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    // Phase 19.27 (security) — keep:
    //   * isPersistent=false: no automatic "remember me" cookie.
    //   * lockoutOnFailure=true: failed attempts feed Identity's lockout counter.
    //   * LocalRedirect + Url.IsLocalUrl: blocks open-redirect via returnUrl.
    // Phase 19.29 — removed [EnableRateLimiting("login")] because the rate-limiter
    // pipeline in 19.27 broke the Railway deploy; we'll reintroduce it carefully
    // later. The lockout policy still throttles brute-force attempts.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
    {
        var result = await _signInManager.PasswordSignInAsync(email, password, false, true);
        if (result.Succeeded)
        {
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
        }
        ModelState.AddModelError("", "البريد الإلكتروني أو كلمة المرور غير صحيحة");
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    public IActionResult AccessDenied() => View();
}
