using System.Text.Json;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 20 — writes one immutable JourneyAuditLog row per privileged journey action.
public class AuditLogService
{
    private readonly ApplicationDbContext _db;

    public AuditLogService(ApplicationDbContext db) { _db = db; }

    public async Task LogAsync(
        string actor,
        string actionType,
        string targetType,
        string? targetId,
        object? details = null)
    {
        _db.JourneyAuditLogs.Add(new JourneyAuditLog
        {
            Actor = actor,
            ActionType = actionType,
            TargetType = targetType,
            TargetId = targetId,
            DetailsJson = details == null ? null : JsonSerializer.Serialize(details),
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }
}
