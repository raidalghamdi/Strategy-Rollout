using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 13 — the comprehensive executive report: HTML dashboard, branded PDF and CSV exports.
[Authorize(Roles = "Admin,Facilitator")]
[Route("Admin")]
public class AdminExecutiveReportController : Controller
{
    private readonly ExecutiveReportService _service;
    private readonly ExecutiveReportPdfDocument _pdf;

    public AdminExecutiveReportController(ExecutiveReportService service, ExecutiveReportPdfDocument pdf)
    {
        _service = service;
        _pdf = pdf;
    }

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
        var bytes = _pdf.Generate(vm);
        return File(bytes, "application/pdf", "executive-report.pdf");
    }

    [HttpGet("ExecutiveReport.csv")]
    public async Task<IActionResult> ExecutiveReportCsv()
    {
        var vm = await _service.BuildAsync();
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

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", "executive-report.csv");
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
