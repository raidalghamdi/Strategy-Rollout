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
        ViewBag.Total = await _db.QuizQuestions.CountAsync();
        ViewBag.Departments = await _db.Departments.OrderBy(d => d.DeptCode)
            .Select(d => new DeptOption { Code = d.DeptCode, Name = d.NameAr ?? d.DeptCode }).ToListAsync();
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
    public async Task<IActionResult> Edit(Guid id, string questionAr, string optionsCsv, int correctIndex,
        string? explanationAr, string? scope, string? deptCode, string tab = "Pending")
    {
        var x = await _db.QuizQuestions.FindAsync(id);
        if (x != null)
        {
            x.QuestionAr = questionAr;
            var opts = x.QuestionType == "TrueFalse"
                ? new List<string> { "صح", "خطأ" }
                : (optionsCsv ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (opts.Count >= 2)
            {
                x.OptionsJson = JsonSerializer.Serialize(opts);
                x.CorrectIndex = Math.Clamp(correctIndex, 0, opts.Count - 1);
            }
            x.ExplanationAr = explanationAr;
            if (scope is "General" or "Department")
            {
                x.Scope = scope;
                x.DeptCodeFilter = scope == "Department" ? deptCode : null;
            }
            if (x.Source != "Manual") x.Source = "Hand";
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

    // POST /Admin/Quiz/Regenerate — explicit auto-generation (idempotent guard removed).
    [HttpPost("Regenerate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Regenerate()
    {
        var n = await _generator.GenerateAllAsync();
        TempData["Saved"] = n > 0 ? $"تم توليد {n} سؤالاً تلقائياً." : "بنك الأسئلة مكتمل بالفعل (لم تتم إضافة أسئلة جديدة).";
        return RedirectToAction(nameof(Index));
    }

    // POST /Admin/Quiz/Reset — destructive: truncate question bank + attempts.
    [HttpPost("Reset")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset()
    {
        var attempts = await _db.QuizAttempts.CountAsync();
        var questions = await _db.QuizQuestions.CountAsync();
        await _db.QuizAttempts.ExecuteDeleteAsync();
        await _db.QuizQuestions.ExecuteDeleteAsync();
        TempData["Saved"] = $"تم حذف {questions} سؤالاً و {attempts} محاولة.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Admin/Quiz/Create — admin-authored question (pre-approved).
    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string scope, string? deptCode, string questionType,
        string questionAr, string optionsCsv, int correctIndex, string? explanationAr)
    {
        scope = scope == "Department" ? "Department" : "General";
        var type = questionType == "TrueFalse" ? "TrueFalse" : "MCQ";
        List<string> opts = type == "TrueFalse"
            ? new List<string> { "صح", "خطأ" }
            : (optionsCsv ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        if (string.IsNullOrWhiteSpace(questionAr) || opts.Count < 2)
        {
            TempData["Error"] = "السؤال يحتاج نصاً وخيارين على الأقل.";
            return RedirectToAction(nameof(Index));
        }

        _db.QuizQuestions.Add(new Domain.Entities.QuizQuestion
        {
            Scope = scope,
            DeptCodeFilter = scope == "Department" ? deptCode : null,
            QuestionType = type,
            QuestionAr = questionAr.Trim(),
            OptionsJson = JsonSerializer.Serialize(opts),
            CorrectIndex = Math.Clamp(correctIndex, 0, opts.Count - 1),
            ExplanationAr = string.IsNullOrWhiteSpace(explanationAr) ? null : explanationAr.Trim(),
            IsApproved = true,
            IsActive = true,
            Source = "Manual",
        });
        await _db.SaveChangesAsync();
        TempData["Saved"] = "تمت إضافة السؤال.";
        return RedirectToAction(nameof(Index), new { tab = "Approved" });
    }

    // POST /Admin/Quiz/Delete/{id} — soft delete to preserve attempt history.
    [HttpPost("Delete/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, string tab = "Pending")
    {
        var x = await _db.QuizQuestions.FindAsync(id);
        if (x != null) { x.IsActive = false; x.IsApproved = false; await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index), new { tab });
    }
}

public class QuizCounts
{
    public int Pending { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
}

public class DeptOption
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}
