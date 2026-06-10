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
}
