using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Models.DataSources;
using StrategyHouse.Web.Services;

namespace StrategyHouse.Web.Controllers;

// Phase 19.23 — operator visibility into the unified strategy data source.
// Shows which source (Mirror | Sqlite | Empty) each entity resolved to and the row
// counts, and lets the admin edit the Department↔Division bridge map stored in
// PageContent under "department.divisions.json".
[Authorize(Roles = "Admin")]
[Route("Admin/DataSources")]
public class AdminDataSourcesController : Controller
{
    private readonly IStrategyDataSource _source;
    private readonly ApplicationDbContext _db;
    private readonly PageContentService _content;

    public AdminDataSourcesController(IStrategyDataSource source, ApplicationDbContext db, PageContentService content)
    {
        _source = source;
        _db = db;
        _content = content;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var counts = await _source.GetCountsAsync(ct);
        var trace = await _source.GetLastTraceAsync();
        var departments = await _db.Departments.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.NameAr)
            .Select(d => new DepartmentOption(d.DeptCode, d.NameAr ?? d.DeptCode))
            .ToListAsync(ct);

        var map = ReadMap();

        var vm = new DataSourcesViewModel
        {
            Counts = counts,
            Trace = trace,
            Departments = departments,
            DivisionMapJson = Serialize(map),
        };
        return View(vm);
    }

    [HttpPost("SaveMap")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveMap(string deptCode, string divisions)
    {
        if (string.IsNullOrWhiteSpace(deptCode))
        {
            TempData["Error"] = "يرجى اختيار الإدارة.";
            return RedirectToAction(nameof(Index));
        }

        var names = (divisions ?? string.Empty)
            .Split(new[] { '\n', '،', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var map = ReadMap();
        if (names.Count == 0)
            map.Remove(deptCode);
        else
            map[deptCode] = names;

        await _content.SaveAsync(_db, UnifiedStrategyDataSource.DivisionMapKey, Serialize(map));
        TempData["Saved"] = $"تم حفظ ربط {names.Count} قسم للإدارة {deptCode}.";
        return RedirectToAction(nameof(Index));
    }

    private Dictionary<string, List<string>> ReadMap()
    {
        var json = _content.Get(UnifiedStrategyDataSource.DivisionMapKey, "");
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, List<string>>(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json)
                ?? new Dictionary<string, List<string>>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, List<string>>(StringComparer.Ordinal);
        }
    }

    private static string Serialize(Dictionary<string, List<string>> map) =>
        JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = false });
}
