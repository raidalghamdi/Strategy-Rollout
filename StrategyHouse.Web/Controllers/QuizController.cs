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

    // GET /Quiz/Start/{sessionId?}
    [HttpGet("Quiz/Start/{sessionId:guid?}")]
    public async Task<IActionResult> Start(Guid? sessionId)
    {
        string? deptCode = null;
        if (sessionId != null)
        {
            var session = await _db.StrategySessions.FindAsync(sessionId.Value);
            if (session != null) deptCode = session.DeptCode;
        }
        ViewBag.SessionId = sessionId;
        ViewBag.DeptCode = deptCode;
        ViewBag.HasDeptQuestions = deptCode != null &&
            await _db.QuizQuestions.AnyAsync(q => q.IsApproved && q.IsActive && q.Scope == "Department" && q.DeptCodeFilter == deptCode);
        return View();
    }

    // GET /Quiz/Play — returns a quiz with 10 random approved questions for the chosen scope.
    [HttpGet("Quiz/Play")]
    public async Task<IActionResult> Play(Guid? sessionId, string scope = "General")
    {
        string? deptCode = null;
        if (sessionId != null)
        {
            var session = await _db.StrategySessions.FindAsync(sessionId.Value);
            deptCode = session?.DeptCode;
        }

        IQueryable<QuizQuestion> pool = _db.QuizQuestions.Where(q => q.IsApproved && q.IsActive);
        if (scope == "Department" && deptCode != null)
            pool = pool.Where(q => q.Scope == "Department" && q.DeptCodeFilter == deptCode);
        else
            pool = pool.Where(q => q.Scope == "General");

        var all = await pool.ToListAsync();
        var seed = Random.Shared.Next();
        var rnd = new Random(seed);
        var picked = all.OrderBy(_ => rnd.Next()).Take(10).ToList();

        ViewBag.SessionId = sessionId;
        ViewBag.Scope = scope;
        ViewBag.DeptCode = deptCode;
        return View(picked);
    }

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
