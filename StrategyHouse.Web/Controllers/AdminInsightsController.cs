using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    public AdminInsightsController(
        ApplicationDbContext db,
        CoverageService coverage,
        PledgeAggregateService pledges,
        ProgrammePosterPdfService poster,
        StrategyContentService content)
    {
        _db = db;
        _coverage = coverage;
        _pledges = pledges;
        _poster = poster;
        _content = content;
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
