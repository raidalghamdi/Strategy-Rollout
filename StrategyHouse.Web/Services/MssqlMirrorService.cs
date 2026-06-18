using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 19.5 — pushes the external MSSQL (Option A) strategy warehouse into the
// local SQLite mirror tables so the app has a resilient offline copy. Invoked by
// the admin "push" button. Read-only against MSSQL; full refresh of the mirror
// (delete-all + insert-all) inside a single SQLite transaction so a failure rolls
// back cleanly and never leaves the mirror half-populated.

public class MirrorPushResult
{
    public bool Success { get; set; }
    public int RecordCount { get; set; }
    public int SkippedCount { get; set; }
    public double DurationSeconds { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface IMssqlMirrorService
{
    Task<MirrorPushResult> PushAllAsync(CancellationToken ct = default);
    Task<MirrorMetadata?> GetMetadataAsync(CancellationToken ct = default);
}

public class MssqlMirrorService : IMssqlMirrorService
{
    private readonly ApplicationDbContext _db;
    private readonly ExternalDbContext? _external;
    private readonly ILogger<MssqlMirrorService> _log;

    public MssqlMirrorService(
        ApplicationDbContext db,
        ILogger<MssqlMirrorService> log,
        ExternalDbContext? external = null)
    {
        _db = db;
        _log = log;
        _external = external;
    }

    public Task<MirrorMetadata?> GetMetadataAsync(CancellationToken ct = default)
        => _db.MirrorMetadata.AsNoTracking().OrderByDescending(m => m.Id).FirstOrDefaultAsync(ct);

    public async Task<MirrorPushResult> PushAllAsync(CancellationToken ct = default)
    {
        var startedAt = DateTime.UtcNow;
        var meta = await _db.MirrorMetadata.OrderByDescending(m => m.Id).FirstOrDefaultAsync(ct);
        if (meta == null)
        {
            meta = new MirrorMetadata();
            _db.MirrorMetadata.Add(meta);
        }
        meta.Status = "InProgress";
        meta.ErrorMessage = null;
        await _db.SaveChangesAsync(ct);

        if (_external == null)
        {
            var msg = "قاعدة البيانات الخارجية غير مفعّلة — فعّل UseExternalDb ووفّر سلسلة اتصال Microsoft SQL.";
            await FinishAsync(meta, false, 0, startedAt, msg, ct);
            return new MirrorPushResult { Success = false, ErrorMessage = msg, DurationSeconds = 0 };
        }

        try
        {
            // Read everything from MSSQL first (read-only) so a connection failure
            // aborts before we touch the local mirror.
            var pillars = await _external.Pillars.AsNoTracking().ToListAsync(ct);
            var objectives = await _external.Objectives.AsNoTracking().ToListAsync(ct);
            var kpis = await _external.Kpis.AsNoTracking().ToListAsync(ct);
            var initiatives = await _external.Initiatives.AsNoTracking().ToListAsync(ct);
            var projects = await _external.Projects.AsNoTracking().ToListAsync(ct);

            // Phase 19.21 (Fix 1) — every entity type is now projected defensively:
            // one malformed source row (unexpected null/overflow/cast) is logged,
            // counted, and skipped instead of aborting the whole sync.
            var skipped = 0;

            // Phase 19.8 — guard against wiping a good mirror with an empty source.
            // If MSSQL is reachable but has no pillars, abort BEFORE touching the
            // mirror so the previous offline copy is preserved.
            if (pillars.Count == 0)
            {
                const string emptyMsg = "تم الاتصال بقاعدة Microsoft SQL لكن لم يتم العثور على أي مرتكزات (Pillars). تم إلغاء عملية الدفع وحفظ النسخة السابقة. تحقق من البيانات في الخادم الخارجي قبل المحاولة مرة أخرى.";
                meta.Status = "Aborted_Empty";
                meta.ErrorMessage = emptyMsg;
                meta.LastPushAt = DateTime.UtcNow;
                meta.DurationSeconds = Math.Round((DateTime.UtcNow - startedAt).TotalSeconds, 1);
                await _db.SaveChangesAsync(ct);
                _log.LogWarning("Mirror push aborted: external MSSQL returned 0 pillars; previous mirror preserved.");
                return new MirrorPushResult
                {
                    Success = false,
                    ErrorMessage = emptyMsg,
                    DurationSeconds = meta.DurationSeconds,
                };
            }

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                _db.MirrorPillars.RemoveRange(_db.MirrorPillars);
                _db.MirrorObjectives.RemoveRange(_db.MirrorObjectives);
                _db.MirrorKpis.RemoveRange(_db.MirrorKpis);
                _db.MirrorInitiatives.RemoveRange(_db.MirrorInitiatives);
                _db.MirrorProjects.RemoveRange(_db.MirrorProjects);
                await _db.SaveChangesAsync(ct);

                foreach (var p in pillars)
                {
                    try
                    {
                        _db.MirrorPillars.Add(new MirrorPillar
                        {
                            PlrCode = p.PlrCode,
                            PillarName = p.PillarName,
                            Budget = p.Budget,
                            Liquidity = p.Liquidity,
                            StartDates = p.StartDates,
                            EndDates = p.EndDates,
                            PlrPeriods = p.PlrPeriods,
                        });
                    }
                    catch (Exception rowEx)
                    {
                        skipped++;
                        _log.LogWarning(rowEx, "Skipped malformed Pillar row {Code} during mirror push.", p.PlrCode);
                    }
                }
                foreach (var o in objectives)
                {
                    try
                    {
                        _db.MirrorObjectives.Add(new MirrorObjective
                        {
                            ObjectiveCode = o.ObjectiveCode,
                            ObjectiveName = o.ObjectiveName,
                            PlrCode = o.PlrCode,
                            Budget = o.Budget,
                            Liquidity = o.Liquidity,
                            StartDates = o.StartDates,
                            EndDates = o.EndDates,
                            ObjPeriod = o.ObjPeriod,
                        });
                    }
                    catch (Exception rowEx)
                    {
                        skipped++;
                        _log.LogWarning(rowEx, "Skipped malformed Objective row {Code} during mirror push.", o.ObjectiveCode);
                    }
                }
                foreach (var k in kpis)
                {
                    try
                    {
                        _db.MirrorKpis.Add(new MirrorKpi
                        {
                            KpiCode = k.KpiCode,
                            KpiName = k.KpiName,
                            ActivationStatus = k.ActivationStatus,
                            KpiType = k.KpiType,
                            ObjectiveCode = k.ObjectiveCode,
                            PlrCode = k.PlrCode,
                            Division = k.Division,
                            Frequency = k.Frequency,
                            // Source exposes the four real MSSQL columns (Unit,
                            // Direction, Minimum, Maximum). The mirror keeps a single
                            // combined Unit/Direction string and one Minimum-Maximum
                            // decimal, so fold the split source columns back together.
                            UnitDirection = CombineText(k.Unit, k.Direction),
                            // Phase 19.21 (Fix 1) — Index_Weight is nvarchar(5) again;
                            // the mirror column is also a string, so copy it straight.
                            IndexWeight = k.IndexWeight,
                            MinimumMaximum = k.Minimum ?? k.Maximum,
                            Target2025 = k.Target2025,
                            Target2026 = k.Target2026,
                            Target2027 = k.Target2027,
                            Target2028 = k.Target2028,
                            Target2029 = k.Target2029,
                            Target2030 = k.Target2030,
                            AutomationStatus = k.AutomationStatus,
                        });
                    }
                    catch (Exception rowEx)
                    {
                        skipped++;
                        _log.LogWarning(rowEx, "Skipped malformed KPI row {KpiCode} during mirror push.", k.KpiCode);
                    }
                }
                foreach (var i in initiatives)
                {
                    try
                    {
                        _db.MirrorInitiatives.Add(new MirrorInitiative
                        {
                            InitiativeCode = i.InitiativeCode,
                            InitiativeName = i.InitiativeName,
                            ObjectiveCode = i.ObjectiveCode,
                            ObjectiveName = i.ObjectiveName,
                            Owners = i.Owners,
                            Budget = i.Budget,
                            Liquidity = i.Liquidity,
                            StartDates = i.StartDates,
                            EndDates = i.EndDates,
                        });
                    }
                    catch (Exception rowEx)
                    {
                        skipped++;
                        _log.LogWarning(rowEx, "Skipped malformed Initiative row {Code} during mirror push.", i.InitiativeCode);
                    }
                }
                foreach (var p in projects)
                {
                    try
                    {
                        _db.MirrorProjects.Add(new MirrorProject
                        {
                            ProjectCode = p.ProjectCode,
                            ProjectName = p.ProjectName,
                            InitiativeCode = p.InitiativeCode,
                            PlrCode = p.PlrCode,
                            ProjectType = p.ProjectType,
                            ProjectStatus = p.ProjectStatus,
                            // Phase 19.21 (Fix 1) — source now has separate Budget and
                            // Liquidity columns; the mirror keeps a single combined
                            // money column, so fold them (prefer Budget, fall back to
                            // Liquidity) to preserve the mirror schema (no migration).
                            BudgetLiquidity = p.Budget ?? p.Liquidity,
                            Liquidity2025 = p.Liquidity2025,
                            Liquidity2026 = p.Liquidity2026,
                            Liquidity2027 = p.Liquidity2027,
                            Liquidity2028 = p.Liquidity2028,
                            Liquidity2029 = p.Liquidity2029,
                            Liquidity2030 = p.Liquidity2030,
                            Liquidity2031 = p.Liquidity2031,
                            GacBudget = p.GacBudget,
                            ProjectSponsor = p.ProjectSponsor,
                            ProjectManager = p.ProjectManager,
                            Division = p.Division,
                            ProjectPhase = p.ProjectPhase,
                        });
                    }
                    catch (Exception rowEx)
                    {
                        skipped++;
                        _log.LogWarning(rowEx, "Skipped malformed Project row {Code} during mirror push.", p.ProjectCode);
                    }
                }
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            // Mirror count reflects rows actually written (all skipped rows excluded).
            var sourceTotal = pillars.Count + objectives.Count + kpis.Count + initiatives.Count + projects.Count;
            var total = sourceTotal - skipped;
            // Phase 19.21 (Fix 1) — surface skipped rows in the saved status note so the
            // admin UI can show "تم دفع N سجل، تخطّى K سجل" without a separate field.
            var note = skipped > 0
                ? $"تم دفع {total} سجل، تخطّى {skipped} سجل."
                : $"تم دفع {total} سجل.";
            await FinishAsync(meta, true, total, startedAt, note, ct);
            _log.LogInformation("Mirror push succeeded: {Count} records ({Skipped} rows skipped) in {Seconds:F1}s.", total, skipped, meta.DurationSeconds);
            return new MirrorPushResult
            {
                Success = true,
                RecordCount = total,
                SkippedCount = skipped,
                DurationSeconds = meta.DurationSeconds,
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Mirror push failed.");
            await FinishAsync(meta, false, 0, startedAt, ex.Message, ct);
            return new MirrorPushResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                DurationSeconds = (DateTime.UtcNow - startedAt).TotalSeconds,
            };
        }
    }

    // Joins two optional source columns into one mirror string, skipping blanks so
    // a missing Unit or Direction doesn't leave stray separators (" / ").
    private static string? CombineText(string? a, string? b)
    {
        var parts = new[] { a, b }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim());
        var joined = string.Join(" / ", parts);
        return joined.Length == 0 ? null : joined;
    }

    private async Task FinishAsync(MirrorMetadata meta, bool success, int count, DateTime startedAt, string? error, CancellationToken ct)
    {
        meta.Status = success ? "Success" : "Failed";
        meta.RecordCount = count;
        meta.ErrorMessage = error;
        meta.LastPushAt = DateTime.UtcNow;
        meta.DurationSeconds = Math.Round((DateTime.UtcNow - startedAt).TotalSeconds, 1);
        await _db.SaveChangesAsync(ct);
    }
}
