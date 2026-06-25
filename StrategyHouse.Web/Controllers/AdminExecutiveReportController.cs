using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StrategyHouse.Web.Models;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 13 — the comprehensive executive report: HTML dashboard plus branded PDF, CSV,
// PowerPoint and Excel exports (Phase 13.1), with optional email delivery.
// Phase 20.33 (Comment 8) — CX role gets full executive report access
[Authorize(Roles = "Admin,Facilitator,CX")]
[Route("Admin")]
public class AdminExecutiveReportController : Controller
{
    private const string PdfMime = "application/pdf";
    private const string CsvMime = "text/csv";
    private const string PptxMime = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly ExecutiveReportService _service;
    private readonly ExecutiveReportPdfDocument _pdf;
    private readonly ExecutiveReportExcelBuilder _excel;
    private readonly ExecutiveReportPowerPointBuilder _pptx;
    private readonly ReportEmailService _email;
    private readonly StrategyDataReportService _strategyReport;
    private readonly ILogger<AdminExecutiveReportController> _logger;

    public AdminExecutiveReportController(
        ExecutiveReportService service,
        ExecutiveReportPdfDocument pdf,
        ExecutiveReportExcelBuilder excel,
        ExecutiveReportPowerPointBuilder pptx,
        ReportEmailService email,
        StrategyDataReportService strategyReport,
        ILogger<AdminExecutiveReportController> logger)
    {
        _service = service;
        _pdf = pdf;
        _excel = excel;
        _pptx = pptx;
        _email = email;
        _strategyReport = strategyReport;
        _logger = logger;
    }

    private static string FileBase => $"Executive_Report_{DateTime.UtcNow:yyyy-MM-dd}";

    // Resolve the section selection for a request: explicit ?sections= wins, else the saved
    // cookie, else all sections. Used by the HTML view and every export.
    private ExecReportSections ResolveSections(string? sections)
    {
        if (!string.IsNullOrWhiteSpace(sections))
            return ExecReportSections.Parse(sections);
        if (Request.Cookies.TryGetValue(ExecReportSections.CookieName, out var cookie) && !string.IsNullOrWhiteSpace(cookie))
            return ExecReportSections.Parse(cookie);
        return ExecReportSections.AllSelected();
    }

    [HttpGet("ExecutiveReport")]
    public async Task<IActionResult> ExecutiveReport(string? sections = null)
    {
        var vm = await _service.BuildAsync(ResolveSections(sections));
        return View(vm);
    }

    // Phase 19.21 (Fix 4) — strategy-data executive report, rebuilt on the Survey
    // analysis pattern: an on-page summary of the External strategy entities plus a
    // one-click .xlsx export. Robust: the service never throws and the summary
    // degrades to zeros if no data is available.
    [HttpGet("StrategyReport")]
    public async Task<IActionResult> StrategyReport()
    {
        var report = await _strategyReport.BuildSummaryAsync();
        return View(report);
    }

    [HttpGet("StrategyReport.xlsx")]
    public async Task<IActionResult> StrategyReportXlsx()
    {
        var report = new StrategyDataReportService.Report();
        var bytes = await _strategyReport.BuildExcelAsync(report);
        return File(bytes, XlsxMime, $"Strategy_Report_{DateTime.UtcNow:yyyy-MM-dd}.xlsx");
    }

