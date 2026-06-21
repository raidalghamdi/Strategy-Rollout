using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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

    // Phase 19.27 (security) — the "login" policy throttles brute-force attempts;
    // see AddRateLimiter() in Program.cs for the actual limit.
    [HttpPost, ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
    {
        // Phase 19.27 (security):
        //   3rd arg (isPersistent) — false: do not issue a "remember me" cookie by default.
        //   4th arg (lockoutOnFailure) — true: feed failed attempts into the Identity lockout counter.
        var result = await _signInManager.PasswordSignInAsync(email, password, false, true);
        if (result.Succeeded)
        {
            // Phase 19.27 (security) — only honour returnUrl if it points to our own site;
            // anything off-site is silently rewritten to "/" to block open-redirect abuse.
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
