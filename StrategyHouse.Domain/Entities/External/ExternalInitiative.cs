using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StrategyHouse.Domain.Entities.External;

// Phase 17 — mirrors the external MSSQL "Initiatives" table (Option A schema).
[Table("Initiatives")]
public class ExternalInitiative
{
    [Key, Column("Initiative_Code"), MaxLength(15)]
    public string InitiativeCode { get; set; } = string.Empty;

    [Column("Initiative_Name"), MaxLength(255)]
    public string? InitiativeName { get; set; }

    [Column("Objective_Code"), MaxLength(15)]
    public string? ObjectiveCode { get; set; }

    [Column("Objective_Name"), MaxLength(255)]
    public string? ObjectiveName { get; set; }

    [Column("Owners"), MaxLength(255)]
    public string? Owners { get; set; }

    [Column("Budget", TypeName = "decimal(18,2)")]
    public decimal? Budget { get; set; }

    [Column("Liquidity", TypeName = "decimal(18,2)")]
    public decimal? Liquidity { get; set; }

    [Column("Start_Dates")]
    public DateTime? StartDates { get; set; }

    [Column("End_Dates")]
    public DateTime? EndDates { get; set; }

    public ExternalObjective? Objective { get; set; }
    public ICollection<ExternalProject> Projects { get; set; } = new List<ExternalProject>();
}
