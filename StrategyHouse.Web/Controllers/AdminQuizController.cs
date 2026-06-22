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
    private readonly PageContentService _pageContent;
    private readonly QuizTemplateService _templates;

    public AdminQuizController(
        ApplicationDbContext db,
        PageContentService pageContent,
        QuizTemplateService templates)
    {
        _db = db;
        _pageContent = pageContent;
        _templates = templates;
    }

    // GET /Admin/Quiz?tab=Pending|Approved|Rejected
    [HttpGet("")]
    public async Task<IActionResult> Index(string tab = "Pending")
    {
        // Phase 20.11 — expose the expanded-bank toggle value to the view.
        ViewBag.ExpandedBankEnabled = string.Equals(
            _pageContent.Get("quiz.bank.useExpanded", "false"),
            "true", StringComparison.OrdinalIgnoreCase);
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

    // Phase 19.23 — the auto-generator (which read strategy tables directly) was removed.
    // "Generate" now ensures the hand-crafted demo bank exists (idempotent); curated
    // questions are never overwritten.
    [HttpPost("Generate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate()
    {
        var seeded = await AssessmentSeeder.EnsureDemoQuizAsync(_db);
        TempData["Saved"] = seeded ? "تمت إضافة أسئلة الاختبار التجريبية." : "بنك الأسئلة يحتوي على أسئلة بالفعل.";
        return RedirectToAction(nameof(Index));
    }

    // POST /Admin/Quiz/Regenerate — kept for stale clients → idempotent demo reseed.
    [HttpPost("Regenerate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Regenerate()
    {
        var seeded = await AssessmentSeeder.EnsureDemoQuizAsync(_db);
        TempData["Saved"] = seeded ? "تمت إضافة أسئلة الاختبار التجريبية." : "بنك الأسئلة يحتوي على أسئلة بالفعل (لم تتم إضافة أسئلة جديدة).";
        return RedirectToAction(nameof(Index));
    }

    // GET /Admin/Quiz/Reset — confirmation page before the destructive reset.
    [HttpGet("Reset")]
    public async Task<IActionResult> Reset()
    {
        ViewBag.QuestionCount = await _db.QuizQuestions.CountAsync();
        ViewBag.AttemptCount = await _db.QuizAttempts.CountAsync();
        return View();
    }

    // POST /Admin/Quiz/Reset — destructive: truncate attempts + questions, then reseed
    // the 5 demo questions so analytics start from zero.
    [HttpPost("Reset")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetConfirmed()
    {
        await AssessmentSeeder.ResetQuizAsync(_db);
        TempData["Saved"] = "تم إعادة تعيين الاختبار بنجاح";
        return RedirectToAction(nameof(Index));
    }

    // POST /Admin/Quiz/Reseed — idempotent safety net: if the bank is empty, insert the
    // 5 demo questions. Never deletes existing questions, so it's safe to click anytime.
    [HttpPost("Reseed")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reseed()
    {
        var seeded = await AssessmentSeeder.EnsureDemoQuizAsync(_db);
        TempData["Saved"] = seeded
            ? "تمت إضافة أسئلة الاختبار التجريبية الخمسة."
            : "بنك الأسئلة يحتوي على أسئلة بالفعل (لم تتم إضافة أي شيء).";
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

    // GET /Admin/Quiz/Analytics — response stats, per-question correctness, per-department.
    [HttpGet("Analytics")]
    public async Task<IActionResult> Analytics()
    {
        var attempts = await _db.QuizAttempts.OrderByDescending(a => a.CompletedAt).ToListAsync();
        var questions = await _db.QuizQuestions.ToDictionaryAsync(q => q.Id, q => q.QuestionAr);

        var perQuestion = new Dictionary<Guid, (int Correct, int Total)>();
        foreach (var a in attempts)
        {
            List<QuizAnswerDetail>? parsed = null;
            try { parsed = JsonSerializer.Deserialize<List<QuizAnswerDetail>>(a.AnswersJson); } catch { }
            if (parsed == null) continue;
            foreach (var d in parsed)
            {
                if (d.Qid == Guid.Empty) continue;
                var cur = perQuestion.TryGetValue(d.Qid, out var v) ? v : (0, 0);
                perQuestion[d.Qid] = (cur.Item1 + (d.Correct ? 1 : 0), cur.Item2 + 1);
            }
        }

        var vm = new QuizAnalyticsViewModel
        {
            TotalAttempts = attempts.Count,
            AverageScorePct = attempts.Count == 0 ? 0
                : Math.Round(attempts.Where(a => a.Total > 0).Select(a => 100.0 * a.Score / a.Total).DefaultIfEmpty(0).Average(), 1),
            PerQuestion = perQuestion
                .Select(kv => new QuizQuestionStat
                {
                    QuestionAr = questions.TryGetValue(kv.Key, out var t) ? t : kv.Key.ToString(),
                    Correct = kv.Value.Correct,
                    Total = kv.Value.Total,
                    Pct = kv.Value.Total == 0 ? 0 : Math.Round(100.0 * kv.Value.Correct / kv.Value.Total, 1),
                })
                .OrderBy(x => x.Pct)
                .ToList(),
            PerDepartment = attempts
                .Where(a => !string.IsNullOrEmpty(a.DeptCode))
                .GroupBy(a => a.DeptCode!)
                .Select(g => new QuizDeptStat
                {
                    DeptCode = g.Key,
                    Attempts = g.Count(),
                    AvgPct = Math.Round(g.Where(a => a.Total > 0).Select(a => 100.0 * a.Score / a.Total).DefaultIfEmpty(0).Average(), 1),
                })
                .OrderByDescending(x => x.AvgPct)
                .ToList(),
            Recent = attempts.Take(30).ToList(),
        };
        return View(vm);
    }

    // GET /Admin/Quiz/Analytics.csv — export raw attempts.
    [HttpGet("Analytics.csv")]
    public async Task<IActionResult> AnalyticsCsv()
    {
        var attempts = await _db.QuizAttempts.OrderByDescending(a => a.CompletedAt).ToListAsync();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("RespondentName,DeptCode,Scope,Score,Total,Percent,CompletedAt");
        static string Esc(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        foreach (var a in attempts)
        {
            var pct = a.Total > 0 ? Math.Round(100.0 * a.Score / a.Total, 1) : 0;
            sb.AppendLine(string.Join(",", Esc(a.RespondentName), Esc(a.DeptCode), Esc(a.Scope),
                a.Score, a.Total, pct, a.CompletedAt.ToString("o")));
        }
        var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", "quiz-analytics.csv");
    }

    // Phase 20.11 — toggle the expanded (200-template) quiz bank on/off.
    // When OFF (default) the standalone quiz shows the 5 curated questions
    // from QuizQuestionsProvider. When ON the QuizController generates 5
    // random questions from live DB data (filtered to the user's department
    // for domains 4 & 5).
    [HttpPost("ToggleBank")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleBank(bool enabled, string tab = "Pending")
    {
        await _pageContent.SaveAsync(_db, "quiz.bank.useExpanded", enabled ? "true" : "false");
        TempData["Saved"] = enabled
            ? "تم تفعيل بنك الأسئلة الموسّع (200 سؤال ديناميكي من قاعدة البيانات)."
            : "تم إيقاف بنك الأسئلة الموسّع (العودة لــ 5 أسئلة التجريبية).";
        return RedirectToAction(nameof(Index), new { tab });
    }

    // GET /Admin/Quiz/PreviewBank — inspect a freshly generated bank sample
    // (for admins to verify templates render correctly against current DB).
    // Phase 20.13 — expose the dept list to the view so the admin can pick a
    // department instead of typing a code (free-text input «دون تغذية راجعة»
    // gave the impression nothing happened when the code didn’t match), and
    // surface whether the entered code resolved to an actual department.
    [HttpGet("PreviewBank")]
    public async Task<IActionResult> PreviewBank(string? deptCode = null)
    {
        var bank = await _templates.GenerateForUserAsync(deptCode);
        var depts = await _db.Departments
            .Where(d => d.IsActive)
            .OrderBy(d => d.DeptCode)
            .Select(d => new DeptOption { Code = d.DeptCode, Name = d.NameAr ?? d.DeptCode })
            .ToListAsync();
        var matched = !string.IsNullOrEmpty(deptCode)
            ? depts.FirstOrDefault(d => d.Code == deptCode)
            : null;
        var deptCount = bank.Count(q => q.Scope == "Department");
        var generalCount = bank.Count - deptCount;
        ViewBag.DeptCode = deptCode;
        ViewBag.Departments = depts;
        ViewBag.MatchedDeptName = matched?.Name;
        ViewBag.DeptQuestionCount = deptCount;
        ViewBag.GeneralQuestionCount = generalCount;
        ViewBag.Total = bank.Count;
        return View(bank.Take(40).ToList());
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

public class QuizAnswerDetail
{
    public Guid Qid { get; set; }
    public int Picked { get; set; }
    public int CorrectIndex { get; set; }
    public bool Correct { get; set; }
}

public class QuizAnalyticsViewModel
{
    public int TotalAttempts { get; set; }
    public double AverageScorePct { get; set; }
    public List<QuizQuestionStat> PerQuestion { get; set; } = new();
    public List<QuizDeptStat> PerDepartment { get; set; } = new();
    public List<Domain.Entities.QuizAttempt> Recent { get; set; } = new();
}

public class QuizQuestionStat
{
    public string QuestionAr { get; set; } = "";
    public int Correct { get; set; }
    public int Total { get; set; }
    public double Pct { get; set; }
}

public class QuizDeptStat
{
    public string DeptCode { get; set; } = "";
    public int Attempts { get; set; }
    public double AvgPct { get; set; }
}
