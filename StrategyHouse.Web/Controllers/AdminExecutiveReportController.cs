using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 13 — the comprehensive executive report: HTML dashboard plus branded PDF, CSV,
// PowerPoint and Excel exports (Phase 13.1), with optional email delivery.
[Authorize(Roles = "Admin,Facilitator")]
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

    public AdminExecutiveReportController(
        ExecutiveReportService service,
        ExecutiveReportPdfDocument pdf,
        ExecutiveReportExcelBuilder excel,
        ExecutiveReportPowerPointBuilder pptx,
        ReportEmailService email)
    {
        _service = service;
        _pdf = pdf;
        _excel = excel;
        _pptx = pptx;
        _email = email;
    }

    private static string FileBase => $"Executive_Report_{DateTime.UtcNow:yyyy-MM-dd}";

    [HttpGet("ExecutiveReport")]
    public async Task<IActionResult> ExecutiveReport()
    {
        var vm = await _service.BuildAsync();
        return View(vm);
    }

    [HttpGet("ExecutiveReport.pdf")]
    public async Task<IActionResult> ExecutiveReportPdf()
    {
        var vm = await _service.BuildAsync();
        return File(_pdf.Generate(vm), PdfMime, $"{FileBase}.pdf");
    }

    [HttpGet("ExecutiveReport.pptx")]
    public async Task<IActionResult> ExecutiveReportPptx()
    {
        var vm = await _service.BuildAsync();
        return File(_pptx.Build(vm), PptxMime, $"{FileBase}.pptx");
    }

    [HttpGet("ExecutiveReport.xlsx")]
    public async Task<IActionResult> ExecutiveReportXlsx()
    {
        var vm = await _service.BuildAsync();
        return File(_excel.Build(vm), XlsxMime, $"{FileBase}.xlsx");
    }

    [HttpGet("ExecutiveReport.csv")]
    public async Task<IActionResult> ExecutiveReportCsv()
    {
        var vm = await _service.BuildAsync();
        return File(BuildCsv(vm), CsvMime, $"{FileBase}.csv");
    }

    [HttpPost("ExecutiveReport/Email")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EmailReport(string email, string format)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return Json(new { success = false, message = "يرجى إدخال بريد إلكتروني صحيح." });

        var vm = await _service.BuildAsync();
        byte[] bytes; string mime, ext;
        switch ((format ?? "pdf").ToLowerInvariant())
        {
            case "pptx": bytes = _pptx.Build(vm); mime = PptxMime; ext = "pptx"; break;
            case "xlsx": bytes = _excel.Build(vm); mime = XlsxMime; ext = "xlsx"; break;
            case "csv": bytes = BuildCsv(vm); mime = CsvMime; ext = "csv"; break;
            default: bytes = _pdf.Generate(vm); mime = PdfMime; ext = "pdf"; break;
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
        AppendRow(sb, "نظرة عامة", "إجمالي الجلسات", vm.Overview.TotalSessions.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "نظرة عامة", "الجلسات المكتملة", vm.Overview.TotalCompletedSessions.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "نظرة عامة", "إجمالي الحضور", vm.Overview.TotalAttendees.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "نظرة عامة", "الإدارات المشاركة", vm.Overview.TotalDepartmentsEngaged.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "نظرة عامة", "متوسط الاختبار (من 5)", vm.Overview.AvgQuizScore.ToString("0.##", CultureInfo.InvariantCulture));
        AppendRow(sb, "نظرة عامة", "وضوح الاستراتيجية (من 5)", vm.Overview.AvgSurveyClarity.ToString("0.##", CultureInfo.InvariantCulture));
        AppendRow(sb, "نظرة عامة", "القدرة على المساهمة (من 5)", vm.Overview.AvgContributionCapability.ToString("0.##", CultureInfo.InvariantCulture));
        AppendRow(sb, "نظرة عامة", "الخرائط الاستراتيجية", vm.MapsCount.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "نظرة عامة", "تواقيع الفرق", vm.GroupSignatures.TotalCount.ToString(CultureInfo.InvariantCulture));

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
