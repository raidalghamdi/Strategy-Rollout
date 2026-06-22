// Phase 20.8.7 — Export every table in the SQLite database to a single Excel
// workbook (one sheet per table). Admin-only. Uses ClosedXML which is already
// referenced by the project.
using System.Data;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Controllers;

[Authorize(Roles = "Admin")]
[Route("Admin/DbExport")]
public class AdminDbExportController : Controller
{
    private readonly ApplicationDbContext _db;

    public AdminDbExportController(ApplicationDbContext db)
    {
        _db = db;
    }

    // GET /Admin/DbExport
    public async Task<IActionResult> Index()
    {
        var tables = await LoadTableSummariesAsync();
        return View(tables);
    }

    // GET /Admin/DbExport/Download
    [HttpGet("Download")]
    public async Task<IActionResult> Download()
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync();

        var tableNames = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                @"SELECT name FROM sqlite_master
                  WHERE type='table' AND name NOT LIKE 'sqlite_%'
                  ORDER BY name;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tableNames.Add(reader.GetString(0));
        }

        using var wb = new XLWorkbook();
        // Phase 20.10 — unify exports on the website font (Cairo).
        wb.Style.Font.FontName = "Cairo";

        // Cover sheet
        var cover = wb.Worksheets.Add("نظرة عامة");
        cover.RightToLeft = true;
        cover.Cell("A1").Value = "تصدير قاعدة بيانات StrategyHouse";
        cover.Cell("A1").Style.Font.Bold = true;
        cover.Cell("A1").Style.Font.FontSize = 16;
        cover.Cell("A2").Value = $"تاريخ التصدير: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC";
        cover.Cell("A3").Value = $"عدد الجداول: {tableNames.Count}";

        cover.Cell("A5").Value = "الجدول";
        cover.Cell("B5").Value = "عدد الصفوف";
        cover.Range("A5:B5").Style.Fill.BackgroundColor = XLColor.FromHtml("#00192B");
        cover.Range("A5:B5").Style.Font.FontColor = XLColor.White;
        cover.Range("A5:B5").Style.Font.Bold = true;

        int summaryRow = 6;
        foreach (var t in tableNames)
        {
            // Write each table as its own sheet
            var sheet = wb.Worksheets.Add(SafeSheetName(t));
            sheet.RightToLeft = true;
            long rowCount = await WriteTableAsync(conn, t, sheet);

            cover.Cell(summaryRow, 1).Value = t;
            cover.Cell(summaryRow, 2).Value = rowCount;
            summaryRow++;
        }

        cover.Columns().AdjustToContents();

        // Stream to bytes
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var fileName = $"StrategyHouse_DB_Export_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx";
        return File(
            ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    // ----- helpers ----------------------------------------------------------

    private async Task<List<TableSummary>> LoadTableSummariesAsync()
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync();

        var result = new List<TableSummary>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                @"SELECT name FROM sqlite_master
                  WHERE type='table' AND name NOT LIKE 'sqlite_%'
                  ORDER BY name;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) result.Add(new TableSummary { Name = reader.GetString(0) });
        }

        foreach (var t in result)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM \"{t.Name.Replace("\"", "\"\"")}\";";
            try { t.RowCount = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0); }
            catch { t.RowCount = -1; }
        }

        return result;
    }

    private static async Task<long> WriteTableAsync(System.Data.Common.DbConnection conn, string table, IXLWorksheet sheet)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"")}\";";
        using var reader = await cmd.ExecuteReaderAsync();

        // Header row
        var colCount = reader.FieldCount;
        for (int c = 0; c < colCount; c++)
        {
            sheet.Cell(1, c + 1).Value = reader.GetName(c);
        }
        var header = sheet.Range(1, 1, 1, colCount);
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#00192B");
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);

        long rowCount = 0;
        int rowIdx = 2;
        while (await reader.ReadAsync())
        {
            for (int c = 0; c < colCount; c++)
            {
                if (reader.IsDBNull(c))
                {
                    // leave blank
                    continue;
                }
                var v = reader.GetValue(c);
                var cell = sheet.Cell(rowIdx, c + 1);
                switch (v)
                {
                    case bool b:   cell.Value = b; break;
                    case byte[] _: cell.Value = "[binary]"; break;
                    case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm"; break;
                    case int i:    cell.Value = i; break;
                    case long l:   cell.Value = l; break;
                    case double d: cell.Value = d; break;
                    case decimal m: cell.Value = m; break;
                    default:
                        var s = v.ToString() ?? string.Empty;
                        // Excel cell hard limit is 32767
                        if (s.Length > 32000) s = s.Substring(0, 32000) + "…(تم الاقتطاع)";
                        cell.Value = s;
                        break;
                }
            }
            rowIdx++;
            rowCount++;
        }

        // Auto-size up to a sensible max so giant text columns don't blow up width
        for (int c = 1; c <= colCount; c++)
        {
            try
            {
                sheet.Column(c).AdjustToContents();
                if (sheet.Column(c).Width > 60) sheet.Column(c).Width = 60;
            }
            catch { /* ignore */ }
        }

        return rowCount;
    }

    private static string SafeSheetName(string raw)
    {
        // Excel sheet names: max 31 chars, no  : \ / ? * [ ]
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        var clean = new string(raw.Where(c => !invalid.Contains(c)).ToArray());
        if (clean.Length > 31) clean = clean.Substring(0, 31);
        return string.IsNullOrWhiteSpace(clean) ? "Sheet" : clean;
    }

    public class TableSummary
    {
        public string Name { get; set; } = "";
        public long RowCount { get; set; }
    }
}
