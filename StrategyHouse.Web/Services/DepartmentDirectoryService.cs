using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 16 — single read path for the department directory.
//
// Default (UseExternalDb=false): departments come from the local SQLite
// ApplicationDbContext, exactly as before.
//
// When UseExternalDb=true and an ExternalDbContext is registered, departments
// are read from the external MSSQL "Departments" table (Option A schema) and
// projected onto the app's Department shape so callers don't change.
public class DepartmentDirectoryService
{
    private readonly ApplicationDbContext _local;
    private readonly ExternalDbContext? _external;
    private readonly bool _useExternal;

    public DepartmentDirectoryService(
        ApplicationDbContext local,
        IConfiguration config,
        ExternalDbContext? external = null)
    {
        _local = local;
        _external = external;
        _useExternal = config.GetValue<bool>("Features:UseExternalDb") && external != null;
    }

    public bool UsingExternal => _useExternal;

    public async Task<List<Department>> GetDepartmentsAsync()
    {
        if (_useExternal && _external != null)
        {
            var ext = await _external.Departments
                .Where(d => d.IsActive)
                .OrderBy(d => d.Code)
                .ToListAsync();
            return ext.Select(d => new Department
            {
                DeptCode = d.Code,
                NameAr = d.Name,
                NameEn = d.NameEn,
                Level = d.Level,
                IsActive = d.IsActive,
            }).ToList();
        }

        return await _local.Departments.OrderBy(d => d.DeptCode).ToListAsync();
    }
}
