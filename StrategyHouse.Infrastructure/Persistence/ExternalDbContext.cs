using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace StrategyHouse.Infrastructure.Persistence;

// =====================================================================
// Phase 16 — External MSSQL database (Option A schema).
//
// This context is OPTIONAL. It is only registered in DI when the
// `UseExternalDb` feature flag is true AND a non-empty connection string
// is supplied via ConnectionStrings:ExternalMssql. When the flag is off
// (the default in dev), the app runs entirely off the local SQLite
// ApplicationDbContext exactly as before — nothing here is touched.
//
// Option A covers the organisation's Departments table. All journey,
// quiz, survey, signature and CMS tables remain local in SQLite.
// =====================================================================
public class ExternalDbContext : DbContext
{
    public ExternalDbContext(DbContextOptions<ExternalDbContext> options) : base(options) { }

    public DbSet<ExternalDepartment> Departments => Set<ExternalDepartment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<ExternalDepartment>().ToTable("Departments");
    }
}

// Mirrors the Option A "Departments" table on the external MSSQL server:
//   id (PK), code, name, level, parent_id, is_active, ...
// Read-only from the app's perspective (we only query it).
[Table("Departments")]
public class ExternalDepartment
{
    [Key, Column("id")] public int Id { get; set; }
    [Column("code"), MaxLength(15)] public string Code { get; set; } = string.Empty;
    [Column("name"), MaxLength(255)] public string? Name { get; set; }
    [Column("name_en"), MaxLength(255)] public string? NameEn { get; set; }
    [Column("level")] public int? Level { get; set; }
    [Column("parent_id")] public int? ParentId { get; set; }
    [Column("is_active")] public bool IsActive { get; set; } = true;
}
