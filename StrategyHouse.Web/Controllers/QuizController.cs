using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Controllers;

// Phase 4 — public-facing quiz. Anonymous; questions drawn server-side from the
// approved pool only. 10 random questions, one-per-screen on the client.
[AllowAnonymous]
public class QuizController : Controller
{
    private readonly ApplicationDbContext _db;

    public QuizController(ApplicationDbContext db) { _db = db; }

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

        var all = await _db.QuizQuestions
            .Where(q => q.IsApproved && q.IsActive && q.Scope == "General")
            .ToListAsync();
        var rnd = new Random(Random.Shared.Next());
        var picked = all.OrderBy(_ => rnd.Next()).Take(10).ToList();

        ViewBag.SessionId = sessionGuid;
        ViewBag.Scope = "General";
        ViewBag.DeptCode = deptCode;
        return View(picked);
    }

    // GET /Quiz/Play — legacy entry point; Phase 8 unified the quiz onto Start.
    [HttpGet("Quiz/Play")]
    public IActionResult Play(Guid? sessionId)
        => RedirectToAction(nameof(Start), new { sessionId });

    // POST /Quiz/Submit — grade server-side, never trust client.
    [HttpPost("Quiz/Submit")]
    public async Task<IActionResult> Submit([FromBody] QuizSubmitDto dto)
    {
        if (dto.Answers == null || dto.Answers.Count == 0) return BadRequest();
        var ids = dto.Answers.Select(a => a.Qid).ToList();
        var questions = await _db.QuizQuestions.Where(q => ids.Contains(q.Id)).ToListAsync();

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
