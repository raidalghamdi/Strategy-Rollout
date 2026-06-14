using System.IO.Compression;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Controllers;

[Authorize(Roles = "Admin")]
[Route("Admin/Wall")]
public class AdminWallController : Controller
{
    private readonly ApplicationDbContext _db;

    public AdminWallController(ApplicationDbContext db) { _db = db; }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var maps = await _db.DepartmentStrategyMaps
            .Where(m => m.SignedAt != null && m.IsActive)
            .OrderByDescending(m => m.SignedAt)
            .ToListAsync();
        ViewBag.DeptNames = await _db.Departments.ToDictionaryAsync(d => d.DeptCode, d => d.NameAr ?? d.DeptCode);
        return View(maps);
    }

    [HttpGet("Pdf/{mapId}")]
    public async Task<IActionResult> Pdf(Guid mapId)
    {
        var map = await _db.DepartmentStrategyMaps.FindAsync(mapId);
        if (map?.PdfBlob == null) return NotFound();
        return File(map.PdfBlob, "application/pdf", $"strategy-map-{map.DeptCode}.pdf");
    }

    [HttpGet("ExportAll")]
    public async Task<IActionResult> ExportAll()
    {
        var maps = await _db.DepartmentStrategyMaps
            .Where(m => m.SignedAt != null && m.PdfBlob != null)
            .ToListAsync();

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var map in maps)
            {
                var entry = zip.CreateEntry($"{map.DeptCode}-{map.Id}.pdf");
                using var es = entry.Open();
                es.Write(map.PdfBlob!, 0, map.PdfBlob!.Length);
            }
        }
        ms.Position = 0;
        return File(ms.ToArray(), "application/zip", "strategy-maps.zip");
    }
}
