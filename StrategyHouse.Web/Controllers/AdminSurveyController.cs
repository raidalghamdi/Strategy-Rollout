using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Domain.Enums;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Models;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 12 — admin surface for the official 8-question survey: per-question analytics
// with the spec's measurement metrics, open-text categorisation, reseed, and the final
// report (HTML + branded PDF). Operates on the single seeded official survey.
[Authorize(Roles = "Admin,Facilitator")]
[Route("Admin/Survey")]
public class AdminSurveyController : Controller
{
    private const string PdfMime = "application/pdf";
    private const string PptxMime = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly ApplicationDbContext _db;
    private readonly SurveyAnalyticsService _analytics;
    private readonly SurveyFinalReportPdfService _pdf;
    private readonly SurveyReportExcelBuilder _excel;
    private readonly SurveyReportPowerPointBuilder _pptx;
    private readonly ReportEmailService _email;

    public AdminSurveyController(
        ApplicationDbContext db,
        SurveyAnalyticsService analytics,
        SurveyFinalReportPdfService pdf,
        SurveyReportExcelBuilder excel,
        SurveyReportPowerPointBuilder pptx,
        ReportEmailService email)
    {
        _db = db;
        _analytics = analytics;
        _pdf = pdf;
        _excel = excel;
        _pptx = pptx;
        _email = email;
    }

    private static string FileBase => $"Survey_Final_Report_{DateTime.UtcNow:yyyy-MM-dd}";

    [HttpGet("Analytics")]
    public async Task<IActionResult> Analytics()
    {
        var model = await BuildAnalyticsAsync();
        if (model == null) return View("NoSurvey");
        return View(model);
    }

    [HttpPost("Reseed")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reseed()
    {
        await Phase12SurveySeeder.ReseedAsync(_db);
        TempData["SurveyMsg"] = "تمت إعادة بذر الاستبيان بالأسئلة الثمانية الرسمية.";
        return RedirectToAction(nameof(Analytics));
    }

    // ---------- Open-text categorisation ----------

    [HttpGet("Categorize")]
    public async Task<IActionResult> CategorizeIndex()
    {
        var survey = await _analytics.GetOfficialSurveyAsync();
        if (survey == null) return View("NoSurvey");
        var qs = (await _analytics.GetQuestionsAsync(survey.Id))
            .Where(q => q.QuestionType == QuestionType.OpenText).ToList();
        var items = new List<OpenTextQuestionLink>();
        foreach (var q in qs)
        {
            var r = await _analytics.GetOpenTextResultsAsync(q.Id);
            items.Add(new OpenTextQuestionLink(q.Id, q.Order, q.QuestionAr, r.TotalResponses, r.UncategorizedCount));
        }
        return View(items);
    }

    [HttpGet("Categorize/{questionId:guid}")]
    public async Task<IActionResult> Categorize(Guid questionId)
    {
        var q = await _db.SurveyQuestions.FindAsync(questionId);
        if (q == null || q.QuestionType != QuestionType.OpenText) return NotFound();
        var model = new CategorizeViewModel
        {
            QuestionId = q.Id,
            QuestionOrder = q.Order,
            QuestionAr = q.QuestionAr,
            Categories = await _analytics.GetCategoriesAsync(q.Id),
            Answers = await _analytics.GetAllOpenTextAsync(q.Id),
        };
        return View(model);
    }

    [HttpPost("Categorize/{questionId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCategory(Guid questionId, Guid responseId, string? category)
    {
        await AssignAsync(questionId, responseId, category);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Categorize), new { questionId });
    }

    [HttpPost("Categorize/{questionId:guid}/Bulk")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkCategory(Guid questionId, Guid[] responseIds, string? category)
    {
        if (responseIds != null)
            foreach (var rid in responseIds)
                await AssignAsync(questionId, rid, category);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Categorize), new { questionId });
    }

