using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 4 — admin authoring & reporting for programme surveys.
[Authorize(Roles = "Admin,Facilitator")]
[Route("Admin/Surveys")]
public class AdminSurveysController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly QrService _qr;
    private readonly SurveyReportPdfService _report;

    public AdminSurveysController(ApplicationDbContext db, QrService qr, SurveyReportPdfService report)
    {
        _db = db;
        _qr = qr;
        _report = report;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var surveys = await _db.Surveys
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new SurveyListItem
            {
                Id = s.Id,
                TitleAr = s.TitleAr,
                IsActive = s.IsActive,
                PublicToken = s.PublicToken,
                Questions = s.Questions.Count,
                Responses = _db.SurveyResponses.Count(r => r.SurveyId == s.Id),
            })
            .ToListAsync();
        return View(surveys);
    }

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string titleAr, string? descriptionAr)
    {
        if (string.IsNullOrWhiteSpace(titleAr)) return RedirectToAction(nameof(Index));
        var survey = new Survey
        {
            TitleAr = titleAr.Trim(),
            DescriptionAr = descriptionAr,
            IsActive = true,
            PublicToken = await UniqueTokenAsync(),
        };
        _db.Surveys.Add(survey);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Edit), new { id = survey.Id });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var survey = await _db.Surveys
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (survey == null) return NotFound();
        survey.Questions = survey.Questions.OrderBy(q => q.Order).ToList();
        return View(survey);
    }

    [HttpPost("{id:guid}/Update")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, string titleAr, string? descriptionAr, bool isActive)
    {
        var survey = await _db.Surveys.FindAsync(id);
        if (survey != null)
        {
            survey.TitleAr = titleAr?.Trim() ?? survey.TitleAr;
            survey.DescriptionAr = descriptionAr;
            survey.IsActive = isActive;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:guid}/AddQuestion")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddQuestion(Guid id, string type, string questionAr, string? optionsCsv, bool isRequired = true)
    {
        var survey = await _db.Surveys.FindAsync(id);
        if (survey != null && !string.IsNullOrWhiteSpace(questionAr))
        {
            var order = (await _db.SurveyQuestions.Where(q => q.SurveyId == id).MaxAsync(q => (int?)q.Order) ?? 0) + 1;
            string? optionsJson = null;
            if (type == "MCQ")
            {
                var opts = (optionsCsv ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                if (opts.Count >= 2) optionsJson = JsonSerializer.Serialize(opts);
            }
            _db.SurveyQuestions.Add(new SurveyQuestion
            {
                SurveyId = id,
                Order = order,
                Type = type,
                QuestionAr = questionAr.Trim(),
                OptionsJson = optionsJson,
                IsRequired = isRequired,
            });
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:guid}/DeleteQuestion/{qid:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteQuestion(Guid id, Guid qid)
    {
        var q = await _db.SurveyQuestions.FirstOrDefaultAsync(x => x.Id == qid && x.SurveyId == id);
        if (q != null) { _db.SurveyQuestions.Remove(q); await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:guid}/Regenerate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerateToken(Guid id)
    {
        var survey = await _db.Surveys.FindAsync(id);
        if (survey != null) { survey.PublicToken = await UniqueTokenAsync(); await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpGet("{id:guid}/Qr")]
    public async Task<IActionResult> Qr(Guid id)
    {
        var survey = await _db.Surveys.FindAsync(id);
        if (survey == null) return NotFound();
        var url = $"{Request.Scheme}://{Request.Host}/s/{survey.PublicToken}";
        ViewBag.Url = url;
        ViewBag.QrPng = _qr.GenerateBase64Png(url, 10);
        return View(survey);
    }

    [HttpGet("{id:guid}/Report")]
    public async Task<IActionResult> Report(Guid id)
    {
        var model = await BuildReportAsync(id);
        if (model == null) return NotFound();
        return View(model);
    }

    // Phase 9 — interactive analytics dashboard with Chart.js distributions.
    [HttpGet("{id:guid}/Analytics")]
    public async Task<IActionResult> Analytics(Guid id)
    {
        var model = await BuildReportAsync(id);
        if (model == null) return NotFound();
        ViewBag.SurveyId = id;
        return View(model);
    }

    // Phase 9 — CSV export of per-question distributions + free-text answers.
    [HttpGet("{id:guid}/Analytics.csv")]
    public async Task<IActionResult> AnalyticsCsv(Guid id)
    {
        var model = await BuildReportAsync(id);
        if (model == null) return NotFound();
        var sb = new System.Text.StringBuilder();
        static string Esc(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        sb.AppendLine("Question,Type,Answered,Bucket,Count,Average");
        foreach (var q in model.Questions)
        {
            if (q.Distribution.Count == 0 && q.Verbatim.Count == 0)
            {
                sb.AppendLine(string.Join(",", Esc(q.QuestionAr), Esc(q.Type), q.Answered, Esc(""), 0, q.Average?.ToString("0.##") ?? ""));
                continue;
            }
            foreach (var (label, count) in q.Distribution)
                sb.AppendLine(string.Join(",", Esc(q.QuestionAr), Esc(q.Type), q.Answered, Esc(label), count, q.Average?.ToString("0.##") ?? ""));
            foreach (var v in q.Verbatim)
                sb.AppendLine(string.Join(",", Esc(q.QuestionAr), Esc(q.Type), q.Answered, Esc("نص"), Esc(v), ""));
        }
        var bytes = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"survey-analytics-{id}.csv");
    }

    [HttpGet("{id:guid}/Report/Pdf")]
    public async Task<IActionResult> ReportPdf(Guid id)
    {
        var model = await BuildReportAsync(id);
        if (model == null) return NotFound();
        var bytes = _report.Generate(model);
        return File(bytes, "application/pdf", $"survey-report-{id}.pdf");
    }

    private async Task<SurveyReportPdfService.ReportModel?> BuildReportAsync(Guid id)
    {
        var survey = await _db.Surveys.Include(s => s.Questions).FirstOrDefaultAsync(s => s.Id == id);
        if (survey == null) return null;
        var questions = survey.Questions.OrderBy(q => q.Order).ToList();
        var responses = await _db.SurveyResponses.Where(r => r.SurveyId == id).ToListAsync();

        var deptNames = await _db.Departments.ToDictionaryAsync(d => d.DeptCode, d => d.NameAr ?? d.DeptCode);

        // answers parsed once: list of dict qid->value(string)
        var parsed = responses.Select(r =>
        {
            try
            {
                var arr = JsonSerializer.Deserialize<List<AnswerItem>>(r.AnswersJson) ?? new();
                return arr.ToDictionary(a => a.Qid, a => a.Value ?? "");
            }
            catch { return new Dictionary<string, string>(); }
        }).ToList();

        var model = new SurveyReportPdfService.ReportModel
        {
            SurveyTitle = survey.TitleAr,
            TotalResponses = responses.Count,
        };

        foreach (var q in questions)
        {
            var qid = q.Id.ToString();
            var values = parsed.Select(p => p.TryGetValue(qid, out var v) ? v : null)
                               .Where(v => !string.IsNullOrWhiteSpace(v))
                               .Select(v => v!)
                               .ToList();
            var stat = new SurveyReportPdfService.QuestionStat
            {
                QuestionAr = q.QuestionAr,
                Type = q.Type,
                Answered = values.Count,
            };

            switch (q.Type)
            {
                case "Likert5":
                    var nums = values.Select(v => int.TryParse(v, out var n) ? n : (int?)null).Where(n => n != null).Select(n => n!.Value).ToList();
                    if (nums.Count > 0) stat.Average = nums.Average();
                    for (int s = 1; s <= 5; s++)
                        stat.Distribution.Add(($"{s}", nums.Count(n => n == s)));
                    break;
                case "YesNo":
                    stat.Distribution.Add(("نعم", values.Count(v => v == "yes" || v == "نعم" || v == "true")));
                    stat.Distribution.Add(("لا", values.Count(v => v == "no" || v == "لا" || v == "false")));
                    break;
                case "MCQ":
                    foreach (var grp in values.GroupBy(v => v).OrderByDescending(g => g.Count()))
                        stat.Distribution.Add((grp.Key, grp.Count()));
                    break;
                case "Text":
                    stat.Verbatim = values.Take(20).ToList();
                    break;
            }
            model.Questions.Add(stat);
        }

        model.ByDept = responses
            .Where(r => !string.IsNullOrWhiteSpace(r.DeptCode))
            .GroupBy(r => r.DeptCode!)
            .Select(g => (deptNames.TryGetValue(g.Key, out var n) ? n : g.Key, g.Count()))
            .OrderByDescending(x => x.Item2)
            .ToList();

        return model;
    }

    private async Task<string> UniqueTokenAsync()
    {
        for (int i = 0; i < 20; i++)
        {
            var token = GenerateToken(16);
            if (!await _db.Surveys.AnyAsync(s => s.PublicToken == token)) return token;
        }
        return GenerateToken(24);
    }

    private static string GenerateToken(int length)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var sb = new System.Text.StringBuilder(length);
        foreach (var b in bytes) sb.Append(chars[b % chars.Length]);
        return sb.ToString();
    }

    private class AnswerItem
    {
        public string Qid { get; set; } = "";
        public string? Value { get; set; }
    }
}

public class SurveyListItem
{
    public Guid Id { get; set; }
    public string TitleAr { get; set; } = "";
    public bool IsActive { get; set; }
    public string PublicToken { get; set; } = "";
    public int Questions { get; set; }
    public int Responses { get; set; }
}
