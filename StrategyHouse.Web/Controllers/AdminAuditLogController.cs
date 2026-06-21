using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Controllers;

// Phase 20 — read-only view of the JourneyAuditLog with filters + CSV export. Admin-only.
[Authorize(Roles = "Admin")]
[Route("Admin/AuditLog")]
public class AdminAuditLogController : Controller
{
    private const int PageSize = 50;
    private readonly ApplicationDbContext _db;

    public AdminAuditLogController(ApplicationDbContext db) { _db = db; }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? actor, string? actionType, string? targetType,
        DateTime? from, DateTime? to, int page = 1)
    {
        var q = Filtered(actor, actionType, targetType, from, to);

        var total = await q.CountAsync();
        if (page < 1) page = 1;
        var items = await q.OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .ToListAsync();

        var vm = new AuditLogViewModel
        {
            Items = items.Select(l => new AuditLogRow
            {
                CreatedAt = l.CreatedAt,
                Actor = l.Actor,
                ActionType = l.ActionType,
                TargetType = l.TargetType,
                TargetId = l.TargetId,
                DetailsJson = l.DetailsJson,
            }).ToList(),
            Actor = actor,
            ActionType = actionType,
            TargetType = targetType,
            From = from,
            To = to,
            Page = page,
            TotalPages = (int)Math.Ceiling(total / (double)PageSize),
            ActionTypeOptions = await _db.JourneyAuditLogs.Where(l => l.ActionType != null)
                .Select(l => l.ActionType!).Distinct().OrderBy(x => x).ToListAsync(),
        };
        return View(vm);
    }

    [HttpGet("Csv")]
    public async Task<IActionResult> Csv(string? actor, string? actionType, string? targetType,
        DateTime? from, DateTime? to)
    {
        var items = await Filtered(actor, actionType, targetType, from, to)
            .OrderByDescending(l => l.CreatedAt).Take(10000).ToListAsync();

        var sb = new StringBuilder();
        sb.Append('﻿'); // UTF-8 BOM for Excel
        sb.AppendLine("CreatedAt,Actor,ActionType,TargetType,TargetId,Details");
        foreach (var l in items)
        {
            string C(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
            sb.AppendLine(string.Join(",",
                C(l.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                C(l.Actor), C(l.ActionType), C(l.TargetType), C(l.TargetId), C(l.DetailsJson)));
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "journey-audit-log.csv");
    }

    private IQueryable<Domain.Entities.JourneyAuditLog> Filtered(string? actor, string? actionType,
        string? targetType, DateTime? from, DateTime? to)
    {
        var q = _db.JourneyAuditLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(actor)) q = q.Where(l => l.Actor != null && l.Actor.Contains(actor));
        if (!string.IsNullOrWhiteSpace(actionType)) q = q.Where(l => l.ActionType == actionType);
        if (!string.IsNullOrWhiteSpace(targetType)) q = q.Where(l => l.TargetType == targetType);
        if (from.HasValue) q = q.Where(l => l.CreatedAt >= from.Value);
        if (to.HasValue) q = q.Where(l => l.CreatedAt < to.Value.AddDays(1));
        return q;
    }
}

public class AuditLogRow
{
    public DateTime CreatedAt { get; set; }
    public string? Actor { get; set; }
    public string? ActionType { get; set; }
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? DetailsJson { get; set; }
}

public class AuditLogViewModel
{
    public List<AuditLogRow> Items { get; set; } = new();
    public string? Actor { get; set; }
    public string? ActionType { get; set; }
    public string? TargetType { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public List<string> ActionTypeOptions { get; set; } = new();
}
