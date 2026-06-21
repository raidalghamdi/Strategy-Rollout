using Microsoft.AspNetCore.Identity;
using StrategyHouse.Domain.Enums;

namespace StrategyHouse.Domain.Entities;

/// <summary>
/// Strategy office user. Three roles: Admin, Facilitator, Viewer.
/// </summary>
public class AppUser : IdentityUser<int>
{
    public string FullNameAr { get; set; } = string.Empty;
    public UserRole AppRole { get; set; } = UserRole.Facilitator;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Phase 20 — default sector scope for journey users (e.g. SECTOR:CORP_SUPPORT,
    // SECTOR:ECONOMIC, SECTOR:LEGAL, GLOBAL, TEST). Null for platform admins/legacy users.
    public string? JourneyScopeKey { get; set; }

    // Phase 20 — stamped on each successful password sign-in; shown on the profile page.
    public DateTime? LastLoginAt { get; set; }
}
