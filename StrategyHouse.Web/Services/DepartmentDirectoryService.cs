using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 17 — single read path for the department directory.
//
// Source of truth (Option A): the external MSSQL "KPIs" table's Division column.
// Departments are the DISTINCT, non-empty Division values. There is no separate
// Departments table in Option A, so the directory is derived at read time.
//
// Default (UseExternalDb=false): departments come from the local SQLite
// ApplicationDbContext, exactly as before (dev fallback). When UseExternalDb=true
// and an ExternalDbContext is registered, departments are read from KPIs.Division
// and projected onto the app's Department shape so callers don't change. Results
// are cached in memory for a short window to reduce pressure on the warehouse.
public class DepartmentDirectoryService
{
    private const string CacheKey = "ext.departments.v1";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ApplicationDbContext _local;
    private readonly ExternalDbContext? _external;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DepartmentDirectoryService> _log;
    private readonly bool _flagOn;
    private readonly bool _useExternal;

    public DepartmentDirectoryService(
        ApplicationDbContext local,
        IConfiguration config,
        IMemoryCache cache,
        ILogger<DepartmentDirectoryService> log,
        ExternalDbContext? external = null)
    {
        _local = local;
        _external = external;
        _cache = cache;
        _log = log;
        _flagOn = config.GetValue<bool>("Features:UseExternalDb");
        _useExternal = _flagOn && external != null;
    }

    public bool UsingExternal => _useExternal;

    public async Task<List<Department>> GetDepartmentsAsync()
    {
        if (_useExternal && _external != null)
        {
            if (_cache.TryGetValue(CacheKey, out List<Department>? cached) && cached != null)
                return cached;

            try
            {
                var divisions = await _external.Kpis
                    .Where(k => k.Division != null && k.Division != "")
                    .Select(k => k.Division!)
                    .Distinct()
                    .OrderBy(d => d)
                    .ToListAsync();

                var list = divisions.Select(d => new Department
                {
                    DeptCode = d,
                    NameAr = d,
                    NameEn = null,
                    Level = 2,
                    IsActive = true,
                }).ToList();

                _cache.Set(CacheKey, list, CacheTtl);
                return list;
            }
            catch (Exception ex)
            {
                // Don't break callers if the warehouse is unreachable; fall back to local.
                _log.LogWarning(ex, "External KPIs.Division read failed; falling back to local departments.");
            }
        }
        else if (_flagOn && _external == null)
        {
            _log.LogWarning("UseExternalDb is true but ExternalDbContext is not registered (empty connection string); using local departments.");
        }

        return await _local.Departments.OrderBy(d => d.DeptCode).ToListAsync();
    }
}
