using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Phase 20 — resolves which department codes a journey user may see, based on the
// JourneyScopeKey stored on their AppUser row.
//   SECTOR:CORP_SUPPORT → Departments where Parent_Sector = 'قطاع الدعم المؤسسي'
//   SECTOR:ECONOMIC      → 'قطاع الشؤون الاقتصادية'
//   SECTOR:LEGAL         → 'قطاع الشؤون القانونية'
//   GLOBAL / TEST / Admin role → all departments (no filter)
//   anything else        → only the user's own department, else empty.
public interface IJourneyScopeService
{
    Task<List<string>> GetVisibleDeptCodesAsync(ClaimsPrincipal user);
    Task<string?> GetSectorKeyAsync(ClaimsPrincipal user);
    Task<bool> IsGlobalAsync(ClaimsPrincipal user);
}

public class JourneyScopeService : IJourneyScopeService
{
    // Sector key → the Parent_Sector value as stored in the Departments table.
    public static readonly IReadOnlyDictionary<string, string> SectorByKey =
        new Dictionary<string, string>
        {
            ["SECTOR:CORP_SUPPORT"] = "قطاع الدعم المؤسسي",
            ["SECTOR:ECONOMIC"] = "قطاع الشؤون الاقتصادية",
            ["SECTOR:LEGAL"] = "قطاع الشؤون القانونية",
        };

    private readonly ApplicationDbContext _db;
    private readonly UserManager<AppUser> _users;

    public JourneyScopeService(ApplicationDbContext db, UserManager<AppUser> users)
    {
        _db = db;
        _users = users;
    }

    public async Task<string?> GetSectorKeyAsync(ClaimsPrincipal user)
    {
        var appUser = await _users.GetUserAsync(user);
        return appUser?.JourneyScopeKey;
    }

    public async Task<bool> IsGlobalAsync(ClaimsPrincipal user)
    {
        if (user.IsInRole("Admin")) return true;
        var key = await GetSectorKeyAsync(user);
        return key is "GLOBAL" or "TEST";
    }

    public async Task<List<string>> GetVisibleDeptCodesAsync(ClaimsPrincipal user)
    {
        // Platform admins and global/test journey users see every department.
        if (await IsGlobalAsync(user))
            return await _db.Departments.OrderBy(d => d.DeptCode).Select(d => d.DeptCode).ToListAsync();

        var key = await GetSectorKeyAsync(user);

        if (!string.IsNullOrEmpty(key) && SectorByKey.TryGetValue(key, out var sector))
        {
            return await _db.Departments
                .Where(d => d.ParentSector == sector)
                .OrderBy(d => d.DeptCode)
                .Select(d => d.DeptCode)
                .ToListAsync();
        }

        // Fallback: a journey user with no sector key sees only their own department,
        // if one is recorded on their profile (FullNameAr is not a dept; we have no dept
        // column on AppUser, so default to empty — nothing leaks).
        return new List<string>();
    }
}
