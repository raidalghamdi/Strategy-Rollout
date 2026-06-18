using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    public QuizController(ApplicationDbContext db, PageContentService pageContent, QrService qr)
    {
        _db = db;
        _pageContent = pageContent;
        _qr = qr;
    }

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

        var picked = QuizQuestionsProvider.GetRandom(10);

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
    public IActionResult Questions(int count = 5)
    {
        if (count < 1) count = 1;
        if (count > 50) count = 50;
        var picked = QuizQuestionsProvider.GetRandom(count).Select(q => new
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
        var questions = QuizQuestionsProvider.All.Where(q => ids.Contains(q.Id)).ToList();

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
