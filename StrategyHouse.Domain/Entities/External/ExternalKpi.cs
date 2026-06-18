using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StrategyHouse.Domain.Entities.External;

// Phase 17 — mirrors the external MSSQL "KPIs" table (Option A schema).
// The Division column here is the authoritative source for the department
// directory (DepartmentDirectoryService reads KPIs.Division DISTINCT).
[Table("KPIs")]
public class ExternalKpi
{
    [Key, Column("KPI_Code"), MaxLength(50)]
    public string KpiCode { get; set; } = string.Empty;

    [Column("KPI_Name"), MaxLength(255)]
    public string? KpiName { get; set; }

    [Column("Activation_Status"), MaxLength(15)]
    public string? ActivationStatus { get; set; }

    [Column("KPI_Type"), MaxLength(50)]
    public string? KpiType { get; set; }

    [Column("Objective_Code"), MaxLength(15)]
    public string? ObjectiveCode { get; set; }

    [Column("PLR_Code"), MaxLength(15)]
    public string? PlrCode { get; set; }

    [Column("Division"), MaxLength(255)]
    public string? Division { get; set; }

    [Column("Frequency"), MaxLength(50)]
    public string? Frequency { get; set; }

    // Phase 19.18 — the real MSSQL "KPIs" table has four separate columns
    // (Unit, Direction, Minimum, Maximum). The old combined Unit_Direction /
    // Minimum_Maximum columns never existed, so EF's auto-generated SELECT failed
    // with "Invalid column name". These map the genuine columns; the SQLite mirror
    // still stores the combined shapes (see MssqlMirrorService projection).
    [Column("Unit"), MaxLength(255)]
    public string? Unit { get; set; }

    [Column("Direction"), MaxLength(255)]
    public string? Direction { get; set; }

    [Column("Index_Weight"), MaxLength(5)]
    public string? IndexWeight { get; set; }

    [Column("Minimum", TypeName = "decimal(18,4)")]
    public decimal? Minimum { get; set; }

    [Column("Maximum", TypeName = "decimal(18,4)")]
    public decimal? Maximum { get; set; }

    [Column("Target_2025"), MaxLength(50)]
    public string? Target2025 { get; set; }
    [Column("Target_2026"), MaxLength(50)]
    public string? Target2026 { get; set; }
    [Column("Target_2027"), MaxLength(50)]
    public string? Target2027 { get; set; }
    [Column("Target_2028"), MaxLength(50)]
    public string? Target2028 { get; set; }
    [Column("Target_2029"), MaxLength(50)]
    public string? Target2029 { get; set; }
    [Column("Target_2030"), MaxLength(50)]
    public string? Target2030 { get; set; }

    [Column("Automation_Status"), MaxLength(255)]
    public string? AutomationStatus { get; set; }

    public ExternalObjective? Objective { get; set; }
    public ExternalPillar? Pillar { get; set; }
}
