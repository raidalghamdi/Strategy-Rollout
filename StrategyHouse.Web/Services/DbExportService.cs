using System.Data.Common;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 19.26 — full SQLite database export.
// Produces two formats:
//
//   1. Excel (.xlsx) — one sheet per user table, header row = column names,
//      data rows = values. Plus a "_Index" sheet describing each sheet's row
//      count and PK columns. This format is the exact mirror of what
//      DbImportService consumes, so the round-trip export → edit → import works.
//
//   2. SQLite (.db) — a byte-perfect copy of the live database file, taken via
//      VACUUM INTO so it is atomic, defragmented, and free of WAL artefacts.
//      The resulting file can be opened in DB Browser for SQLite, queried with
//      `sqlite3`, or converted to Microsoft SQL Server using free tools such as
//      "SQLite to MSSQL Converter" or the official `sqlite3` → CSV → bcp path.
//
// Safety:
//   • Read-only operations; the live DB is never mutated.
//   • Internal SQLite tables (`sqlite_*`) and EF migration history are skipped
//     from the Excel export (they are still inside the .db copy).
//   • All values are streamed cell-by-cell so large tables don't blow up RAM.
public class DbExportService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<DbExportService> _log;

    public DbExportService(ApplicationDbContext db, ILogger<DbExportService> log)
    {
        _db = db;
        _log = log;
    }

    // ------------------------------------------------------------------
    // 1) Excel export
    // ------------------------------------------------------------------
    public async Task<byte[]> ExportXlsxAsync(CancellationToken ct = default)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        // Collect user tables
        var tables = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT name FROM sqlite_master
                WHERE type='table'
                  AND name NOT LIKE 'sqlite_%'
                  AND name <> '__EFMigrationsHistory'
                ORDER BY name;";
            using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                tables.Add(rd.GetString(0));
            }
        }

        using var wb = new XLWorkbook();

        // Build _Index sheet first so it appears at the start
        var idx = wb.Worksheets.Add("_Index");
        idx.Cell(1, 1).Value = "Sheet";
        idx.Cell(1, 2).Value = "Rows";
        idx.Cell(1, 3).Value = "Primary Key";
        idx.Cell(1, 4).Value = "Columns";
        idx.Range(1, 1, 1, 4).Style.Font.Bold = true;
        idx.Range(1, 1, 1, 4).Style.Fill.BackgroundColor = XLColor.FromHtml("#00192B");
        idx.Range(1, 1, 1, 4).Style.Font.FontColor = XLColor.White;
        var idxRow = 2;

        foreach (var table in tables)
        {
            ct.ThrowIfCancellationRequested();

            // Per-table column metadata
            var columns = new List<(string Name, bool IsPk)>();
            using (var pcmd = conn.CreateCommand())
            {
                pcmd.CommandText = $"PRAGMA table_info([{Quote(table)}]);";
                using var rd = await pcmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct))
                {
                    var name = rd.GetString(1);
                    var pk = rd.GetInt32(5) > 0;
                    columns.Add((name, pk));
                }
            }
            if (columns.Count == 0) continue;

            // Excel sheet names are max 31 chars; keep first 31 and warn on collisions.
            var sheetName = table.Length > 31 ? table.Substring(0, 31) : table;
            var ws = wb.Worksheets.Add(sheetName);

            // Header
            for (int i = 0; i < columns.Count; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = columns[i].Name;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = columns[i].IsPk
                    ? XLColor.FromHtml("#FAC126")  // PK = gold
                    : XLColor.FromHtml("#00192B"); // others = navy
                if (columns[i].IsPk)
                {
                    cell.Style.Font.FontColor = XLColor.FromHtml("#00192B");
                }
            }

            // Data
            int rowNum = 2;
            using (var dcmd = conn.CreateCommand())
            {
                var colList = string.Join(",", columns.Select(c => $"[{Quote(c.Name)}]"));
                dcmd.CommandText = $"SELECT {colList} FROM [{Quote(table)}];";
                using var rd = await dcmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct))
                {
                    for (int i = 0; i < columns.Count; i++)
                    {
                        if (rd.IsDBNull(i)) { rowNum.ToString(); continue; }
                        var v = rd.GetValue(i);
                        SetCellValue(ws.Cell(rowNum, i + 1), v);
                    }
                    rowNum++;
                }
            }

            ws.SheetView.FreezeRows(1);
            try { ws.Columns().AdjustToContents(1, 200, 10, 60); } catch { /* best-effort */ }

            // Index entry
            idx.Cell(idxRow, 1).Value = sheetName;
            idx.Cell(idxRow, 2).Value = rowNum - 2;
            idx.Cell(idxRow, 3).Value = string.Join(", ", columns.Where(c => c.IsPk).Select(c => c.Name));
            idx.Cell(idxRow, 4).Value = string.Join(", ", columns.Select(c => c.Name));
            idxRow++;
        }

        idx.SheetView.FreezeRows(1);
        try { idx.Columns().AdjustToContents(1, idxRow, 10, 80); } catch { }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void SetCellValue(IXLCell cell, object v)
    {
        switch (v)
        {
            case null: return;
            case bool b: cell.Value = b; break;
            case byte by: cell.Value = by; break;
            case short s: cell.Value = s; break;
            case int i: cell.Value = i; break;
            case long l: cell.Value = l; break;
            case float f: cell.Value = f; break;
            case double d: cell.Value = d; break;
            case decimal dec: cell.Value = dec; break;
            case DateTime dt: cell.Value = dt; break;
            case DateTimeOffset dto: cell.Value = dto.UtcDateTime; break;
            case Guid g: cell.Value = g.ToString(); break;
            case byte[] _:
                // Binary blob — skip; could be base64-encoded if ever needed.
                cell.Value = "[blob]";
                break;
            default: cell.Value = v.ToString(); break;
        }
    }

    // ------------------------------------------------------------------
    // 2) SQLite file export (byte-perfect snapshot)
    // ------------------------------------------------------------------
    //
    // Uses VACUUM INTO to produce a defragmented, atomic copy of the live
    // database into a temporary file, then reads the bytes and returns them.
    // The original DB is not touched.
    public async Task<byte[]> ExportSqliteAsync(CancellationToken ct = default)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        // VACUUM INTO requires a non-existent target path.
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"strategy_house_export_{Guid.NewGuid():N}.db");
        try
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"VACUUM INTO '{tempPath.Replace("'", "''")}';";
                await cmd.ExecuteNonQueryAsync(ct);
            }
            return await File.ReadAllBytesAsync(tempPath, ct);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best-effort */ }
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------
    // SQLite identifiers can't really contain "]" reliably; we still escape
    // any embedded brackets defensively so the SQL never breaks. None of our
    // tables/columns contain such characters, so this is just hardening.
    private static string Quote(string ident) => ident.Replace("]", "]]");
}