    private async Task AssignAsync(Guid questionId, Guid responseId, string? category)
    {
        var existing = await _db.OpenTextCategoryAssignments
            .FirstOrDefaultAsync(a => a.SurveyQuestionId == questionId && a.SurveyResponseId == responseId);
        if (string.IsNullOrWhiteSpace(category))
        {
            if (existing != null) _db.OpenTextCategoryAssignments.Remove(existing);
            return;
        }
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

    [HttpGet("Categorize/{questionId:guid}/Export.csv")]
    public async Task<IActionResult> CategorizeCsv(Guid questionId)
    {
        var q = await _db.SurveyQuestions.FindAsync(questionId);
        if (q == null) return NotFound();
        var rows = await _analytics.GetAllOpenTextAsync(questionId);
        var sb = new StringBuilder();
        static string Esc(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        sb.AppendLine("ResponseId,SubmittedAt,Category,Answer");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", r.ResponseId, r.SubmittedAt.ToString("yyyy-MM-dd HH:mm"), Esc(r.Category), Esc(r.Text)));
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"survey-q{q.Order}-categorized.csv");
    }

    // ---------- Final report ----------

    [HttpGet("FinalReport")]
    public async Task<IActionResult> FinalReport()
    {
        var model = await BuildFinalReportAsync();
        if (model == null) return View("NoSurvey");
        return View(model);
    }

    [HttpGet("FinalReport.pdf")]
    public async Task<IActionResult> FinalReportPdf()
    {
        var model = await BuildFinalReportAsync();
        if (model == null) return NotFound();
        return File(_pdf.Generate(model), PdfMime, $"{FileBase}.pdf");
    }

    [HttpGet("FinalReport.pptx")]
    public async Task<IActionResult> FinalReportPptx()
    {
        var model = await BuildFinalReportAsync();
        if (model == null) return NotFound();
        return File(_pptx.Build(model), PptxMime, $"{FileBase}.pptx");
    }

    [HttpGet("FinalReport.xlsx")]
    public async Task<IActionResult> FinalReportXlsx()
    {
        var model = await BuildFinalReportAsync();
        if (model == null) return NotFound();
        return File(_excel.Build(model), XlsxMime, $"{FileBase}.xlsx");
    }

    [HttpPost("FinalReport/Email")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EmailReport(string email, string format)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return Json(new { success = false, message = "يرجى إدخال بريد إلكتروني صحيح." });

        var model = await BuildFinalReportAsync();
        if (model == null)
            return Json(new { success = false, message = "لا يوجد استبيان رسمي لإصدار تقريره." });

        byte[] bytes; string mime, ext;
        switch ((format ?? "pdf").ToLowerInvariant())
        {
            case "pptx": bytes = _pptx.Build(model); mime = PptxMime; ext = "pptx"; break;
            case "xlsx": bytes = _excel.Build(model); mime = XlsxMime; ext = "xlsx"; break;
            default: bytes = _pdf.Generate(model); mime = PdfMime; ext = "pdf"; break;
        }

        var fileName = $"{FileBase}.{ext}";
        var body = "<p style=\"font-family:sans-serif;direction:rtl\">مرفق التقرير النهائي للاستبيان الرسمي من الهيئة العامة للمنافسة.</p>";
        var result = await _email.SendReportAsync(email, "التقرير النهائي للاستبيان — الهيئة العامة للمنافسة", body, fileName, bytes, mime);
        return Json(new { success = result.Sent, message = result.Reason });
    }

    // ---------- builders ----------

    private async Task<SurveyAnalyticsViewModel?> BuildAnalyticsAsync()
    {
        var survey = await _analytics.GetOfficialSurveyAsync();
        if (survey == null) return null;
        var questions = await _analytics.GetQuestionsAsync(survey.Id);
        var total = await _db.SurveyResponses.CountAsync(r => r.SurveyId == survey.Id);

        var cards = new List<QuestionCard>();
        foreach (var q in questions)
            cards.Add(await BuildCardAsync(q));

        return new SurveyAnalyticsViewModel
        {
            SurveyId = survey.Id,
            SurveyTitle = survey.TitleAr,
            PublicToken = survey.PublicToken,
            TotalResponses = total,
            Cards = cards,
        };
    }

    private async Task<QuestionCard> BuildCardAsync(SurveyQuestion q)
    {
        var card = new QuestionCard
        {
            QuestionId = q.Id,
            Order = q.Order,
            QuestionAr = q.QuestionAr,
            Type = q.QuestionType,
            Metric = q.MeasurementMetric ?? "",
            Formula = q.MeasurementFormula ?? "",
        };
        switch (q.QuestionType)
        {
            case QuestionType.Likert5:
                card.Likert = await _analytics.GetLikertResultsAsync(q.Id);
                break;
            case QuestionType.MultipleChoice:
                card.Choices = await _analytics.GetMultipleChoiceResultsAsync(q.Id);
                break;
            case QuestionType.OpenText:
                card.OpenText = await _analytics.GetOpenTextResultsAsync(q.Id);
                break;
        }
        return card;
    }

    private async Task<FinalReportViewModel?> BuildFinalReportAsync()
    {
        var analytics = await BuildAnalyticsAsync();
        if (analytics == null) return null;

        var dates = await _db.SurveyResponses.Where(r => r.SurveyId == analytics.SurveyId)
            .Select(r => r.SubmittedAt).ToListAsync();

        var model = new FinalReportViewModel
        {
            SurveyId = analytics.SurveyId,
            SurveyTitle = analytics.SurveyTitle,
            TotalResponses = analytics.TotalResponses,
            DateFrom = dates.Count > 0 ? dates.Min() : null,
            DateTo = dates.Count > 0 ? dates.Max() : null,
            Cards = analytics.Cards,
        };

        // Per-question one-line interpretations.
        foreach (var c in analytics.Cards)
            model.Interpretations[c.QuestionId] = Interpret(c);

        // Top 3 takeaways.
        var q1 = analytics.Cards.FirstOrDefault(c => c.Order == 1)?.Likert;
        if (q1 != null && q1.Total > 0)
            model.Takeaways.Add($"وضوح الاستراتيجية: {q1.PctHigh:0.#}% من المشاركين أجابوا بدرجة وضوح عالية (4 أو 5).");
        var topValue = analytics.Cards.FirstOrDefault(c => c.Order == 3)?.Choices?.FirstOrDefault();
        if (topValue != null && topValue.Count > 0)
            model.Takeaways.Add($"أبرز نقطة قوة: \"{topValue.ChoiceText}\" باختيار {topValue.Percent:0.#}%.");
        var topChallenge = analytics.Cards.FirstOrDefault(c => c.Order == 4)?.OpenText?.Categories.FirstOrDefault();
        if (topChallenge != null)
            model.Takeaways.Add($"أبرز محور تحدٍّ: \"{topChallenge.Category}\" بنسبة {topChallenge.Percent:0.#}% من الإجابات المصنّفة.");
        var q8 = analytics.Cards.FirstOrDefault(c => c.Order == 8)?.Likert;
        if (model.Takeaways.Count < 3 && q8 != null && q8.Total > 0)
            model.Takeaways.Add($"القدرة على المساهمة: متوسط {q8.Mean:0.##} / 5 ({q8.PctHigh:0.#}% بقدرة عالية).");

        // Overall insights.
        var officialValues = new[] { "الشفافية", "التعاون", "التميز", "العدالة", "الابتكار" };
        var q3 = analytics.Cards.FirstOrDefault(c => c.Order == 3)?.Choices;
        if (q3 != null && q3.Any(c => c.Count > 0))
        {
            var top = q3.First();
            model.Insights.Add($"التوافق مع القيم الرسمية: القيمة الأبرز لدى الموظفين هي \"{top.ChoiceText}\" — وجميع الخيارات ضمن القيم الرسمية المعتمدة ({string.Join("، ", officialValues)}).");
        }
        if (q1 != null && q8 != null && q1.Total > 0 && q8.Total > 0)
        {
            var gap = q1.PctHigh - q8.PctHigh;
            var dir = gap > 5 ? "الوضوح أعلى من الشعور بالقدرة على المساهمة" : gap < -5 ? "الشعور بالقدرة أعلى من وضوح الاستراتيجية" : "متقاربان";
            model.Insights.Add($"الفجوة بين الوضوح (س1: {q1.PctHigh:0.#}%) والقدرة على المساهمة (س8: {q8.PctHigh:0.#}%): {dir} (فارق {Math.Abs(gap):0.#} نقطة).");
        }
        var topChall = analytics.Cards.FirstOrDefault(c => c.Order == 4)?.OpenText?.Categories.FirstOrDefault();
        var topInit = analytics.Cards.FirstOrDefault(c => c.Order == 6)?.Choices?.FirstOrDefault(c => c.Count > 0);
        if (topChall != null && topInit != null)
            model.Insights.Add($"أبرز تحدٍّ مصنّف \"{topChall.Category}\" مقابل أهم مبادرة مختارة \"{topInit.ChoiceText}\" — يُنصح بمواءمة المبادرة مع معالجة هذا التحدي.");

        return model;
    }

    private static string Interpret(QuestionCard c)
    {
        switch (c.Type)
        {
            case QuestionType.Likert5:
                var l = c.Likert;
                if (l == null || l.Total == 0) return "لا توجد إجابات بعد.";
                if (l.PctHigh >= 70) return "غالبية المشاركين عند درجة عالية على هذا المقياس.";
                if (l.PctHigh >= 40) return "استجابة متوسطة — مجال للتحسين.";
                return "نسبة الدرجات العالية منخفضة — يتطلب اهتمامًا.";
            case QuestionType.MultipleChoice:
                var top = c.Choices?.FirstOrDefault(x => x.Count > 0);
                return top != null ? $"الخيار الأبرز: \"{top.ChoiceText}\" بنسبة {top.Percent:0.#}%." : "لا توجد إجابات بعد.";
            case QuestionType.OpenText:
                var o = c.OpenText;
                if (o == null || o.TotalResponses == 0) return "لا توجد إجابات نصية بعد.";
                var tc = o.Categories.FirstOrDefault();
                return tc != null
                    ? $"{o.TotalResponses} إجابة، أبرز فئة \"{tc.Category}\" ({tc.Count})؛ غير مصنّف: {o.UncategorizedCount}."
                    : $"{o.TotalResponses} إجابة، لم يُصنَّف منها {o.UncategorizedCount} بعد.";
            default:
                return "";
        }
    }
}
