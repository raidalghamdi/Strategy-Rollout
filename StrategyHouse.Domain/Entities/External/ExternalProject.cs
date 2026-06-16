using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StrategyHouse.Domain.Entities.External;

// Phase 17 — mirrors the external MSSQL "Projects" table (Option A schema).
[Table("Projects")]
public class ExternalProject
{
    [Key, Column("Project_Code"), MaxLength(15)]
    public string ProjectCode { get; set; } = string.Empty;

    [Column("Project_Name"), MaxLength(255)]
    public string? ProjectName { get; set; }

    [Column("Initiative_Code"), MaxLength(15)]
    public string? InitiativeCode { get; set; }

    [Column("PLR_Code"), MaxLength(15)]
    public string? PlrCode { get; set; }

    [Column("Project_Type"), MaxLength(50)]
    public string? ProjectType { get; set; }

    [Column("Project_Status"), MaxLength(50)]
    public string? ProjectStatus { get; set; }

    [Column("Budget_Liquidity", TypeName = "decimal(18,2)")]
    public decimal? BudgetLiquidity { get; set; }

    [Column("Liquidity_2025", TypeName = "decimal(18,2)")]
    public decimal? Liquidity2025 { get; set; }
    [Column("Liquidity_2026", TypeName = "decimal(18,2)")]
    public decimal? Liquidity2026 { get; set; }
    [Column("Liquidity_2027", TypeName = "decimal(18,2)")]
    public decimal? Liquidity2027 { get; set; }
    [Column("Liquidity_2028", TypeName = "decimal(18,2)")]
    public decimal? Liquidity2028 { get; set; }
    [Column("Liquidity_2029", TypeName = "decimal(18,2)")]
    public decimal? Liquidity2029 { get; set; }
    [Column("Liquidity_2030", TypeName = "decimal(18,2)")]
    public decimal? Liquidity2030 { get; set; }
    [Column("Liquidity_2031", TypeName = "decimal(18,2)")]
    public decimal? Liquidity2031 { get; set; }

    [Column("GAC_Budget", TypeName = "decimal(18,2)")]
    public decimal? GacBudget { get; set; }

    [Column("Project_Sponsor"), MaxLength(255)]
    public string? ProjectSponsor { get; set; }

    [Column("Project_Manager"), MaxLength(255)]
    public string? ProjectManager { get; set; }

    [Column("Division"), MaxLength(255)]
    public string? Division { get; set; }

    [Column("Project_Phase"), MaxLength(255)]
    public string? ProjectPhase { get; set; }

    public ExternalInitiative? Initiative { get; set; }
    public ExternalPillar? Pillar { get; set; }
}
