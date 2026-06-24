using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// =====================================================================
// Phase 20.29 — Email-only access to the department journey.
//
// Goal (per user): "instead everyone is entered by the department code
// like GAC202 they entered by their emails without passwords and
// anything else." No magic link, no password, no department code.
//
// Flow:
//   GET  /Journey/Access  → simple Arabic RTL email form
//   POST /Journey/Access  → look up DepartmentRoster by EmailNormalized;
//                            if found, mint a StrategySession for that
//                            DeptCode (just like /Journey/Start does for
//                            a code) and redirect to /Journey/Run.
//
// The email is unique across the roster (filtered unique index).
// Failure returns a generic message — no email enumeration.
// =====================================================================
[AllowAnonymous]
public class JourneyAccessController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly StrategyContentService _content;
    private readonly ILogger<JourneyAccessController> _logger;

    public JourneyAccessController(
        ApplicationDbContext db,
        StrategyContentService content,
        ILogger<JourneyAccessController> logger)
    {
        _db = db;
        _content = content;
        _logger = logger;
    }

    // GET /Journey/Access — email-only form.
    [HttpGet("Journey/Access")]
    public IActionResult Index(string? email)
    {
        ViewBag.Email = email;
        ViewBag.Content = _content;
        return View();
    }

    // POST /Journey/Access — look up the member by email, create a
    // department session, then redirect into the journey.
    [HttpPost("Journey/Access")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(string email)
    {
        var raw = (email ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            TempData["Error"] = "الرجاء إدخال البريد الإلكتروني.";
            return RedirectToAction(nameof(Index));
        }
        var normalized = raw.ToLowerInvariant();

        var member = await _db.DepartmentRoster
            .FirstOrDefaultAsync(r => r.EmailNormalized == normalized && r.IsActive);

        if (member == null || string.IsNullOrWhiteSpace(member.DeptCode))
        {
            // Generic message — no enumeration of which emails exist.
            TempData["Error"] = "تعذّر العثور على هذا البريد. تواصل مع المسؤول للتسجيل.";
            return RedirectToAction(nameof(Index), new { email = raw });
        }

        // Mirror /Journey/Start: stamp ownership if a platform user
        // happens to be signed in (rare for kiosk flow, but kept for parity).
        int? ownerId = null;
        var idClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (User.Identity?.IsAuthenticated == true && int.TryParse(idClaim, out var uid))
            ownerId = uid;

        var session = new StrategySession
        {
            DeptCode = member.DeptCode,
            // No DepartmentAccessCodes row was used; stamp the email so
            // the audit trail still shows how this session was started.
            AccessCodeUsed = $"EMAIL:{member.EmailNormalized}",
            OwnerUserId = ownerId,
        };
        _db.StrategySessions.Add(session);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Journey email-access: member {MemberId} ({Name}) dept={Dept} → session {SessionId}",
            member.MemberId, member.NameAr, member.DeptCode, session.Id);

        // Phase 20.30 — the journey Run action is routed as a path segment:
        //   [HttpGet("Journey/Run/{sessionId:guid}")]
        // The earlier query-string URL (/Journey/Run?sessionId=...) didn't match
        // any route on production (Railway), so the browser fell through to a
        // static-file fallback that prompted to download a file named "Run".
        // RedirectToAction emits the correct path-segment URL and proper
        // 302 + Location headers.
        return RedirectToAction("Run", "Journey", new { sessionId = session.Id });
    }
}
