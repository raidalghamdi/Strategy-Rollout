// Phase 20.35 — Category Management Service.
// Admin surface for managing open-text question categories: edit names, keywords,
// activate/deactivate, re-run auto-categorisation, and mark a question as
// "Ready for Report" before it flows into the Final Report.
//
// All data is read from the DB; no hardcoded category lists.
//
// User instruction (verbatim): "1 with managing the service and its features
// before populating it to the final report".

using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Domain.Enums;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

[Authorize(Roles = "Admin,Facilitator,CX")]
[Route("Admin/Survey/CategoryManager")]
public class AdminCategoryManagerController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly SurveyAnalyticsService _analytics;
    private readonly OpenTextAutoCategorizer _autoCat;

    public AdminCategoryManagerController(
        ApplicationDbContext db,
        SurveyAnalyticsService analytics,
        OpenTextAutoCategorizer autoCat)
    {
        _db = db;
        _analytics = analytics;
        _autoCat = autoCat;
    }

    // ---------- View Models (kept here to avoid bloating shared models file) ----------

    public class IndexRow
    {
        public Guid QuestionId { get; set; }
        public int Order { get; set; }
        public string QuestionAr { get; set; } = "";
        public int Total { get; set; }
        public int Uncategorized { get; set; }
        public int BlankCount { get; set; }
        public int ActiveCategories { get; set; }
        public int InactiveCategories { get; set; }
        public bool ReadyForReport { get; set; }
        public DateTime? ReadyForReportAt { get; set; }
    }

    public class EditViewModel
    {
        public Guid QuestionId { get; set; }
        public int QuestionOrder { get; set; }
        public string QuestionAr { get; set; } = "";
        public bool ReadyForReport { get; set; }
        public DateTime? ReadyForReportAt { get; set; }
        public List<CategoryRow> Categories { get; set; } = new();
        public List<UncategorizedRow> Uncategorized { get; set; } = new();
        public int TotalAnswers { get; set; }
        public int CategorizedAnswers { get; set; }
        public int BlankAnswers { get; set; }
    }

    public class CategoryRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string? DescriptionAr { get; set; }
        public int Order { get; set; }
        public bool IsActive { get; set; }
        public bool IsBuiltin { get; set; }
        public List<string> Keywords { get; set; } = new();
        public int AssignedCount { get; set; }
    }

    public class UncategorizedRow
    {
        public Guid ResponseId { get; set; }
        public string Text { get; set; } = "";
        public DateTime SubmittedAt { get; set; }
    }

    // ---------- Actions ----------

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var survey = await _analytics.GetOfficialSurveyAsync();
        if (survey == null) return View("../AdminSurvey/NoSurvey");

        var questions = (await _analytics.GetQuestionsAsync(survey.Id))
            .Where(q => q.QuestionType == QuestionType.OpenText)
            .OrderBy(q => q.Order)
            .ToList();

        var rows = new List<IndexRow>();
        foreach (var q in questions)
        {
            var r = await _analytics.GetOpenTextResultsAsync(q.Id);
            var cats = await _db.SurveyQuestionCategories
                .Where(c => c.SurveyQuestionId == q.Id)
                .ToListAsync();
            rows.Add(new IndexRow
            {
                QuestionId = q.Id,
                Order = q.Order,
                QuestionAr = q.QuestionAr,
                Total = r.TotalResponses,
                Uncategorized = r.UncategorizedCount,
                BlankCount = r.BlankCount,
                ActiveCategories = cats.Count(c => c.IsActive),
                InactiveCategories = cats.Count(c => !c.IsActive),
                ReadyForReport = q.ReadyForReport,
                ReadyForReportAt = q.ReadyForReportAt,
            });
        }
        ViewData["SurveyTitle"] = survey.TitleAr;
        return View(rows);
    }

    [HttpGet("Edit/{questionId:guid}")]
    public async Task<IActionResult> Edit(Guid questionId)
    {
        var q = await _db.SurveyQuestions.FindAsync(questionId);
        if (q == null || q.QuestionType != QuestionType.OpenText) return NotFound();

        var cats = await _db.SurveyQuestionCategories
            .Where(c => c.SurveyQuestionId == questionId)
            .OrderBy(c => c.Order).ThenBy(c => c.Name)
            .ToListAsync();

        // Per-category assigned counts (live).
        var assignments = await _db.OpenTextCategoryAssignments
            .Where(a => a.SurveyQuestionId == questionId)
            .GroupBy(a => a.Category)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync();

        var rows = cats.Select(c =>
        {
            List<string> kws;
            try { kws = JsonSerializer.Deserialize<List<string>>(c.KeywordsJson ?? "[]") ?? new(); }
            catch { kws = new(); }
            return new CategoryRow
            {
                Id = c.Id,
                Name = c.Name,
                DescriptionAr = c.DescriptionAr,
                Order = c.Order,
                IsActive = c.IsActive,
                IsBuiltin = c.IsBuiltin,
                Keywords = kws,
                AssignedCount = assignments.FirstOrDefault(a => a.Name == c.Name)?.Count ?? 0,
            };
        }).ToList();

        var all = await _analytics.GetAllOpenTextAsync(questionId);
        var uncategorized = all
            .Where(a => string.IsNullOrWhiteSpace(a.Category) && !string.IsNullOrWhiteSpace(a.Text))
            .Select(a => new UncategorizedRow { ResponseId = a.ResponseId, Text = a.Text, SubmittedAt = a.SubmittedAt })
            .ToList();
        var blank = all.Count(a => string.IsNullOrWhiteSpace(a.Text));
        var categorized = all.Count - uncategorized.Count - blank;

        var model = new EditViewModel
        {
            QuestionId = q.Id,
            QuestionOrder = q.Order,
            QuestionAr = q.QuestionAr,
            ReadyForReport = q.ReadyForReport,
            ReadyForReportAt = q.ReadyForReportAt,
            Categories = rows,
            Uncategorized = uncategorized,
            TotalAnswers = all.Count,
            CategorizedAnswers = categorized,
            BlankAnswers = blank,
        };
        return View(model);
    }

    [HttpPost("AddCategory")]
    [Authorize(Roles = "Admin,Facilitator")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCategory(Guid questionId, string name, string? descriptionAr, string? keywords)
    {
        if (string.IsNullOrWhiteSpace(name)) { TempData["CatMsg"] = "اسم الفئة مطلوب."; return RedirectToAction(nameof(Edit), new { questionId }); }
        var trimmedName = name.Trim();
        var dupe = await _db.SurveyQuestionCategories
            .AnyAsync(c => c.SurveyQuestionId == questionId && c.Name == trimmedName);
        if (dupe) { TempData["CatMsg"] = "هذه الفئة موجودة بالفعل."; return RedirectToAction(nameof(Edit), new { questionId }); }

        var maxOrder = await _db.SurveyQuestionCategories
            .Where(c => c.SurveyQuestionId == questionId).MaxAsync(c => (int?)c.Order) ?? 0;

        var kwList = ParseKeywords(keywords);

        _db.SurveyQuestionCategories.Add(new SurveyQuestionCategory
        {
            Id = Guid.NewGuid(),
            SurveyQuestionId = questionId,
            Name = trimmedName,
            DescriptionAr = string.IsNullOrWhiteSpace(descriptionAr) ? null : descriptionAr.Trim(),
            Order = maxOrder + 1,
            IsActive = true,
            IsBuiltin = false,
            KeywordsJson = JsonSerializer.Serialize(kwList),
        });
        await _db.SaveChangesAsync();
        TempData["CatMsg"] = $"تمت إضافة الفئة \"{trimmedName}\".";
        return RedirectToAction(nameof(Edit), new { questionId });
    }

    [HttpPost("EditCategory")]
    [Authorize(Roles = "Admin,Facilitator")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCategory(Guid questionId, Guid categoryId, string name, string? descriptionAr, string? keywords, int? order)
    {
        var cat = await _db.SurveyQuestionCategories.FindAsync(categoryId);
        if (cat == null || cat.SurveyQuestionId != questionId) return NotFound();
        if (string.IsNullOrWhiteSpace(name)) { TempData["CatMsg"] = "اسم الفئة مطلوب."; return RedirectToAction(nameof(Edit), new { questionId }); }
        var trimmedName = name.Trim();
        // If the name changed, update existing assignments to point at the new label so categorisation stays consistent.
        if (cat.Name != trimmedName)
        {
            var assignments = await _db.OpenTextCategoryAssignments
                .Where(a => a.SurveyQuestionId == questionId && a.Category == cat.Name)
                .ToListAsync();
            foreach (var a in assignments) a.Category = trimmedName;
            cat.Name = trimmedName;
        }
        cat.DescriptionAr = string.IsNullOrWhiteSpace(descriptionAr) ? null : descriptionAr.Trim();
        cat.KeywordsJson = JsonSerializer.Serialize(ParseKeywords(keywords));
        if (order.HasValue) cat.Order = order.Value;
        await _db.SaveChangesAsync();
        TempData["CatMsg"] = $"تم حفظ الفئة \"{trimmedName}\".";
        return RedirectToAction(nameof(Edit), new { questionId });
    }

    [HttpPost("ToggleActive")]
    [Authorize(Roles = "Admin,Facilitator")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(Guid questionId, Guid categoryId)
    {
        var cat = await _db.SurveyQuestionCategories.FindAsync(categoryId);
        if (cat == null || cat.SurveyQuestionId != questionId) return NotFound();
        cat.IsActive = !cat.IsActive;
        await _db.SaveChangesAsync();
        TempData["CatMsg"] = cat.IsActive ? $"تم تفعيل الفئة \"{cat.Name}\"." : $"تم تعطيل الفئة \"{cat.Name}\".";
        return RedirectToAction(nameof(Edit), new { questionId });
    }

    [HttpPost("DeleteCategory")]
    [Authorize(Roles = "Admin,Facilitator")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCategory(Guid questionId, Guid categoryId)
    {
        var cat = await _db.SurveyQuestionCategories.FindAsync(categoryId);
        if (cat == null || cat.SurveyQuestionId != questionId) return NotFound();
        var used = await _db.OpenTextCategoryAssignments
            .AnyAsync(a => a.SurveyQuestionId == questionId && a.Category == cat.Name);
        if (used)
        {
            // Soft-delete: deactivate instead of removing so historical assignments stay readable.
            cat.IsActive = false;
            await _db.SaveChangesAsync();
            TempData["CatMsg"] = $"الفئة \"{cat.Name}\" مرتبطة بإجابات — تم تعطيلها بدلًا من حذفها.";
        }
        else
        {
            _db.SurveyQuestionCategories.Remove(cat);
            await _db.SaveChangesAsync();
            TempData["CatMsg"] = $"تم حذف الفئة \"{cat.Name}\".";
        }
        return RedirectToAction(nameof(Edit), new { questionId });
    }

    [HttpPost("AssignAnswer")]
    [Authorize(Roles = "Admin,Facilitator,CX")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignAnswer(Guid questionId, Guid responseId, string? category)
    {
        var existing = await _db.OpenTextCategoryAssignments
            .FirstOrDefaultAsync(a => a.SurveyQuestionId == questionId && a.SurveyResponseId == responseId);
        if (string.IsNullOrWhiteSpace(category))
        {
            if (existing != null) _db.OpenTextCategoryAssignments.Remove(existing);
        }
        else
        {
            int? userId = int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : null;
            if (existing == null)
                _db.OpenTextCategoryAssignments.Add(new OpenTextCategoryAssignment
                {
                    SurveyQuestionId = questionId,
                    SurveyResponseId = responseId,
                    Category = category.Trim(),
                    AssignedByUserId = userId,
                });
            else { existing.Category = category.Trim(); existing.AssignedAt = DateTime.UtcNow; existing.AssignedByUserId = userId; }
        }
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Edit), new { questionId });
    }

    [HttpPost("Rerun/{questionId:guid}")]
    [Authorize(Roles = "Admin,Facilitator")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rerun(Guid questionId, CancellationToken ct)
    {
        var (auto, skipped, total) = await _autoCat.CategorizeActiveSurveyAsync(ct);
        TempData["CatMsg"] = total == 0
            ? "لا توجد إجابات مفتوحة لتصنيفها."
            : $"تم تصنيف {auto} إجابة جديدة (تم تجاهل {skipped} مصنّفة سابقًا، إجمالي {total}).";
        return RedirectToAction(nameof(Edit), new { questionId });
    }

    [HttpPost("MarkReady/{questionId:guid}")]
    [Authorize(Roles = "Admin,Facilitator")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkReady(Guid questionId)
    {
        var q = await _db.SurveyQuestions.FindAsync(questionId);
        if (q == null) return NotFound();
        int? userId = int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : null;
        q.ReadyForReport = true;
        q.ReadyForReportAt = DateTime.UtcNow;
        q.ReadyForReportByUserId = userId;
        await _db.SaveChangesAsync();
        TempData["CatMsg"] = "تم وضع علامة \"جاهز للنشر في التقرير\" على هذا السؤال.";
        return RedirectToAction(nameof(Edit), new { questionId });
    }

    [HttpPost("Unmark/{questionId:guid}")]
    [Authorize(Roles = "Admin,Facilitator")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unmark(Guid questionId)
    {
        var q = await _db.SurveyQuestions.FindAsync(questionId);
        if (q == null) return NotFound();
        q.ReadyForReport = false;
        q.ReadyForReportAt = null;
        q.ReadyForReportByUserId = null;
        await _db.SaveChangesAsync();
        TempData["CatMsg"] = "تم إلغاء جاهزية هذا السؤال — لن يظهر في التقرير النهائي حتى المراجعة.";
        return RedirectToAction(nameof(Edit), new { questionId });
    }

    private static List<string> ParseKeywords(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();
        return raw
            .Split(new[] { ',', '،', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct()
            .ToList();
    }
}
