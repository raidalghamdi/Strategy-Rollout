using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 4 — admin curation of the auto-generated quiz bank.
[Authorize(Roles = "Admin,Facilitator")]
[Route("Admin/Quiz")]
public class AdminQuizController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly QuizGeneratorService _generator;

    public AdminQuizController(ApplicationDbContext db, QuizGeneratorService generator)
    {
        _db = db;
        _generator = generator;
    }

    // GET /Admin/Quiz?tab=Pending|Approved|Rejected
    [HttpGet("")]
    public async Task<IActionResult> Index(string tab = "Pending")
    {
        IQueryable<Domain.Entities.QuizQuestion> q = _db.QuizQuestions;
        q = tab switch
        {
            "Approved" => q.Where(x => x.IsApproved && x.IsActive),
            "Rejected" => q.Where(x => !x.IsActive),
            _ => q.Where(x => !x.IsApproved && x.IsActive),
        };
        ViewBag.Tab = tab;
        ViewBag.Counts = new QuizCounts
        {
            Pending = await _db.QuizQuestions.CountAsync(x => !x.IsApproved && x.IsActive),
            Approved = await _db.QuizQuestions.CountAsync(x => x.IsApproved && x.IsActive),
            Rejected = await _db.QuizQuestions.CountAsync(x => !x.IsActive),
        };
        var list = await q.OrderByDescending(x => x.CreatedAt).Take(400).ToListAsync();
        return View(list);
    }

    [HttpPost("Approve/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id, string tab = "Pending")
    {
        var x = await _db.QuizQuestions.FindAsync(id);
        if (x != null) { x.IsApproved = true; x.IsActive = true; await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index), new { tab });
    }

    [HttpPost("Reject/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid id, string tab = "Pending")
    {
        var x = await _db.QuizQuestions.FindAsync(id);
        if (x != null) { x.IsApproved = false; x.IsActive = false; await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index), new { tab });
    }

    [HttpPost("Edit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, string questionAr, string optionsCsv, int correctIndex, string? explanationAr, string tab = "Pending")
    {
        var x = await _db.QuizQuestions.FindAsync(id);
        if (x != null)
        {
            x.QuestionAr = questionAr;
            var opts = (optionsCsv ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (opts.Count >= 2)
            {
                x.OptionsJson = JsonSerializer.Serialize(opts);
                x.CorrectIndex = Math.Clamp(correctIndex, 0, opts.Count - 1);
            }
            x.ExplanationAr = explanationAr;
            x.Source = "Hand";
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index), new { tab });
    }

    [HttpPost("BulkApprove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkApprove(string scope = "General", string tab = "Pending")
    {
        var pending = _db.QuizQuestions.Where(x => !x.IsApproved && x.IsActive);
        if (scope != "All") pending = pending.Where(x => x.Scope == scope);
        await pending.ForEachAsync(x => x.IsApproved = true);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { tab });
    }

    [HttpPost("Generate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate()
    {
        var n = await _generator.GenerateAllAsync();
        TempData["Saved"] = n > 0 ? $"تم توليد {n} سؤالاً." : "بنك الأسئلة مكتمل بالفعل.";
        return RedirectToAction(nameof(Index));
    }
}

public class QuizCounts
{
    public int Pending { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
}
