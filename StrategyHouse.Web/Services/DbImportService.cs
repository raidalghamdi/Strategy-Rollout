using System.Data;
using System.Data.Common;
using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;
using StrategyHouse.Web.Models.DbImport;

namespace StrategyHouse.Web.Services;

// Phase 19.22 — Excel → SQLite full-mirror import. The uploaded .xlsx is the one produced
// by the DB export workflow: one sheet per table, header row = column names, data rows =
// values, plus a metadata "_Index" sheet that is ignored.
//
// SQLite-only. Works at the raw ADO.NET level (DbConnection/DbCommand obtained from the
// EF context) rather than mapping EF entities, so Identity (AspNet*) and mirror tables are
// handled uniformly with composite-PK support. Every value is parameterized — identifiers
// are quoted, values are never string-concatenated.
//
// Safety: AnalyzeAsync only reads (dry run). ApplyAsync takes a timestamped backup of the
// db file, runs all writes inside a single transaction with foreign_keys OFF, and rolls
// back on any error. No schema changes, no migrations.
public class DbImportService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<DbImportService> _log;

    private const string IndexSheet = "_Index";

    // Unit-separator joins composite-PK parts into a single comparable key string. It will
    // not appear inside identifier/text PK values exported from Excel.
    private const char KeySep = '\u001F';

    public DbImportService(ApplicationDbContext db, ILogger<DbImportService> log)
    {
        _db = db;
        _log = log;
    }

    private sealed class ColumnInfo
    {
        public string Name = "";
        public string Type = "";
        public bool IsPk;
        public int PkOrder;
    }

    // ---- Schema discovery ---------------------------------------------------

    private async Task<bool> TableExistsAsync(DbConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n LIMIT 1;";
        AddParam(cmd, "$n", table);
        var r = await cmd.ExecuteScalarAsync();
        return r != null && r != DBNull.Value;
    }

    private async Task<List<ColumnInfo>> GetColumnsAsync(DbConnection conn, string table)
    {
        // PRAGMA doesn't accept parameters; the table name comes from sqlite_master
        // (already verified to exist) and is wrapped in quotes, so it's safe here.
        var cols = new List<ColumnInfo>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({QuoteId(table)});";
        using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            cols.Add(new ColumnInfo
            {
                Name = rdr.GetString(1),
                Type = rdr.IsDBNull(2) ? "" : rdr.GetString(2),
                PkOrder = rdr.IsDBNull(5) ? 0 : Convert.ToInt32(rdr.GetValue(5), CultureInfo.InvariantCulture),
                IsPk = !rdr.IsDBNull(5) && Convert.ToInt32(rdr.GetValue(5), CultureInfo.InvariantCulture) > 0,
            });
        }
        return cols;
    }

    private static bool IsSkippableSheet(string name)
        => string.Equals(name, IndexSheet, StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, "__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase);

    // Read a worksheet into header + string rows (mirrors the Survey parser's robustness).
    private static (List<string> Headers, List<List<string>> Rows) ReadSheet(IXLWorksheet ws)
    {
        var used = ws.RangeUsed();
        var headers = new List<string>();
        var rows = new List<List<string>>();
        if (used == null) return (headers, rows);

        var usedRows = used.RowsUsed().ToList();
        if (usedRows.Count == 0) return (headers, rows);

        var headerRow = usedRows[0];
        headers = headerRow.Cells().Select(c => c.GetString().Trim()).ToList();
        while (headers.Count > 0 && string.IsNullOrWhiteSpace(headers[^1]))
            headers.RemoveAt(headers.Count - 1);
        if (headers.Count == 0) return (headers, rows);

        for (var r = 1; r < usedRows.Count; r++)
        {
            var row = usedRows[r];
            var values = new List<string>(headers.Count);
            var any = false;
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = row.Cell(i + 1);
                var text = cell.GetString().Trim();
                if (!string.IsNullOrEmpty(text)) any = true;
                values.Add(text);
            }
            if (any) rows.Add(values);
        }
        return (headers, rows);
    }

    // ---- Dry-run analysis ---------------------------------------------------

    public async Task<DbImportPreview> AnalyzeAsync(Stream xlsxStream)
    {
        var preview = new DbImportPreview();
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync();

        using var wb = new XLWorkbook(xlsxStream);
        foreach (var ws in wb.Worksheets)
        {
            var table = ws.Name;
            if (IsSkippableSheet(table)) { preview.SkippedSheets.Add(table); continue; }

            if (!await TableExistsAsync(conn, table))
            {
                preview.Warnings.Add($"تم تجاهل الورقة «{table}» لعدم وجود جدول مطابق في قاعدة البيانات.");
                preview.SkippedSheets.Add(table);
                continue;
            }

            var cols = await GetColumnsAsync(conn, table);
            var pkCols = cols.Where(c => c.IsPk).OrderBy(c => c.PkOrder).Select(c => c.Name).ToList();
            if (pkCols.Count == 0)
            {
                preview.Warnings.Add($"تم تخطّي الجدول «{table}» لعدم وجود مفتاح أساسي (PK) — لا يمكن مزامنته بأمان.");
                preview.SkippedSheets.Add(table);
                continue;
            }

            var (headers, rows) = ReadSheet(ws);
            var dbColSet = cols.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var headerToDb = headers
                .Select((h, idx) => (h, idx))
                .Where(x => dbColSet.Contains(x.h))
                .ToDictionary(x => x.h, x => x.idx, StringComparer.OrdinalIgnoreCase);

            var diff = new DbImportPreview.SheetDiff { Table = table, ExcelRows = rows.Count };

            // Every PK column must be present in the Excel header, otherwise we can't diff.
            var missingPk = pkCols.Where(pk => !headerToDb.ContainsKey(pk)).ToList();
            if (missingPk.Count > 0)
            {
                preview.Warnings.Add($"تم تخطّي الجدول «{table}» لأن أعمدة المفتاح الأساسي غير موجودة في الملف: {string.Join("، ", missingPk)}.");
                preview.SkippedSheets.Add(table);
                continue;
            }

            var ignoredCols = headers.Where(h => !string.IsNullOrWhiteSpace(h) && !dbColSet.Contains(h)).ToList();
            if (ignoredCols.Count > 0)
                diff.Notes.Add($"أعمدة في الملف غير موجودة في الجدول وسيتم تجاهلها: {string.Join("، ", ignoredCols)}.");

            // Existing PK tuples in the DB.
            var dbKeys = await LoadDbKeysAsync(conn, table, pkCols);
            diff.DbRows = dbKeys.Count;

            var excelKeys = new HashSet<string>();
            foreach (var row in rows)
            {
                var key = BuildKey(pkCols, headerToDb, row);
                if (key == null) continue;
                if (!excelKeys.Add(key)) continue; // duplicate PK in Excel — count once
                if (dbKeys.Contains(key)) diff.Updates++;
                else diff.Inserts++;
            }
            diff.Deletes = dbKeys.Count(k => !excelKeys.Contains(k));

            preview.Sheets.Add(diff);
        }

        preview.Sheets = preview.Sheets.OrderBy(s => s.Table, StringComparer.OrdinalIgnoreCase).ToList();
        return preview;
    }

    private async Task<HashSet<string>> LoadDbKeysAsync(DbConnection conn, string table, List<string> pkCols)
    {
        var keys = new HashSet<string>();
        using var cmd = conn.CreateCommand();
        var colList = string.Join(",", pkCols.Select(QuoteId));
        cmd.CommandText = $"SELECT {colList} FROM {QuoteId(table)};";
        using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            var parts = new string[pkCols.Count];
            for (var i = 0; i < pkCols.Count; i++)
                parts[i] = rdr.IsDBNull(i) ? "" : Convert.ToString(rdr.GetValue(i), CultureInfo.InvariantCulture) ?? "";
            keys.Add(string.Join(KeySep.ToString(), parts));
        }
        return keys;
    }

    private static string? BuildKey(List<string> pkCols, Dictionary<string, int> headerToDb, List<string> row)
    {
        var parts = new string[pkCols.Count];
        for (var i = 0; i < pkCols.Count; i++)
        {
            var idx = headerToDb[pkCols[i]];
            var v = idx < row.Count ? row[idx] : "";
            parts[i] = string.IsNullOrEmpty(v) ? "" : v;
        }
        return string.Join(KeySep.ToString(), parts);
    }

    // ---- Apply --------------------------------------------------------------

    public async Task<DbImportResult> ApplyAsync(Stream xlsxStream)
    {
        var result = new DbImportResult();
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync();

        // 1) Backup the db file before any write.
        try
        {
            result.BackupFileName = BackupDatabase(conn);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DB import: backup failed; aborting before any write.");
            result.Success = false;
            result.Error = "تعذّر إنشاء نسخة احتياطية قبل الاستيراد. تم إيقاف العملية.";
            return result;
        }

        using var wb = new XLWorkbook(xlsxStream);

        // foreign_keys must be toggled outside the transaction (SQLite ignores the PRAGMA
        // inside an open transaction).
        await ExecAsync(conn, "PRAGMA foreign_keys = OFF;");

        using var tx = await conn.BeginTransactionAsync();
        try
        {
            foreach (var ws in wb.Worksheets)
            {
                var table = ws.Name;
                if (IsSkippableSheet(table)) continue;
                if (!await TableExistsAsync(conn, table)) continue;

                var cols = await GetColumnsAsync(conn, table);
                var pkCols = cols.Where(c => c.IsPk).OrderBy(c => c.PkOrder).Select(c => c.Name).ToList();
                if (pkCols.Count == 0)
                {
                    result.Warnings.Add($"تم تخطّي الجدول «{table}» (لا يوجد مفتاح أساسي).");
                    continue;
                }

                var (headers, rows) = ReadSheet(ws);
                var typeByName = cols.ToDictionary(c => c.Name, c => c.Type, StringComparer.OrdinalIgnoreCase);
                var dbColSet = cols.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var headerToDb = headers
                    .Select((h, idx) => (h, idx))
                    .Where(x => dbColSet.Contains(x.h))
                    .ToDictionary(x => x.h, x => x.idx, StringComparer.OrdinalIgnoreCase);

                if (pkCols.Any(pk => !headerToDb.ContainsKey(pk)))
                {
                    result.Warnings.Add($"تم تخطّي الجدول «{table}» (أعمدة المفتاح الأساسي ناقصة في الملف).");
                    continue;
                }

                // Columns we will actually write = DB columns present in the Excel header.
                var writeCols = cols.Where(c => headerToDb.ContainsKey(c.Name)).Select(c => c.Name).ToList();

                var (ins, upd, del) = await ApplySheetAsync(conn, tx, table, pkCols, writeCols, typeByName, headerToDb, rows);
                result.TotalInserts += ins;
                result.TotalUpdates += upd;
                result.TotalDeletes += del;
                _log.LogInformation("DB import: table {Table} +{Ins} ~{Upd} -{Del}", table, ins, upd, del);

                // Phase 20.29 — normalize DepartmentRoster emails after any import.
                // Users typically only fill the visible "Email" column in Excel; we
                // derive "EmailNormalized" (lower-cased, trimmed) so that the
                // /Journey/Access lookup (which queries EmailNormalized) keeps
                // working without forcing the admin to maintain a second column.
                if (string.Equals(table, "DepartmentRoster", StringComparison.OrdinalIgnoreCase))
                {
                    using var normCmd = conn.CreateCommand();
                    normCmd.Transaction = tx;
                    normCmd.CommandText =
                        "UPDATE DepartmentRoster " +
                        "SET EmailNormalized = CASE " +
                        "   WHEN Email IS NULL OR TRIM(Email) = '' THEN NULL " +
                        "   ELSE LOWER(TRIM(Email)) END;";
                    var normRows = await normCmd.ExecuteNonQueryAsync();
                    _log.LogInformation("DB import: normalized {Rows} DepartmentRoster emails", normRows);
                }
            }

            await tx.CommitAsync();
            result.Success = true;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _log.LogError(ex, "DB import failed; rolled back.");
            result.Success = false;
            result.Error = ex.Message;
        }
        finally
        {
            await ExecAsync(conn, "PRAGMA foreign_keys = ON;");
        }

        return result;
    }

    private async Task<(int Inserts, int Updates, int Deletes)> ApplySheetAsync(
        DbConnection conn, DbTransaction tx, string table,
        List<string> pkCols, List<string> writeCols, Dictionary<string, string> typeByName,
        Dictionary<string, int> headerToDb, List<List<string>> rows)
    {
        var dbKeys = await LoadDbKeysAsync(conn, table, pkCols);
        var excelKeys = new HashSet<string>();
        int inserts = 0, updates = 0, deletes = 0;

        var nonPkCols = writeCols.Where(c => !pkCols.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();

        // Phase 20.25.2 — NEVER touch the admin Identity row from an Excel
        // import. Excel mangles long PasswordHash strings (cuts >255 chars,
        // strips trailing '+'/'='/'/' base64 padding, or normalises stamps to
        // scientific notation), which has locked admins out of production
        // after merge+reimport workflows. Protecting the row keeps the seed
        // admin login working regardless of what the user does in Excel.
        const int AdminUserId = 1;
        bool isUsersTable = string.Equals(table, "AspNetUsers", StringComparison.OrdinalIgnoreCase);
        bool isAdminRelatedTable =
            string.Equals(table, "AspNetUserRoles", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(table, "AspNetUserTokens", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(table, "AspNetUserClaims", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(table, "AspNetUserLogins", StringComparison.OrdinalIgnoreCase);
        int? userNameColIndex = isUsersTable && headerToDb.TryGetValue("UserName", out var unIdx) ? unIdx : null;
        int? userIdColIndex = isAdminRelatedTable && headerToDb.TryGetValue("UserId", out var uidIdx) ? uidIdx : null;
        bool RowIsProtectedAdmin(List<string> row)
        {
            if (isUsersTable && userNameColIndex != null
                && userNameColIndex.Value >= 0 && userNameColIndex.Value < row.Count)
            {
                var uname = (row[userNameColIndex.Value] ?? string.Empty).Trim();
                if (string.Equals(uname, "admin@gac.gov.sa", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uname, "admin", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            if (isAdminRelatedTable && userIdColIndex != null
                && userIdColIndex.Value >= 0 && userIdColIndex.Value < row.Count)
            {
                if (int.TryParse(row[userIdColIndex.Value]?.Trim(), out var uid) && uid == AdminUserId)
                    return true;
            }
            return false;
        }
        bool DbKeyIsProtectedAdmin(string key)
        {
            if (isUsersTable && key == AdminUserId.ToString()) return true;
            if (isAdminRelatedTable)
            {
                var first = key.Split(KeySep)[0];
                if (first == AdminUserId.ToString()) return true;
            }
            return false;
        }

        foreach (var row in rows)
        {
            var key = BuildKey(pkCols, headerToDb, row);
            if (key == null) continue;
            if (!excelKeys.Add(key)) continue; // ignore duplicate PK rows in Excel

            // Protect admin: skip any Excel row that targets the admin user
            // (whether by row identity or PK match) — also protect his role
            // and token rows so a re-import can't strip admin privileges.
            if ((isUsersTable || isAdminRelatedTable) && (RowIsProtectedAdmin(row) || DbKeyIsProtectedAdmin(key)))
                continue;

            var exists = dbKeys.Contains(key);
            if (exists)
            {
                // UPDATE only when there are non-PK columns to set.
                if (nonPkCols.Count == 0) continue;
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                var sets = nonPkCols.Select((c, i) => $"{QuoteId(c)}=$s{i}");
                var wheres = pkCols.Select((c, i) => $"{QuoteId(c)}=$k{i}");
                cmd.CommandText = $"UPDATE {QuoteId(table)} SET {string.Join(",", sets)} WHERE {string.Join(" AND ", wheres)};";
                for (var i = 0; i < nonPkCols.Count; i++)
                    AddParam(cmd, $"$s{i}", Coerce(GetCell(row, headerToDb, nonPkCols[i]), typeByName[nonPkCols[i]]));
                for (var i = 0; i < pkCols.Count; i++)
                    AddParam(cmd, $"$k{i}", Coerce(GetCell(row, headerToDb, pkCols[i]), typeByName[pkCols[i]]));
                await cmd.ExecuteNonQueryAsync();
                updates++;
            }
            else
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                var colList = string.Join(",", writeCols.Select(QuoteId));
                var valList = string.Join(",", writeCols.Select((_, i) => $"$v{i}"));
                cmd.CommandText = $"INSERT INTO {QuoteId(table)} ({colList}) VALUES ({valList});";
                for (var i = 0; i < writeCols.Count; i++)
                    AddParam(cmd, $"$v{i}", Coerce(GetCell(row, headerToDb, writeCols[i]), typeByName[writeCols[i]]));
                await cmd.ExecuteNonQueryAsync();
                inserts++;
            }
        }

        // DELETE rows in DB not present in Excel (full mirror).
        foreach (var key in dbKeys)
        {
            if (excelKeys.Contains(key)) continue;
            // Never delete the seed admin row or its role/token/claim/login
            // rows even if they are missing from Excel.
            if ((isUsersTable || isAdminRelatedTable) && DbKeyIsProtectedAdmin(key)) continue;
            var parts = key.Split(KeySep);
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            var wheres = pkCols.Select((c, i) => $"{QuoteId(c)}=$k{i}");
            cmd.CommandText = $"DELETE FROM {QuoteId(table)} WHERE {string.Join(" AND ", wheres)};";
            for (var i = 0; i < pkCols.Count; i++)
                AddParam(cmd, $"$k{i}", Coerce(parts[i], typeByName[pkCols[i]]));
            await cmd.ExecuteNonQueryAsync();
            deletes++;
        }

        return (inserts, updates, deletes);
    }

    private static string GetCell(List<string> row, Dictionary<string, int> headerToDb, string col)
    {
        var idx = headerToDb[col];
        return idx < row.Count ? row[idx] : "";
    }

    // ---- Backup -------------------------------------------------------------

    private string BackupDatabase(DbConnection conn)
    {
        var dbPath = conn.DataSource;
        if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
            throw new FileNotFoundException("SQLite database file not found.", dbPath);

        // Flush the WAL into the main db file so the copy is a complete snapshot.
        ExecAsync(conn, "PRAGMA wal_checkpoint(TRUNCATE);").GetAwaiter().GetResult();

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var backupPath = $"{dbPath}.backup-{stamp}";
        File.Copy(dbPath, backupPath, overwrite: true);

        // Prune to the 10 most recent backups.
        var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(dir))
        {
            var prefix = Path.GetFileName(dbPath) + ".backup-";
            var backups = Directory.GetFiles(dir, prefix + "*")
                .OrderByDescending(f => f, StringComparer.Ordinal)
                .ToList();
            foreach (var old in backups.Skip(10))
            {
                try { File.Delete(old); } catch (Exception ex) { _log.LogWarning(ex, "Could not prune backup {File}", old); }
            }
        }
        return Path.GetFileName(backupPath);
    }

    // ---- Helpers ------------------------------------------------------------

    // Coerce an Excel string cell to a CLR value the SQLite parameter binder will store
    // with the column's affinity. Empty string → NULL.
    private static object Coerce(string raw, string sqliteType)
    {
        if (string.IsNullOrEmpty(raw)) return DBNull.Value;

        var t = (sqliteType ?? "").ToUpperInvariant();

        if (t.Contains("INT"))
        {
            // Booleans exported as True/False land in INTEGER columns (e.g. IsActive).
            if (bool.TryParse(raw, out var b)) return b ? 1L : 0L;
            if (long.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var l)) return l;
            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var dl)) return (long)dl;
            return raw; // leave as text; SQLite is dynamically typed
        }
        if (t.Contains("REAL") || t.Contains("FLOA") || t.Contains("DOUB"))
        {
            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
            return raw;
        }
        if (t.Contains("NUMERIC") || t.Contains("DEC"))
        {
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec)) return dec;
            if (bool.TryParse(raw, out var nb)) return nb ? 1L : 0L;
            return raw;
        }
        // TEXT / BLOB / everything else: store as-is (GUIDs, ISO datetimes, JSON, etc.).
        return raw;
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    private static async Task ExecAsync(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    // Quote a SQLite identifier (double-quotes, escape embedded quotes).
    private static string QuoteId(string id) => "\"" + id.Replace("\"", "\"\"") + "\"";
}
