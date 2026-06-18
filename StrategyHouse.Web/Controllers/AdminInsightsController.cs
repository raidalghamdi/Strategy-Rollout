using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 2 — leadership analytics. No schema changes; all aggregation off existing tables.
[Authorize(Roles = "Admin,Facilitator")]
[Route("Admin/Insights")]
public class AdminInsightsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly CoverageService _coverage;
    private readonly PledgeAggregateService _pledges;
    private readonly ProgrammePosterPdfService _poster;
    private readonly StrategyContentService _content;
    private readonly PageContentService _pageContent;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly UserManager<AppUser> _userManager;
    private readonly IWebHostEnvironment _env;

    public AdminInsightsController(
        ApplicationDbContext db,
        CoverageService coverage,
        PledgeAggregateService pledges,
        ProgrammePosterPdfService poster,
        StrategyContentService content,
        PageContentService pageContent,
        SignInManager<AppUser> signInManager,
        UserManager<AppUser> userManager,
        IWebHostEnvironment env)
    {
        _db = db;
        _coverage = coverage;
        _pledges = pledges;
        _poster = poster;
        _content = content;
        _pageContent = pageContent;
        _signInManager = signInManager;
        _userManager = userManager;
        _env = env;
    }

    private const string SurveyUploadFileName = "survey-upload.xlsx";
    private string SurveyUploadDir => Path.Combine(_env.ContentRootPath, "App_Data", "survey");
    private string SurveyUploadPath => Path.Combine(SurveyUploadDir, SurveyUploadFileName);

    public class SurveySummary
    {
        public int RowCount { get; set; }
        public List<string> Columns { get; set; } = new();
        public Dictionary<string, double> Averages { get; set; } = new();
        public Dictionary<string, List<KeyValuePair<string, int>>> ValueCounts { get; set; } = new();
        public DateTime UploadedAt { get; set; }
        public long FileSizeBytes { get; set; }
        public string FileName { get; set; } = SurveyUploadFileName;
    }

    private SurveySummary? LoadSurveySummary()
    {
        if (!System.IO.File.Exists(SurveyUploadPath)) return null;
        try
        {
            using var wb = new XLWorkbook(SurveyUploadPath);
            var ws = wb.Worksheets.First();
            var rows = ws.RangeUsed()?.RowsUsed().ToList();
            if (rows == null || rows.Count == 0) return null;

            var headerRow = rows[0];
            var columns = headerRow.Cells().Select(c => c.GetString().Trim()).ToList();
            // Trim trailing empty header names.
            while (columns.Count > 0 && string.IsNullOrWhiteSpace(columns[^1]))
                columns.RemoveAt(columns.Count - 1);
            if (columns.Count == 0) return null;

            var numericCount = new int[columns.Count];
            var numericSum = new double[columns.Count];
            var nonEmptyCount = new int[columns.Count];
            var freq = new Dictionary<string, int>[columns.Count];
            for (var i = 0; i < columns.Count; i++) freq[i] = new Dictionary<string, int>();

            var dataRows = 0;
            for (var r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                var any = false;
                for (var i = 0; i < columns.Count; i++)
                {
                    var cell = row.Cell(i + 1);
                    var text = cell.GetString().Trim();
                    if (string.IsNullOrEmpty(text)) continue;
                    any = true;
                    nonEmptyCount[i]++;
                    if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
                    {
                        numericCount[i]++;
                        numericSum[i] += num;
                    }
                    freq[i][text] = freq[i].TryGetValue(text, out var c) ? c + 1 : 1;
                }
                if (any) dataRows++;
            }

            var summary = new SurveySummary
            {
                RowCount = dataRows,
                Columns = columns,
            };

            for (var i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                if (string.IsNullOrWhiteSpace(col)) continue;
                if (numericCount[i] >= 1 && nonEmptyCount[i] > 0 &&
                    (double)numericCount[i] / nonEmptyCount[i] >= 0.8)
                {
                    summary.Averages[col] = numericSum[i] / numericCount[i];
                }
                else if (nonEmptyCount[i] > 0)
                {
                    summary.ValueCounts[col] = freq[i]
                        .OrderByDescending(kv => kv.Value)
                        .ThenBy(kv => kv.Key)
                        .Take(8)
                        .ToList();
                }
            }

            var fi = new FileInfo(SurveyUploadPath);
            summary.UploadedAt = fi.LastWriteTime;
            summary.FileSizeBytes = fi.Length;
            return summary;
        }
        catch
        {
            return null;
        }
    }

    // GET /Admin/Insights/Survey — survey analysis dashboard.
    [HttpGet("Survey")]
    public IActionResult Survey()
    {
        var summary = LoadSurveySummary();
        ViewBag.Summary = summary;
        ViewBag.SurveyUrl = _pageContent.Get("quiz.survey.url");
        return View();
    }

    // POST /Admin/Insights/Survey/Upload — store the Microsoft Forms .xlsx export.
    [HttpPost("Survey/Upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> SurveyUpload(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            TempData["Error"] = "يرجى اختيار ملف Excel (.xlsx).";
            return RedirectToAction(nameof(Survey));
        }
        var ext = Path.GetExtension(file.FileName);
        if (!string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "صيغة الملف غير مدعومة. يرجى رفع ملف .xlsx فقط.";
            return RedirectToAction(nameof(Survey));
        }
        Directory.CreateDirectory(SurveyUploadDir);
        using (var fs = System.IO.File.Create(SurveyUploadPath))
        {
            await file.CopyToAsync(fs);
        }
        try { using var wb = new XLWorkbook(SurveyUploadPath); _ = wb.Worksheets.First(); }
        catch
        {
            System.IO.File.Delete(SurveyUploadPath);
            TempData["Error"] = "تعذرت قراءة ملف Excel. تأكد من أنه ملف صحيح من Microsoft Forms.";
            return RedirectToAction(nameof(Survey));
        }
        TempData["Success"] = "تم رفع ملف الاستبيان وتحليله بنجاح.";
        return RedirectToAction(nameof(Survey));
    }

    // POST /Admin/Insights/Survey/Reset — password-gated: delete data + revert survey URL.
    [HttpPost("Survey/Reset")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SurveyReset(string password)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            TempData["Error"] = "الجلسة غير صالحة. يرجى تسجيل الدخول من جديد.";
            return RedirectToAction(nameof(Survey));
        }
        var ok = await _userManager.CheckPasswordAsync(user, password ?? string.Empty);
        if (!ok)
        {
            TempData["Error"] = "كلمة المرور غير صحيحة. لم تتم إعادة التهيئة.";
            return RedirectToAction(nameof(Survey));
        }
        if (System.IO.File.Exists(SurveyUploadPath)) System.IO.File.Delete(SurveyUploadPath);
        var def = PageContentService.Defaults.FirstOrDefault(d => d.Key == "quiz.survey.url");
        if (!string.IsNullOrEmpty(def.Key))
        {
            await _pageContent.SaveAsync(_db, def.Key, def.Default);
        }
        TempData["Success"] = "تمت إعادة تهيئة الاستبيان وحذف بياناته بنجاح.";
        return RedirectToAction(nameof(Survey));
    }

    // GET /Admin/Insights/Survey/Download — download the raw uploaded .xlsx.
    [HttpGet("Survey/Download")]
    public IActionResult SurveyDownload()
    {
        if (!System.IO.File.Exists(SurveyUploadPath)) return NotFound();
        var bytes = System.IO.File.ReadAllBytes(SurveyUploadPath);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", SurveyUploadFileName);
    }

    // GET /Admin/Insights — programme health overview cards + mini charts.
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var deptCount = await _db.Departments.CountAsync();
        var sessions = await _db.StrategySessions.AsNoTracking().ToListAsync();
        var maps = await _db.DepartmentStrategyMaps.AsNoTracking().ToListAsync();

        var completedDepts = sessions.Where(s => s.CompletedAt != null).Select(s => s.DeptCode).Distinct().Count();
        var signedMaps = maps.Count(m => m.SignedAt != null);
        var approvedMaps = maps.Count(m => m.SignedAt != null && m.IsActive);

        ViewBag.DeptCount = deptCount;
        ViewBag.CompletedDepts = completedDepts;
        ViewBag.SignedMaps = signedMaps;
        ViewBag.ApprovedMaps = approvedMaps;
        ViewBag.TotalPledges = await _db.ContributionPledges.CountAsync();
        ViewBag.QuizAttempts = await _db.QuizAttempts.CountAsync();
        ViewBag.SurveyResponses = await _db.SurveyResponses.CountAsync();
        ViewBag.TotalSessions = sessions.Count;

        // Signature rate per dept (signed maps / sessions started per dept).
        var perDept = sessions.GroupBy(s => s.DeptCode)
            .ToDictionary(g => g.Key, g => g.Count());
        var signedPerDept = maps.Where(m => m.SignedAt != null).GroupBy(m => m.DeptCode)
            .ToDictionary(g => g.Key, g => g.Count());
        var deptNames = await _db.Departments.ToDictionaryAsync(d => d.DeptCode, d => d.NameAr ?? d.DeptCode);
        ViewBag.SignatureRate = perDept.OrderBy(kv => kv.Key)
            .Select(kv => new { dept = deptNames.TryGetValue(kv.Key, out var n) ? n : kv.Key, started = kv.Value, signed = signedPerDept.TryGetValue(kv.Key, out var s) ? s : 0 })
            .ToList();

        // Completion timeline (sessions completed per day).
        ViewBag.Timeline = sessions.Where(s => s.CompletedAt != null)
            .GroupBy(s => s.CompletedAt!.Value.Date)
            .OrderBy(g => g.Key)
            .Select(g => new { date = g.Key.ToString("yyyy-MM-dd"), count = g.Count() })
            .ToList();

        return View();
    }

    // GET /Admin/Insights/Coverage — strategy element × department heat map.
    [HttpGet("Coverage")]
    public async Task<IActionResult> Coverage(string dim = "Pillar")
    {
        var dimension = dim switch
        {
            "Objective" => CoverageService.ElementDimension.Objective,
            "Initiative" => CoverageService.ElementDimension.Initiative,
            _ => CoverageService.ElementDimension.Pillar,
        };
        ViewBag.Dim = dimension.ToString();
        var matrix = await _coverage.BuildAsync(dimension);
        return View(matrix);
    }

    // GET /Admin/Insights/Pledges — pledge aggregation dashboard.
    [HttpGet("Pledges")]
    public async Task<IActionResult> Pledges()
    {
        var agg = await _pledges.BuildAsync();
        return View(agg);
    }

    // GET /Admin/Insights/Pledges/Csv — CSV export of all pledges.
    [HttpGet("Pledges/Csv")]
    public async Task<IActionResult> PledgesCsv()
    {
        var agg = await _pledges.BuildAsync();
        var sb = new StringBuilder();
        sb.Append('﻿'); // BOM for Excel UTF-8 Arabic
        sb.AppendLine("DeptCode,DeptName,ElementType,ElementCode,ElementLabel,Kind,Notes,CreatedAt");
        foreach (var r in agg.Rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(r.DeptCode), Csv(r.DeptName), Csv(r.ElementType), Csv(r.ElementCode),
                Csv(r.ElementLabel), Csv(r.Kind), Csv(r.Notes),
                Csv(r.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))));
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", "pledges.csv");
    }

    // GET /Admin/Insights/Poster — A2 programme poster (PDF).
    [HttpGet("Poster")]
    public async Task<IActionResult> Poster()
    {
        var pillars = await _db.Pillars.OrderBy(p => p.PlrCode).ToListAsync();
        var depts = await _db.Departments.OrderBy(d => d.DeptCode).ToListAsync();
        var maps = await _db.DepartmentStrategyMaps.AsNoTracking().ToListAsync();

        var posterDepts = depts.Select(d =>
        {
            var map = maps.Where(m => m.DeptCode == d.DeptCode).OrderByDescending(m => m.CreatedAt).FirstOrDefault();
            var status = map == null ? "locked" : (map.SignedAt != null ? "signed" : "pending");
            return new ProgrammePosterPdfService.DeptPoster
            {
                DeptCode = d.DeptCode,
                DeptName = d.NameAr ?? d.DeptCode,
                Status = status,
                ThumbPng = null, // textual fallback rendered by the poster service
            };
        }).ToList();

        var pdf = _poster.Generate(pillars, posterDepts);
        return File(pdf, "application/pdf", "programme-poster-A2.pdf");
    }

    private static string Csv(string? v)
    {
        v ??= "";
        if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        return v;
    }
}