    // "حفظ كافتراضي" — persist the current selection in a cookie for future visits.
    [HttpPost("ExecutiveReport/SaveDefault")]
    [ValidateAntiForgeryToken]
    public IActionResult SaveDefault(string? sections)
    {
        var resolved = ExecReportSections.Parse(sections);
        Response.Cookies.Append(ExecReportSections.CookieName, resolved.ToQueryValue(), new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly = false,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
        });
        return Json(new { success = true, message = "تم حفظ الأقسام المختارة كافتراضي." });
    }

    // Phase 19.20 (Fix 5) — every export builds its bytes inside a guard so a builder
    // failure (or empty dataset) yields a valid downloadable file with a friendly Arabic
    // placeholder instead of a 500 error page. The HTML report itself already tolerates an
    // empty dataset (all counts default to zero), so the only remaining risk is a builder
    // exception — this is where we catch it.
    [HttpGet("ExecutiveReport.pdf")]
    public async Task<IActionResult> ExecutiveReportPdf(string? sections = null)
    {
        try
        {
            var vm = await _service.BuildAsync(ResolveSections(sections));
            return File(_pdf.Generate(vm), PdfMime, $"{FileBase}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Executive report PDF export failed.");
            return File(PlaceholderCsv(), CsvMime, $"{FileBase}.csv");
        }
    }

    [HttpGet("ExecutiveReport.pptx")]
    public async Task<IActionResult> ExecutiveReportPptx(string? sections = null)
    {
        try
        {
            var vm = await _service.BuildAsync(ResolveSections(sections));
            return File(_pptx.Build(vm), PptxMime, $"{FileBase}.pptx");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Executive report PowerPoint export failed.");
            return File(PlaceholderCsv(), CsvMime, $"{FileBase}.csv");
        }
    }

    [HttpGet("ExecutiveReport.xlsx")]
    public async Task<IActionResult> ExecutiveReportXlsx(string? sections = null)
    {
        try
        {
            var vm = await _service.BuildAsync(ResolveSections(sections));
            return File(_excel.Build(vm), XlsxMime, $"{FileBase}.xlsx");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Executive report Excel export failed.");
            return File(PlaceholderCsv(), CsvMime, $"{FileBase}.csv");
        }
    }

    [HttpGet("ExecutiveReport.csv")]
    public async Task<IActionResult> ExecutiveReportCsv(string? sections = null)
    {
        try
        {
            var vm = await _service.BuildAsync(ResolveSections(sections));
            return File(BuildCsv(vm), CsvMime, $"{FileBase}.csv");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Executive report CSV export failed.");
            return File(PlaceholderCsv(), CsvMime, $"{FileBase}.csv");
        }
    }

    // Minimal valid CSV used when a richer export can't be produced.
    private static byte[] PlaceholderCsv()
    {
        var sb = new StringBuilder();
        sb.Append('﻿'); // UTF-8 BOM so Excel renders Arabic correctly.
        sb.AppendLine("القسم,المؤشر,القيمة");
        sb.AppendLine("تنبيه,تعذّر إنشاء التقرير حالياً,لا توجد بيانات كافية");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    [HttpPost("ExecutiveReport/Email")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EmailReport(string email, string format, string? sections = null)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return Json(new { success = false, message = "يرجى إدخال بريد إلكتروني صحيح." });

        // Phase 19.20 (Fix 5) — guard report assembly + attachment building. The email
        // service already returns a friendly message when SMTP isn't configured; here we
        // additionally ensure a builder failure produces a friendly JSON, never a 500.
        byte[] bytes; string mime, ext;
        try
        {
            var vm = await _service.BuildAsync(ResolveSections(sections));
            switch ((format ?? "pdf").ToLowerInvariant())
            {
                case "pptx": bytes = _pptx.Build(vm); mime = PptxMime; ext = "pptx"; break;
                case "xlsx": bytes = _excel.Build(vm); mime = XlsxMime; ext = "xlsx"; break;
                case "csv": bytes = BuildCsv(vm); mime = CsvMime; ext = "csv"; break;
                default: bytes = _pdf.Generate(vm); mime = PdfMime; ext = "pdf"; break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Executive report email build failed for format {Format}.", format);
            return Json(new { success = false, message = "تعذّر إنشاء التقرير للإرسال حالياً. يرجى المحاولة لاحقاً." });
        }

        var fileName = $"{FileBase}.{ext}";
        var body = "<p style=\"font-family:sans-serif;direction:rtl\">مرفق التقرير التنفيذي الشامل من الهيئة العامة للمنافسة.</p>";
        var result = await _email.SendReportAsync(email, "التقرير التنفيذي الشامل — الهيئة العامة للمنافسة", body, fileName, bytes, mime);
        return Json(new { success = result.Sent, message = result.Reason });
    }

    private static byte[] BuildCsv(Models.ExecutiveReportViewModel vm)
    {
        var sb = new StringBuilder();
        sb.Append('﻿'); // UTF-8 BOM so Excel renders Arabic correctly.

        sb.AppendLine("القسم,المؤشر,القيمة");
        if (vm.Sections.Has(ExecReportSections.Overview))
        {
            AppendRow(sb, "نظرة عامة", "إجمالي الجلسات", vm.Overview.TotalSessions.ToString(CultureInfo.InvariantCulture));
            AppendRow(sb, "نظرة عامة", "الجلسات المكتملة", vm.Overview.TotalCompletedSessions.ToString(CultureInfo.InvariantCulture));
            AppendRow(sb, "نظرة عامة", "إجمالي الحضور", vm.Overview.TotalAttendees.ToString(CultureInfo.InvariantCulture));
            AppendRow(sb, "نظرة عامة", "الإدارات المشاركة", vm.Overview.TotalDepartmentsEngaged.ToString(CultureInfo.InvariantCulture));
            AppendRow(sb, "نظرة عامة", "متوسط الاختبار (من 5)", vm.Overview.AvgQuizScore.ToString("0.##", CultureInfo.InvariantCulture));
            AppendRow(sb, "نظرة عامة", "وضوح الاستراتيجية (من 5)", vm.Overview.AvgSurveyClarity.ToString("0.##", CultureInfo.InvariantCulture));
            AppendRow(sb, "نظرة عامة", "القدرة على المساهمة (من 5)", vm.Overview.AvgContributionCapability.ToString("0.##", CultureInfo.InvariantCulture));
            AppendRow(sb, "نظرة عامة", "الخرائط الاستراتيجية", vm.MapsCount.ToString(CultureInfo.InvariantCulture));
            AppendRow(sb, "نظرة عامة", "تواقيع الفرق", vm.GroupSignatures.TotalCount.ToString(CultureInfo.InvariantCulture));
        }

        if (vm.Sections.Has(ExecReportSections.Departments))
        {
            sb.AppendLine();
            sb.AppendLine("الإدارة,الجلسات,الحضور,نسبة الإكمال %");
            foreach (var d in vm.DepartmentBreakdown)
            {
                sb.Append(Csv(d.DeptName)).Append(',')
                  .Append(d.SessionsCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(d.AttendeesCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(d.CompletionRate.ToString("0.#", CultureInfo.InvariantCulture))
                  .Append('\n');
            }
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void AppendRow(StringBuilder sb, string section, string metric, string value)
        => sb.Append(Csv(section)).Append(',').Append(Csv(metric)).Append(',').Append(Csv(value)).Append('\n');

    private static string Csv(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
