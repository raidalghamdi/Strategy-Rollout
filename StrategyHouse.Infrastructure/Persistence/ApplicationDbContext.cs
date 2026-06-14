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
    }
}
