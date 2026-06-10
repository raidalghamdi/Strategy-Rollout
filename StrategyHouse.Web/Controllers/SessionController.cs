using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Domain.Enums;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

/// <summary>
/// In-session experience: the three movements (baseline check, Map, commitments)
/// plus the QR-driven survey and the same-day quiz link. Public access via session
/// AccessCode for in-room iPads and attendee phones; no login required for these
/// flows because attendees are physically in the controlled room.
/// </summary>
public class SessionController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly QrService _qr;
    private readonly IConfiguration _config;

    public SessionController(ApplicationDbContext db, QrService qr, IConfiguration config)
    {
        _db = db; _qr = qr; _config = config;
    }

    // === In-room console ===
    [HttpGet("Session/Console")]
    public async Task<IActionResult> Console(string code)
    {
        var session = await GetSessionByCode(code);
        if (session == null) return NotFound();
        var baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        ViewBag.BaselineQr = _qr.GenerateBase64Png($"{baseUrl}/Session/Baseline?code={code}");
        ViewBag.SurveyQr = _qr.GenerateBase64Png($"{baseUrl}/Session/Survey?code={code}");
        return View(session);
    }

    // === Movement 1 — anonymous baseline check ===
    [HttpGet("Session/Baseline")]
    public async Task<IActionResult> Baseline(string code)
    {
        var session = await GetSessionByCode(code);
        if (session == null) return NotFound();
        return View(session);
    }

    [HttpPost("Session/Baseline")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitBaseline(string code, string? pillar, string? value, int? roleAware)
    {
        var session = await GetSessionByCode(code);
        if (session == null) return NotFound();
        _db.BaselineResponses.Add(new BaselineResponse
        {
            SessionId = session.Id,
            Q1_PillarNamed = pillar,
            Q2_ValueNamed = value,
            Q3_RoleAwarenessRating = roleAware,
        });
        await _db.SaveChangesAsync();
        return RedirectToAction("BaselineThanks");
    }

    public IActionResult BaselineThanks() => View();

    // === Movement 2 — collaborative Map canvas (per-department) ===
    [HttpGet("Session/Map")]
    public async Task<IActionResult> Map(string code, int departmentId)
    {
        var session = await _db.Sessions
            .Include(s => s.Framework).ThenInclude(f => f!.Layers).ThenInclude(l => l.Elements)
            .FirstOrDefaultAsync(s => s.AccessCode == code);
        if (session == null) return NotFound();

        var dept = await _db.Departments
            .Include(d => d.Projects)
            .Include(d => d.Kpis)
            .Include(d => d.Roles)
            .FirstOrDefaultAsync(d => d.Id == departmentId);
        if (dept == null) return NotFound();

        var map = await _db.StrategyMaps
            .Include(m => m.Placements)
            .Include(m => m.Commitments).ThenInclude(c => c.LinkedElement)
            .Include(m => m.Signatures)
            .FirstOrDefaultAsync(m => m.SessionId == session.Id && m.DepartmentId == departmentId);

        if (map == null)
        {
            map = new StrategyMap { SessionId = session.Id, DepartmentId = departmentId };
            _db.StrategyMaps.Add(map);
            await _db.SaveChangesAsync();
        }

        ViewBag.Session = session;
        ViewBag.Department = dept;
        ViewBag.Map = map;
        return View(map);
    }

    [HttpPost("Session/AddPlacement")]
    public async Task<IActionResult> AddPlacement(int mapId, int elementId, string kind, int? projectId, int? kpiId, int? roleId, string? customLabel)
    {
        var placement = new MapPlacement
        {
            StrategyMapId = mapId,
            FrameworkElementId = elementId,
            PlacementKind = kind,
            ProjectId = projectId,
            KpiId = kpiId,
            RoleId = roleId,
            CustomLabelAr = customLabel,
        };
        _db.MapPlacements.Add(placement);
        await _db.SaveChangesAsync();
        return Json(new { ok = true, id = placement.Id });
    }

    [HttpPost("Session/RemovePlacement")]
    public async Task<IActionResult> RemovePlacement(int placementId)
    {
        var p = await _db.MapPlacements.FindAsync(placementId);
        if (p != null) { _db.MapPlacements.Remove(p); await _db.SaveChangesAsync(); }
        return Json(new { ok = true });
    }

    // === Movement 3 — Commitments + signing ===
    [HttpGet("Session/Commit")]
    public async Task<IActionResult> Commit(string code, int departmentId)
    {
        var session = await GetSessionByCode(code);
        if (session == null) return NotFound();
        var dept = await _db.Departments.FindAsync(departmentId);
        if (dept == null) return NotFound();

        var map = await _db.StrategyMaps
            .Include(m => m.Placements)
            .Include(m => m.Commitments).ThenInclude(c => c.LinkedElement)
            .Include(m => m.Signatures)
            .FirstOrDefaultAsync(m => m.SessionId == session.Id && m.DepartmentId == departmentId);
        if (map == null) return NotFound();

        var commitments = await _db.CommitmentTemplates
            .Where(c => c.IsActive && (c.DepartmentId == null || c.DepartmentId == departmentId))
            .OrderBy(c => c.Order)
            .ToListAsync();

        var framework = await _db.Frameworks
            .Include(f => f.Layers).ThenInclude(l => l.Elements)
            .FirstAsync(f => f.Id == session.FrameworkId);

        ViewBag.Session = session;
        ViewBag.Department = dept;
        ViewBag.Map = map;
        ViewBag.Commitments = commitments;
        ViewBag.Framework = framework;
        return View();
    }

    [HttpPost("Session/AddCommitment")]
    public async Task<IActionResult> AddCommitment(int mapId, int? templateId, string text, int linkType, int linkedElementId)
    {
        _db.MapCommitments.Add(new MapCommitment
        {
            StrategyMapId = mapId,
            CommitmentTemplateId = templateId,
            TextAr = text,
            LinkType = (CommitmentLinkType)linkType,
            LinkedElementId = linkedElementId,
        });
        await _db.SaveChangesAsync();
        return Json(new { ok = true });
    }

    [HttpPost("Session/Sign")]
    public async Task<IActionResult> Sign(int mapId, string signerName, string signaturePng)
    {
        _db.MapSignatures.Add(new MapSignature
        {
            StrategyMapId = mapId,
            SignerNameAr = signerName,
            SignaturePngBase64 = signaturePng,
        });
        await _db.SaveChangesAsync();
        return Json(new { ok = true });
    }

    [HttpPost("Session/Finalize")]
    public async Task<IActionResult> Finalize(int mapId)
    {
        var m = await _db.StrategyMaps.FindAsync(mapId);
        if (m == null) return NotFound();
        m.IsFinalized = true;
        m.FinalizedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // === Survey (QR-driven, end of session) ===
    [HttpGet("Session/Survey")]
    public async Task<IActionResult> Survey(string code)
    {
        var session = await GetSessionByCode(code);
        if (session == null) return NotFound();
        var survey = await _db.Surveys
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.IsActive);
        if (survey == null) return NotFound();
        ViewBag.Session = session;
        return View(survey);
    }

    [HttpPost("Session/Survey")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitSurvey(string code, int surveyId, Dictionary<int, string> answers)
    {
        var session = await GetSessionByCode(code);
        if (session == null) return NotFound();

        var response = new SurveyResponse { SessionId = session.Id, SurveyId = surveyId };
        _db.SurveyResponses.Add(response);
        await _db.SaveChangesAsync();

        var questions = await _db.SurveyQuestions.Where(q => q.SurveyId == surveyId).ToListAsync();
        foreach (var q in questions)
        {
            if (!answers.TryGetValue(q.Id, out var raw) || string.IsNullOrWhiteSpace(raw)) continue;
            var ans = new SurveyAnswer { SurveyResponseId = response.Id, SurveyQuestionId = q.Id };
            switch (q.Type)
            {
                case SurveyQuestionType.Rating:
                    if (int.TryParse(raw, out var rv)) ans.RatingValue = rv;
                    break;
                case SurveyQuestionType.SingleChoice:
                case SurveyQuestionType.MultiChoice:
                    ans.ChoiceValue = raw;
                    break;
                case SurveyQuestionType.OpenText:
                    ans.OpenText = raw;
                    break;
            }
            _db.SurveyAnswers.Add(ans);
        }
        await _db.SaveChangesAsync();
        return RedirectToAction("SurveyThanks");
    }

    public IActionResult SurveyThanks() => View();

    // === Quiz (same-day, optional, anonymous) ===
    [HttpGet("Session/Quiz")]
    public async Task<IActionResult> Quiz(string code)
    {
        var session = await GetSessionByCode(code);
        if (session == null) return NotFound();
        var quiz = await _db.Quizzes
            .Include(q => q.Questions)
            .FirstOrDefaultAsync(q => q.IsActive);
        if (quiz == null) return NotFound();
        ViewBag.Session = session;
        return View(quiz);
    }

    [HttpPost("Session/Quiz")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitQuiz(string code, int quizId, Dictionary<int, string> answers)
    {
        var session = await GetSessionByCode(code);
        if (session == null) return NotFound();

        var response = new QuizResponse { SessionId = session.Id, QuizId = quizId };
        _db.QuizResponses.Add(response);
        await _db.SaveChangesAsync();

        var questions = await _db.QuizQuestions.Where(q => q.QuizId == quizId).ToListAsync();
        var correctCount = 0;
        foreach (var q in questions)
        {
            if (!answers.TryGetValue(q.Id, out var raw)) continue;
            var correct = q.CorrectOptionsJson;
            var isCorrect = $"[{raw}]" == correct;
            if (isCorrect) correctCount++;
            _db.QuizAnswers.Add(new QuizAnswer
            {
                QuizResponseId = response.Id,
                QuizQuestionId = q.Id,
                SelectedOptionsJson = $"[{raw}]",
                IsCorrect = isCorrect,
            });
        }
        await _db.SaveChangesAsync();
        ViewBag.Total = questions.Count;
        ViewBag.Correct = correctCount;
        return View("QuizThanks");
    }

    private Task<Session?> GetSessionByCode(string code)
        => _db.Sessions.FirstOrDefaultAsync(s => s.AccessCode == code);
}
