using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 5 — one-time idempotent backfill.
// Historically signature ink was saved as ModerationStatus="Pending" and the PDF
// only renders Approved ink, so existing signed maps had no signatures in their PDF.
// This flips pending signatures to Approved, then regenerates the PDF for every
// signed map. Guarded so it does nothing on subsequent startups (idempotent: once
// every signature is Approved and PDFs reflect them, the flip count is 0 and we skip).
public static class SignatureBackfill
{
    public static async Task RunAsync(ApplicationDbContext db, StrategyMapPdfService pdf)
    {
        // 1) Flip pending signatures → Approved.
        var pending = await db.MapInkAssets
            .Where(a => a.AssetKind == "signature" && a.ModerationStatus == "Pending")
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var a in pending)
        {
            a.ModerationStatus = "Approved";
            a.ModeratedAt = now;
            a.ModeratedBy = "system:backfill-sig";
            a.IsActive = true;
            db.ModerationAuditLogs.Add(new ModerationAuditLog
            {
                TargetType = "MapInkAsset",
                TargetId = a.Id,
                Action = "Approve",
                ActorUserId = "system:backfill-sig",
                Note = "phase5 backfill: auto-approve pending signature",
            });
        }
        if (pending.Count > 0) await db.SaveChangesAsync();

        // Guard: if no signatures were flipped, the backfill already ran — skip regen.
        if (pending.Count == 0)
        {
            Console.WriteLine("[SignatureBackfill] No pending signatures; skipping (already backfilled).");
            return;
        }

        // 2) Regenerate PDFs for every signed map that has at least one signature.
        var signedMapIds = pending.Select(a => a.MapId).Distinct().ToList();
        var maps = await db.DepartmentStrategyMaps
            .Where(m => m.SignedAt != null && signedMapIds.Contains(m.Id))
            .ToListAsync();

        int regenerated = 0;
        foreach (var map in maps)
        {
            var session = await db.StrategySessions.Include(s => s.Members)
                .FirstOrDefaultAsync(s => s.Id == map.SessionId);
            var members = session?.Members.ToList() ?? new List<SessionMember>();
            var dept = await db.Departments.FindAsync(map.DeptCode) ?? new Department { DeptCode = map.DeptCode };
            var pledges = await db.ContributionPledges.Where(p => p.SessionId == map.SessionId).ToListAsync();
            var pillars = await db.Pillars.OrderBy(p => p.PlrCode).ToListAsync();
            var kpis = await db.Kpis.Where(k => k.DepartmentCode == map.DeptCode).ToListAsync();
            var projects = await db.Projects.Where(p => p.DepartmentCode == map.DeptCode).ToListAsync();
            var assets = await db.MapInkAssets.Where(a => a.MapId == map.Id).ToListAsync();
            try
            {
                map.PdfBlob = pdf.Generate(map, dept, members, pledges, pillars, kpis, projects, assets);
                await db.SaveChangesAsync();
                regenerated++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SignatureBackfill] PDF regen failed for map {map.Id}: {ex.Message}");
            }
        }

        Console.WriteLine($"[SignatureBackfill] Approved {pending.Count} pending signature(s); regenerated {regenerated} PDF(s).");
    }
}
