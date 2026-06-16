using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StrategyHouse.Domain.Entities.External;

// Phase 17 — mirrors the external MSSQL "Objectives" table (Option A schema).
[Table("Objectives")]
public class ExternalObjective
{
    [Key, Column("Objective_Code"), MaxLength(15)]
    public string ObjectiveCode { get; set; } = string.Empty;

    [Column("Objective_Name"), MaxLength(255)]
    public string? ObjectiveName { get; set; }

    [Column("PLR_Code"), MaxLength(15)]
    public string? PlrCode { get; set; }

    [Column("Budget", TypeName = "decimal(18,2)")]
    public decimal? Budget { get; set; }

    [Column("Liquidity", TypeName = "decimal(18,2)")]
    public decimal? Liquidity { get; set; }

    [Column("Start_Dates")]
    public DateTime? StartDates { get; set; }

    [Column("End_Dates")]
    public DateTime? EndDates { get; set; }

    [Column("Obj_Period"), MaxLength(10)]
    public string? ObjPeriod { get; set; }

    public ExternalPillar? Pillar { get; set; }
    public ICollection<ExternalKpi> Kpis { get; set; } = new List<ExternalKpi>();
    public ICollection<ExternalInitiative> Initiatives { get; set; } = new List<ExternalInitiative>();
}
