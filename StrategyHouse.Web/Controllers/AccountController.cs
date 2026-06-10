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

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
    {
        var result = await _signInManager.PasswordSignInAsync(email, password, true, false);
        if (result.Succeeded)
            return Redirect(returnUrl ?? "/");
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
