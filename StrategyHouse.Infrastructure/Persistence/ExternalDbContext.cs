using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities.External;

namespace StrategyHouse.Infrastructure.Persistence;

// =====================================================================
// Phase 17 — External MSSQL database (Option A schema, full 5 tables).
//
// This context is OPTIONAL. It is only registered in DI when the
// `UseExternalDb` feature flag is true AND a non-empty connection string
// is supplied via ConnectionStrings:ExternalMssql. When the flag is off
// (the default in dev), the app runs entirely off the local SQLite
// ApplicationDbContext exactly as before — nothing here is touched.
//
// The app only ever QUERIES this context (read-only). No migrations are
// generated against it; the external warehouse owns the schema. Column
// names are the flattened snake-case names from the warehouse, bound via
// data annotations on the entity classes plus the FK relationships below.
//
// Tables (Option A): Pillars → Objectives → KPIs / Initiatives → Projects.
// Departments are derived at read time from KPIs.Division (DISTINCT) — there
// is no separate Departments table in Option A.
// =====================================================================
public class ExternalDbContext : DbContext
{
    public ExternalDbContext(DbContextOptions<ExternalDbContext> options) : base(options) { }

    public DbSet<ExternalPillar> Pillars => Set<ExternalPillar>();
    public DbSet<ExternalObjective> Objectives => Set<ExternalObjective>();
    public DbSet<ExternalKpi> Kpis => Set<ExternalKpi>();
    public DbSet<ExternalInitiative> Initiatives => Set<ExternalInitiative>();
    public DbSet<ExternalProject> Projects => Set<ExternalProject>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // Pillars → Objectives (PLR_Code)
        b.Entity<ExternalObjective>()
            .HasOne(o => o.Pillar)
            .WithMany(p => p.Objectives)
            .HasForeignKey(o => o.PlrCode)
            .OnDelete(DeleteBehavior.Restrict);

        // Objectives → Initiatives (Objective_Code)
        b.Entity<ExternalInitiative>()
            .HasOne(i => i.Objective)
            .WithMany(o => o.Initiatives)
            .HasForeignKey(i => i.ObjectiveCode)
            .OnDelete(DeleteBehavior.Restrict);

        // Objectives → KPIs (Objective_Code) + denormalised Pillars → KPIs (PLR_Code)
        b.Entity<ExternalKpi>()
            .HasOne(k => k.Objective)
            .WithMany(o => o.Kpis)
            .HasForeignKey(k => k.ObjectiveCode)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<ExternalKpi>()
            .HasOne(k => k.Pillar)
            .WithMany(p => p.Kpis)
            .HasForeignKey(k => k.PlrCode)
            .OnDelete(DeleteBehavior.Restrict);

        // Initiatives → Projects (Initiative_Code) + denormalised Pillars → Projects (PLR_Code)
        b.Entity<ExternalProject>()
            .HasOne(p => p.Initiative)
            .WithMany(i => i.Projects)
            .HasForeignKey(p => p.InitiativeCode)
            .OnDelete(DeleteBehavior.Restrict);
        b.Entity<ExternalProject>()
            .HasOne(p => p.Pillar)
            .WithMany(p => p.Projects)
            .HasForeignKey(p => p.PlrCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
