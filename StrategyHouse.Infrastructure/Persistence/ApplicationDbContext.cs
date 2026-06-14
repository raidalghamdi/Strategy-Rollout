using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;

namespace StrategyHouse.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<AppUser, IdentityRole<int>, int>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    // Strict strategy schema — Pillars → Objectives → Kpis / Initiatives → Projects (+ Departments lookup)
    public DbSet<Pillar> Pillars => Set<Pillar>();
    public DbSet<Objective> Objectives => Set<Objective>();
    public DbSet<Kpi> Kpis => Set<Kpi>();
    public DbSet<Initiative> Initiatives => Set<Initiative>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Department> Departments => Set<Department>();

    // Phase 1 — Department Strategy Journey
    public DbSet<DepartmentAccessCode> DepartmentAccessCodes => Set<DepartmentAccessCode>();
    public DbSet<StrategySession> StrategySessions => Set<StrategySession>();
    public DbSet<SessionMember> SessionMembers => Set<SessionMember>();
    public DbSet<ContributionPledge> ContributionPledges => Set<ContributionPledge>();
    public DbSet<DepartmentStrategyMap> DepartmentStrategyMaps => Set<DepartmentStrategyMap>();
    public DbSet<MapInkAsset> MapInkAssets => Set<MapInkAsset>();
    public DbSet<ModerationAuditLog> ModerationAuditLogs => Set<ModerationAuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Data annotations carry FK/column config. Relax delete behaviour on the
        // denormalised PLR_Code shortcuts so they don't clash with the primary chain.
        b.Entity<Kpi>()
            .HasOne(k => k.Pillar)
            .WithMany(p => p.Kpis)
            .HasForeignKey(k => k.PlrCode)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Kpi>()
            .HasOne(k => k.Objective)
            .WithMany(o => o.Kpis)
            .HasForeignKey(k => k.ObjectiveCode)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Project>()
            .HasOne(p => p.Pillar)
            .WithMany(pl => pl.Projects)
            .HasForeignKey(p => p.PlrCode)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Project>()
            .HasOne(p => p.Initiative)
            .WithMany(i => i.Projects)
            .HasForeignKey(p => p.InitiativeCode)
            .OnDelete(DeleteBehavior.Restrict);

        // Phase 1 journey FKs — no cascade deletes, keep all history.
        b.Entity<SessionMember>()
            .HasOne(m => m.Session)
            .WithMany(s => s.Members)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<ContributionPledge>()
            .HasOne(p => p.Session)
            .WithMany()
            .HasForeignKey(p => p.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<DepartmentStrategyMap>()
            .HasOne(m => m.Session)
            .WithMany()
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<MapInkAsset>()
            .HasOne(a => a.Map)
            .WithMany()
            .HasForeignKey(a => a.MapId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
