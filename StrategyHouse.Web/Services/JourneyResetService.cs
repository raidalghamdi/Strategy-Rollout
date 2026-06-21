using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 20 — shared stage-clearing logic used by /Admin/JourneyReset (per department)
// and /Admin/TestResults (per session). Implements the Stage→Tables map from SPEC.
//   1 الصورة الكبرى    → Phase18_OpeningReflections
//   2 البيت الاستراتيجي → (no per-stage rows — marker only)
//   3 دوري في الاستراتيجية → Phase18_RoleContributions, TeamValueSelections, ContributionPledges
//   4 الرحلة نحو الرؤية → DepartmentStrategyMaps, MapInkAssets
//   5 (final)          → StrategySessions.SignedAt=null, Status='InProgress', CompletedAt=null
// No cascade deletes — everything is removed explicitly so orphan-free behaviour is exact.
public class JourneyResetService
{
    private readonly ApplicationDbContext _db;

    public JourneyResetService(ApplicationDbContext db) { _db = db; }

    // Clears the chosen stages for every session belonging to a department.
    // Returns the affected session ids. When deleteSessions is true (كل المراحل)
    // the StrategySessions rows themselves are removed too.
    public async Task<List<Guid>> ResetDepartmentAsync(string deptCode, IReadOnlyCollection<int> stages, bool deleteSessions)
    {
        var sessionIds = await _db.StrategySessions
            .Where(s => s.DeptCode == deptCode)
            .Select(s => s.Id)
            .ToListAsync();

        await ResetSessionsAsync(sessionIds, stages, deleteSessions);
        return sessionIds;
    }

    // Clears the chosen stages for a single session (used by TestResults selective delete).
    public async Task ResetSessionAsync(Guid sessionId, IReadOnlyCollection<int> stages, bool deleteSession)
        => await ResetSessionsAsync(new List<Guid> { sessionId }, stages, deleteSession);

    private async Task ResetSessionsAsync(List<Guid> sessionIds, IReadOnlyCollection<int> stages, bool deleteSessions)
    {
        if (sessionIds.Count == 0) return;

        if (stages.Contains(1))
        {
            var rows = await _db.OpeningReflections.Where(r => r.SessionId != null && sessionIds.Contains(r.SessionId.Value)).ToListAsync();
            _db.OpeningReflections.RemoveRange(rows);
        }
        // Stage 2 — no per-stage capture rows.
        if (stages.Contains(3))
        {
            _db.RoleContributions.RemoveRange(await _db.RoleContributions.Where(r => r.SessionId != null && sessionIds.Contains(r.SessionId.Value)).ToListAsync());
            _db.TeamValueSelections.RemoveRange(await _db.TeamValueSelections.Where(r => r.SessionId != null && sessionIds.Contains(r.SessionId.Value)).ToListAsync());
            _db.ContributionPledges.RemoveRange(await _db.ContributionPledges.Where(r => sessionIds.Contains(r.SessionId)).ToListAsync());
        }
        if (stages.Contains(4))
        {
            var maps = await _db.DepartmentStrategyMaps.Where(m => sessionIds.Contains(m.SessionId)).ToListAsync();
            var mapIds = maps.Select(m => m.Id).ToList();
            _db.MapInkAssets.RemoveRange(await _db.MapInkAssets.Where(a => mapIds.Contains(a.MapId)).ToListAsync());
            _db.DepartmentStrategyMaps.RemoveRange(maps);
        }
        if (stages.Contains(5) || deleteSessions)
        {
            var sessions = await _db.StrategySessions.Where(s => sessionIds.Contains(s.Id)).ToListAsync();
            foreach (var s in sessions)
            {
                s.SignedAt = null;
                s.Status = "InProgress";
                s.CompletedAt = null;
            }
        }

        if (deleteSessions)
        {
            // Remove members and the sessions themselves (AccessCodes survive on purpose).
            _db.SessionMembers.RemoveRange(await _db.SessionMembers.Where(m => sessionIds.Contains(m.SessionId)).ToListAsync());
            _db.StrategySessions.RemoveRange(await _db.StrategySessions.Where(s => sessionIds.Contains(s.Id)).ToListAsync());
        }

        await _db.SaveChangesAsync();
    }

    // Full delete of a session and all of its child rows across every stage table.
    public async Task DeleteSessionFullAsync(Guid sessionId)
        => await ResetSessionsAsync(new List<Guid> { sessionId }, new[] { 1, 3, 4 }, deleteSessions: true);
}
