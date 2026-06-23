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
    private readonly PageContentService _pageContent;
    private readonly SurveyImportService _import;
    private readonly OpenTextAutoCategorizer _autoCat;

    public AdminSurveysController(
        ApplicationDbContext db,
        QrService qr,
        SurveyReportPdfService report,
        PageContentService pageContent,
        SurveyImportService import,
        OpenTextAutoCategorizer autoCat)
    {
        _db = db;
        _qr = qr;
        _report = report;
        _pageContent = pageContent;
        _import = import;
        _autoCat = autoCat;
    }

    // Phase 19.25 — PageContent keys for the survey link / custom QR feature.
    private const string KeySurveyUrl = "quiz.survey.url";
    private const string KeyUseCustom = "quiz.survey.qr.useCustom";
    private const string KeyCustomQr  = "quiz.survey.qr.custom";

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

    // Phase 17 — survey questions are now hard-coded in SurveyQuestionsProvider and are
    // no longer editable from the admin UI. Add/Edit/Delete are disabled and simply
    // redirect with an explanatory message. (The official 8-question bank is materialised
    // by Phase12SurveySeeder from the static provider on startup.)
    private const string QuestionsLockedMsg =
        "أسئلة الاستبيان أصبحت ثابتة في الكود (SurveyQuestionsProvider) ولا يمكن تعديلها من لوحة التحكم.";

    [HttpPost("{id:guid}/AddQuestion")]
    [ValidateAntiForgeryToken]
    public IActionResult AddQuestion(Guid id)
    {
        TempData["SurveyLocked"] = QuestionsLockedMsg;
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:guid}/EditQuestion/{qid:guid}")]
    [ValidateAntiForgeryToken]
    public IActionResult EditQuestion(Guid id, Guid qid)
    {
        TempData["SurveyLocked"] = QuestionsLockedMsg;
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("{id:guid}/DeleteQuestion/{qid:guid}")]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteQuestion(Guid id, Guid qid)
    {
        TempData["SurveyLocked"] = QuestionsLockedMsg;
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

    // ----------------------------------------------------------------------
    // Phase 19.25 — Survey-wide link & custom-QR editor.
    // Lets admins paste any URL (Microsoft Forms, Google Forms, /s/<token>, ...)
    // and preview the auto-generated QR; or upload a custom QR image (PNG/JPG/
    // SVG/GIF/WEBP up to 1 MB) that will be rendered instead. Toggling
    // "useCustom" off reverts to the URL-generated QR while keeping the upload
    // for fast re-enable. Storage: 3 PageContents keys (no schema change).
    //   quiz.survey.url           — the survey link (added in 19.15)
    //   quiz.survey.qr.useCustom  — "true" | "false"
    //   quiz.survey.qr.custom     — data:image/<mime>;base64,...
    // ----------------------------------------------------------------------
    [HttpGet("Link")]
    public IActionResult Link()
    {
        var url = _pageContent.Get(KeySurveyUrl, "");
        var useCustom = string.Equals(_pageContent.Get(KeyUseCustom, "false"), "true", StringComparison.OrdinalIgnoreCase);
        var customQr = _pageContent.Get(KeyCustomQr, "");

        ViewBag.SurveyUrl = url;
        ViewBag.UseCustom = useCustom;
        ViewBag.CustomQr = customQr;
        try { ViewBag.AutoQr = string.IsNullOrWhiteSpace(url) ? null : _qr.GenerateBase64Png(url, 8); }
        catch { ViewBag.AutoQr = null; }
        return View();
    }

    [HttpPost("Link/SaveUrl")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LinkSaveUrl(string? surveyUrl)
    {
        var trimmed = (surveyUrl ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            TempData["Error"] = "يرجى إدخال الرابط.";
            return RedirectToAction(nameof(Link));
        }
        var looksLikeUrl = trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                        || trimmed.StartsWith("/");
        if (!looksLikeUrl)
        {
            TempData["Error"] = "صيغة الرابط غير صحيحة. يجب أن يبدأ بـ http:// أو https:// أو /.";
            return RedirectToAction(nameof(Link));
        }
        await _pageContent.SaveAsync(_db, KeySurveyUrl, trimmed);
        TempData["Success"] = "تم حفظ رابط الاستبيان. رمز QR تحدّث تلقائياً.";
        return RedirectToAction(nameof(Link));
    }

    [HttpPost("Link/UploadQr")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> LinkUploadQr(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "يرجى اختيار صورة QR للرفع.";
            return RedirectToAction(nameof(Link));
        }
        const long maxBytes = 1_048_576; // 1 MB
        if (file.Length > maxBytes)
        {
            TempData["Error"] = "حجم الصورة يتجاوز 1 ميغابايت. يرجى رفع صورة أصغر.";
            return RedirectToAction(nameof(Link));
        }
        var ext = (Path.GetExtension(file.FileName) ?? "").ToLowerInvariant();
        string mime;
        switch (ext)
        {
            case ".png":  mime = "image/png";  break;
            case ".jpg":
            case ".jpeg": mime = "image/jpeg"; break;
            case ".gif":  mime = "image/gif";  break;
            case ".svg":  mime = "image/svg+xml"; break;
            case ".webp": mime = "image/webp"; break;
            default:
                TempData["Error"] = "نوع الملف غير مدعوم. المدعوم: PNG, JPG, GIF, SVG, WEBP.";
                return RedirectToAction(nameof(Link));
        }
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            bytes = ms.ToArray();
        }
        var dataUri = "data:" + mime + ";base64," + Convert.ToBase64String(bytes);
        await _pageContent.SaveAsync(_db, KeyCustomQr, dataUri);
        await _pageContent.SaveAsync(_db, KeyUseCustom, "true");
        TempData["Success"] = "تم رفع صورة QR وتفعيلها.";
        return RedirectToAction(nameof(Link));
    }

    [HttpPost("Link/ToggleCustom")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LinkToggleCustom(bool useCustom)
    {
        if (useCustom && string.IsNullOrEmpty(_pageContent.Get(KeyCustomQr, "")))
        {
            TempData["Error"] = "لا توجد صورة QR مرفوعة. يرجى رفع صورة أولاً.";
            return RedirectToAction(nameof(Link));
        }
        await _pageContent.SaveAsync(_db, KeyUseCustom, useCustom ? "true" : "false");
        TempData["Success"] = useCustom
            ? "تم تفعيل صورة QR المخصّصة."
            : "تم الرجوع إلى رمز QR المولّد تلقائياً من الرابط.";
        return RedirectToAction(nameof(Link));
    }

    [HttpPost("Link/ClearQr")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LinkClearQr()
    {
        await _pageContent.SaveAsync(_db, KeyCustomQr, "");
        await _pageContent.SaveAsync(_db, KeyUseCustom, "false");
        TempData["Success"] = "تم حذف الصورة المخصّصة وإعادة التوليد التلقائي.";
        return RedirectToAction(nameof(Link));
    }

    // ------------------------------------------------------------------
    // Phase 20.18 — رفع نتائج Excel وتحليلها تلقائياً (single-button)
    // ------------------------------------------------------------------
    [HttpGet("Import")]
    public IActionResult Import()
    {
        return View();
    }

    [HttpPost("Import/Preview")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> ImportPreview(IFormFile? file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "يرجى اختيار ملف Excel للرفع.";
            return RedirectToAction(nameof(Import));
        }
        var ext = (Path.GetExtension(file.FileName) ?? "").ToLowerInvariant();
        if (ext != ".xlsx")
        {
            TempData["Error"] = "الملف يجب أن يكون بصيغة .xlsx فقط.";
            return RedirectToAction(nameof(Import));
        }
        // Stash bytes in TempData so the Apply step doesn't need a re-upload.
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }
        SurveyImportService.ImportPreview preview;
        using (var inMs = new MemoryStream(bytes))
        {
            preview = await _import.AnalyzeAsync(inMs, null, ct);
        }
        // Persist the uploaded file for the Apply step.
        var stashDir = Path.Combine(Path.GetTempPath(), "sh_import_stash");
        Directory.CreateDirectory(stashDir);
        var stashId = Guid.NewGuid().ToString("N");
        var stashPath = Path.Combine(stashDir, stashId + ".xlsx");
        await System.IO.File.WriteAllBytesAsync(stashPath, bytes, ct);
        TempData["ImportStashId"] = stashId;
        TempData["ImportFileName"] = file.FileName;
        return View("Preview", preview);
    }

    [HttpPost("Import/Apply")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportApply(string stashId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stashId))
        {
            TempData["Error"] = "انتهت صلاحية الرفع. يرجى رفع الملف مجدداً.";
            return RedirectToAction(nameof(Import));
        }
        // Defensive: stashId must be a hex GUID, no path separators.
        if (!System.Text.RegularExpressions.Regex.IsMatch(stashId, "^[a-f0-9]{32}$"))
        {
            TempData["Error"] = "معرّف الرفع غير صالح.";
            return RedirectToAction(nameof(Import));
        }
        var stashDir = Path.Combine(Path.GetTempPath(), "sh_import_stash");
        var stashPath = Path.Combine(stashDir, stashId + ".xlsx");
        if (!System.IO.File.Exists(stashPath))
        {
            TempData["Error"] = "لم يتم العثور على الملف المؤقت. يرجى رفع الملف مجدداً.";
            return RedirectToAction(nameof(Import));
        }
        int added;
        Guid? targetId;
        try
        {
            using var fs = System.IO.File.OpenRead(stashPath);
            var result = await _import.ApplyAsync(fs, null, ct);
            added = result.AddedResponses;
            targetId = result.TargetSurveyId;
            // Phase 20.21 — auto-categorise open-text answers per official mechanism sheet.
            try
            {
                var (auto, _, _) = await _autoCat.CategorizeActiveSurveyAsync(ct);
                if (auto > 0)
                {
                    TempData["AutoCatInfo"] = $"تم تصنيف {auto} إجابة مفتوحة تلقائيًا (سـ 4، سـ 5، سـ 7).";
                }
            }
            catch { /* best-effort; missing categorisation is recoverable */ }
        }
        finally
        {
            try { System.IO.File.Delete(stashPath); } catch { /* best-effort */ }
        }
        TempData["Success"] = $"تم استيراد {added} استجابة وإضافتها إلى الاستبيان.";
        if (targetId.HasValue)
        {
            return RedirectToAction(nameof(Analytics), new { id = targetId.Value });
        }
        return RedirectToAction(nameof(Index));
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
