using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 4 — public-facing quiz. Anonymous; one-per-screen on the client.
// Phase 16 — questions come from the hard-coded QuizQuestionsProvider instead of
// the database, so the page is never blank waiting on an admin to approve a bank.
[AllowAnonymous]
public class QuizController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly PageContentService _pageContent;
    private readonly QrService _qr;
    private readonly QuizTemplateService _templates;
    private readonly IMemoryCache _cache;

    public QuizController(
        ApplicationDbContext db,
        PageContentService pageContent,
        QrService qr,
        QuizTemplateService templates,
        IMemoryCache cache)
    {
        _db = db;
        _pageContent = pageContent;
        _qr = qr;
        _templates = templates;
        _cache = cache;
    }

    // Phase 20.11 — when the expanded bank is on, we cache the generated
    // question (incl. correct index + explanation) by its Guid so /Submit
    // can grade it without trusting the client. 2-hour sliding window is
    // generous enough for slow respondents and is automatically refreshed
    // every time the same Id is read.
    private static readonly TimeSpan QuizCacheTtl = TimeSpan.FromHours(2);

    private bool ExpandedBankEnabled()
        => string.Equals(_pageContent.Get("quiz.bank.useExpanded", "false"), "true", StringComparison.OrdinalIgnoreCase);

    private static string QuizCacheKey(Guid qid) => $"quiz.dyn.{qid}";

    // GET /Quiz — landing alias so the bare /Quiz URL never 404s. Sends visitors
    // into the standalone quiz; admins manage the bank at /Admin/Quiz.
    [HttpGet("Quiz")]
    public IActionResult Index()
    {
        if (User.IsInRole("Admin") || User.IsInRole("Facilitator"))
            return Redirect("/Admin/Quiz");
        return RedirectToAction(nameof(Start), new { sessionId = (Guid?)null });
    }

    // GET /Quiz/Start/{sessionId?}
    // Phase 8 — render the quiz questions directly on this page (no intro gate).
    // Anonymous learning tool; works standalone whether or not a session exists.
    // Phase 10.2 — accept any string segment so a non-GUID id (e.g. /Quiz/Start/1)
    // still renders the standalone quiz instead of 404ing; parse the GUID best-effort.
    [HttpGet("Quiz/Start/{sessionId?}")]
    public async Task<IActionResult> Start(string? sessionId)
    {
        Guid? sessionGuid = Guid.TryParse(sessionId, out var parsed) ? parsed : null;
        string? deptCode = null;
        if (sessionGuid != null)
        {
            var session = await _db.StrategySessions.FindAsync(sessionGuid.Value);
            if (session != null) deptCode = session.DeptCode;
        }

        List<QuizQuestion> picked;
        if (ExpandedBankEnabled())
        {
            // Phase 20.11 — pick 5 random from the live 200-template bank.
            // Domains 4 & 5 are auto-filtered to the user's department; if no
            // deptCode is available (anonymous quiz), they're skipped.
            var bank = await _templates.GenerateForUserAsync(deptCode);
            picked = bank.OrderBy(_ => Guid.NewGuid()).Take(5).ToList();
            // Cache each picked question (full record incl. CorrectIndex/explanation)
            // so /Quiz/Submit can grade against server-side truth.
            foreach (var q in picked)
            {
                _cache.Set(QuizCacheKey(q.Id), q, QuizCacheTtl);
            }
        }
        else
        {
            picked = QuizQuestionsProvider.GetRandom(10);
        }

        ViewBag.SessionId = sessionGuid;
        ViewBag.Scope = "General";
        ViewBag.DeptCode = deptCode;

        // Phase 19.15 — surface the admin-editable survey URL plus a pre-rendered
        // QR code (base64 PNG data URI) so the thank-you screen shown after the
        // user finishes the quiz can be 100% offline-friendly. PageContentService
        // returns either the saved value or the seeded default, so this is safe
        // even before an admin has customized the URL.
        var surveyUrl = _pageContent.Get("quiz.survey.url");
        ViewBag.SurveyUrl = surveyUrl;
        ViewBag.SurveyTitle = _pageContent.Get("quiz.survey.title");
        ViewBag.SurveyBody = _pageContent.Get("quiz.survey.body");

        // Phase 19.25 — if an admin has uploaded a custom QR image AND toggled it
        // on (quiz.survey.qr.useCustom == "true"), use that data URI instead of
        // auto-generating from the URL. Falls back gracefully to the generated QR.
        var useCustom = string.Equals(
            _pageContent.Get("quiz.survey.qr.useCustom", "false"),
            "true", StringComparison.OrdinalIgnoreCase);
        var customQr = _pageContent.Get("quiz.survey.qr.custom", "");
        if (useCustom && !string.IsNullOrEmpty(customQr))
        {
            ViewBag.SurveyQrDataUri = customQr;
        }
        else
        {
            try
            {
                ViewBag.SurveyQrDataUri = _qr.GenerateBase64Png(surveyUrl, pixelsPerModule: 6);
            }
            catch
            {
                // QR generation is best-effort — if it fails the view falls back to
                // the link-only layout. Never let it block the quiz from rendering.
                ViewBag.SurveyQrDataUri = null;
            }
        }

        return View(picked);
    }

    // GET /Quiz/Play — legacy entry point; Phase 8 unified the quiz onto Start.
    [HttpGet("Quiz/Play")]
    public IActionResult Play(Guid? sessionId)
        => RedirectToAction(nameof(Start), new { sessionId });

    // GET /Quiz/Questions — Phase 19: JSON feed of the active questions so the quiz
    // can be embedded inline (e.g. stage 5 of the journey) without a full page render.
    // Returns the same shape the standalone view serialises ({ id, text, options }).
    [HttpGet("Quiz/Questions")]
    public async Task<IActionResult> Questions(int count = 5, string? deptCode = null)
    {
        if (count < 1) count = 1;
        if (count > 50) count = 50;

        List<QuizQuestion> source;
        if (ExpandedBankEnabled())
        {
            var bank = await _templates.GenerateForUserAsync(deptCode);
            source = bank.OrderBy(_ => Guid.NewGuid()).Take(count).ToList();
            foreach (var q in source)
            {
                _cache.Set(QuizCacheKey(q.Id), q, QuizCacheTtl);
            }
        }
        else
        {
            source = QuizQuestionsProvider.GetRandom(count).ToList();
        }

        var picked = source.Select(q => new
        {
            id = q.Id,
            text = q.QuestionAr,
            options = System.Text.Json.JsonSerializer.Deserialize<List<string>>(q.OptionsJson) ?? new List<string>(),
        });
        return Json(new { ok = true, questions = picked });
    }

    // POST /Quiz/Submit — grade server-side, never trust client.
    [HttpPost("Quiz/Submit")]
    public async Task<IActionResult> Submit([FromBody] QuizSubmitDto dto)
    {
        if (dto.Answers == null || dto.Answers.Count == 0) return BadRequest();
        var ids = dto.Answers.Select(a => a.Qid).ToHashSet();
        // Phase 20.11 — accept both static-bank and expanded-bank questions.
        // Static bank is matched by Id from QuizQuestionsProvider.All; expanded
        // bank questions live in IMemoryCache from when /Start or /Questions
        // generated them. Both lookups are server-side, so the client can
        // never inject a fabricated CorrectIndex.
        var questions = QuizQuestionsProvider.All.Where(q => ids.Contains(q.Id)).ToList();
        foreach (var id in ids)
        {
            if (questions.Any(q => q.Id == id)) continue;
            if (_cache.TryGetValue<QuizQuestion>(QuizCacheKey(id), out var dyn) && dyn != null)
            {
                questions.Add(dyn);
            }
        }

        int score = 0;
        var detail = new List<object>();
        foreach (var ans in dto.Answers)
        {
            var q = questions.FirstOrDefault(x => x.Id == ans.Qid);
            if (q == null) continue;
            bool correct = ans.Picked == q.CorrectIndex;
            if (correct) score++;
            detail.Add(new { qid = q.Id, picked = ans.Picked, correctIndex = q.CorrectIndex, correct, explanation = q.ExplanationAr });
        }

        var attempt = new QuizAttempt
        {
            SessionId = dto.SessionId,
            RespondentName = dto.RespondentName,
            Scope = dto.Scope ?? "General",
            DeptCode = dto.DeptCode,
            Score = score,
            Total = dto.Answers.Count,
            AnswersJson = JsonSerializer.Serialize(detail),
        };
        _db.QuizAttempts.Add(attempt);

        // Phase 20.33 — mark the strategy session as complete the moment the
        // quiz is submitted (one quiz attempt per session). Previously CompletedAt
        // was only set after the map-sign + Complete page redirect, which left
        // many sessions stuck at CompletedAt=NULL when facilitators closed the
        // tab right after signing. Tying completion to quiz submission also
        // makes the executive report's "avg completion time" reflect the actual
        // learner experience end-to-end.
        if (dto.SessionId.HasValue)
        {
            var session = await _db.StrategySessions.FindAsync(dto.SessionId.Value);
            if (session != null && session.CompletedAt == null)
            {
                session.CompletedAt = DateTime.UtcNow;
                session.LastActivityAt = DateTime.UtcNow;
                if (string.Equals(session.Status, "InProgress", StringComparison.OrdinalIgnoreCase))
                {
                    session.Status = "Completed";
                }
            }
        }

        await _db.SaveChangesAsync();

        return Json(new { ok = true, score, total = dto.Answers.Count, detail });
    }
}

public class QuizSubmitDto
{
    public Guid? SessionId { get; set; }
    public string? RespondentName { get; set; }
    public string? Scope { get; set; }
    public string? DeptCode { get; set; }
    public List<QuizAnswerDto> Answers { get; set; } = new();
}

public class QuizAnswerDto
{
    public Guid Qid { get; set; }
    public int Picked { get; set; }
}
