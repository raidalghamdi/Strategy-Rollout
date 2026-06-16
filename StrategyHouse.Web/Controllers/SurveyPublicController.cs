using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 4 — anonymous, mobile-friendly public survey submission via per-survey token.
[AllowAnonymous]
public class SurveyPublicController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly DepartmentDirectoryService _depts;

    public SurveyPublicController(ApplicationDbContext db, DepartmentDirectoryService depts)
    {
        _db = db;
        _depts = depts;
    }

    // GET /s/{token}
    [HttpGet("s/{token}")]
    public async Task<IActionResult> Index(string token)
    {
        var survey = await _db.Surveys
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.PublicToken == token && s.IsActive);
        if (survey == null) return View("Closed");

        var now = DateTime.UtcNow;
        if ((survey.OpensAt != null && now < survey.OpensAt) || (survey.ClosesAt != null && now > survey.ClosesAt))
            return View("Closed");

        survey.Questions = survey.Questions.OrderBy(q => q.Order).ToList();
        var depts = await _depts.GetDepartmentsAsync();
        ViewBag.Departments = depts.Where(d => d.IsActive).OrderBy(d => d.DeptCode).ToList();
        return View(survey);
    }

    // POST /s/{token}
    [HttpPost("s/{token}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(string token, [FromForm] SurveySubmitForm form)
    {
        var survey = await _db.Surveys
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.PublicToken == token && s.IsActive);
        if (survey == null) return View("Closed");

        var fingerprint = Fingerprint();

        // Soft duplicate window: same fingerprint + survey within 60s.
        var cutoff = DateTime.UtcNow.AddSeconds(-60);
        bool recent = fingerprint != null && await _db.SurveyResponses.AnyAsync(r =>
            r.SurveyId == survey.Id && r.ClientFingerprint == fingerprint && r.SubmittedAt >= cutoff);
        if (recent) return View("ThankYou", survey.TitleAr);

        var answers = new List<object>();
        foreach (var q in survey.Questions)
        {
            var key = "q_" + q.Id;
            var val = form.Answers != null && form.Answers.TryGetValue(q.Id.ToString(), out var v) ? v : null;
            answers.Add(new { qid = q.Id.ToString(), value = val });
        }

        var response = new SurveyResponse
        {
            SurveyId = survey.Id,
            RespondentName = string.IsNullOrWhiteSpace(form.Name) ? null : form.Name.Trim(),
            RespondentRole = string.IsNullOrWhiteSpace(form.Role) ? null : form.Role.Trim(),
            DeptCode = string.IsNullOrWhiteSpace(form.DeptCode) ? null : form.DeptCode,
            AnswersJson = JsonSerializer.Serialize(answers),
            ClientFingerprint = fingerprint,
        };
        _db.SurveyResponses.Add(response);
        await _db.SaveChangesAsync();

        return View("ThankYou", survey.TitleAr);
    }

    private string? Fingerprint()
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var ua = Request.Headers.UserAgent.ToString();
        if (ip == "" && ua == "") return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ip + "|" + ua));
        return Convert.ToHexString(hash)[..32];
    }
}

public class SurveySubmitForm
{
    public string? Name { get; set; }
    public string? Role { get; set; }
    public string? DeptCode { get; set; }
    // keyed by question Guid string
    public Dictionary<string, string>? Answers { get; set; }
}
