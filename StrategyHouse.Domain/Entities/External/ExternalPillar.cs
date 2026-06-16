using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StrategyHouse.Domain.Entities.External;

// Phase 17 — mirrors the external MSSQL "Pillars" table (Option A schema).
// Read-only from the app's perspective. Column names below are the flattened
// snake-case names supplied by the external warehouse; Fluent API in
// ExternalDbContext binds them. PK is a string (nvarchar) per Option A.
[Table("Pillars")]
public class ExternalPillar
{
    [Key, Column("PLR_Code"), MaxLength(15)]
    public string PlrCode { get; set; } = string.Empty;

    [Column("PILLAR_Name"), MaxLength(255)]
    public string? PillarName { get; set; }

    [Column("Budget", TypeName = "decimal(18,2)")]
    public decimal? Budget { get; set; }

    [Column("Liquidity", TypeName = "decimal(18,2)")]
    public decimal? Liquidity { get; set; }

    [Column("Start_Dates")]
    public DateTime? StartDates { get; set; }

    [Column("End_Dates")]
    public DateTime? EndDates { get; set; }

    [Column("PLR_Periods"), MaxLength(10)]
    public string? PlrPeriods { get; set; }

    public ICollection<ExternalObjective> Objectives { get; set; } = new List<ExternalObjective>();
    public ICollection<ExternalKpi> Kpis { get; set; } = new List<ExternalKpi>();
    public ICollection<ExternalProject> Projects { get; set; } = new List<ExternalProject>();
}
