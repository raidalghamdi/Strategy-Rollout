using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StrategyHouse.Domain.Entities;

// =====================================================================
// Strategy schema — Option A (strict mirror of the HTML diagram)
// Pillars → Objectives → KPIs / Initiatives → Projects
// Denormalised shortcuts (KPIs.PLR_Code, Projects.PLR_Code) and
// Initiatives.Objective_Name are preserved as drawn.
// =====================================================================

[Table("Pillars")]
public class Pillar
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

    // Navigation
    public ICollection<Objective> Objectives { get; set; } = new List<Objective>();
    public ICollection<Kpi> Kpis { get; set; } = new List<Kpi>();
    public ICollection<Project> Projects { get; set; } = new List<Project>();
}

[Table("Objectives")]
public class Objective
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

    // Navigation
    [ForeignKey(nameof(PlrCode))]
    public Pillar? Pillar { get; set; }
    public ICollection<Kpi> Kpis { get; set; } = new List<Kpi>();
    public ICollection<Initiative> Initiatives { get; set; } = new List<Initiative>();
}

[Table("KPIs")]
public class Kpi
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

    // Denormalised shortcut to Pillar (per diagram)
    [Column("PLR_Code"), MaxLength(15)]
    public string? PlrCode { get; set; }

    [Column("Division"), MaxLength(255)]
    public string? Division { get; set; }

    // FK to Departments lookup
    [Column("Department_Code"), MaxLength(15)]
    public string? DepartmentCode { get; set; }

    [Column("Frequency"), MaxLength(50)]
    public string? Frequency { get; set; }

    [Column("Unit"), MaxLength(50)]
    public string? Unit { get; set; }

    [Column("Direction"), MaxLength(50)]
    public string? Direction { get; set; }

    [Column("Index_Weight"), MaxLength(5)]
    public string? IndexWeight { get; set; }

    [Column("Minimum", TypeName = "decimal(18,4)")]
    public decimal? Minimum { get; set; }

    [Column("Maximum", TypeName = "decimal(18,4)")]
    public decimal? Maximum { get; set; }

    // Six yearly targets 2025-2030 (per diagram "Target 2025–2030 ×6")
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

    // Navigation
    [ForeignKey(nameof(ObjectiveCode))]
    public Objective? Objective { get; set; }

    [ForeignKey(nameof(PlrCode))]
    public Pillar? Pillar { get; set; }

    [ForeignKey(nameof(DepartmentCode))]
    public Department? Department { get; set; }
}

[Table("Initiatives")]
public class Initiative
{
    [Key, Column("Initiative_Code"), MaxLength(15)]
    public string InitiativeCode { get; set; } = string.Empty;

    [Column("Initiative_Name"), MaxLength(255)]
    public string? InitiativeName { get; set; }

    [Column("Objective_Code"), MaxLength(15)]
    public string? ObjectiveCode { get; set; }

    // Denormalised name copy (per diagram)
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

    // Navigation
    [ForeignKey(nameof(ObjectiveCode))]
    public Objective? Objective { get; set; }

    public ICollection<Project> Projects { get; set; } = new List<Project>();
}

[Table("Projects")]
public class Project
{
    [Key, Column("Project_Code"), MaxLength(15)]
    public string ProjectCode { get; set; } = string.Empty;

    [Column("Project_Name"), MaxLength(255)]
    public string? ProjectName { get; set; }

    [Column("Initiative_Code"), MaxLength(15)]
    public string? InitiativeCode { get; set; }

    // Denormalised shortcut to Pillar (per diagram)
    [Column("PLR_Code"), MaxLength(15)]
    public string? PlrCode { get; set; }

    [Column("Project_Type"), MaxLength(50)]
    public string? ProjectType { get; set; }

    [Column("Project_Status"), MaxLength(50)]
    public string? ProjectStatus { get; set; }

    [Column("Budget", TypeName = "decimal(18,2)")]
    public decimal? Budget { get; set; }

    [Column("Liquidity", TypeName = "decimal(18,2)")]
    public decimal? Liquidity { get; set; }

    // 7-year liquidity spread 2025-2031 (per diagram "Liquidity 2025–2031 ×7")
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

    // FK to Departments lookup
    [Column("Department_Code"), MaxLength(15)]
    public string? DepartmentCode { get; set; }

    [Column("Project_Phase"), MaxLength(255)]
    public string? ProjectPhase { get; set; }

    // Navigation
    [ForeignKey(nameof(InitiativeCode))]
    public Initiative? Initiative { get; set; }

    [ForeignKey(nameof(PlrCode))]
    public Pillar? Pillar { get; set; }

    [ForeignKey(nameof(DepartmentCode))]
    public Department? Department { get; set; }
}

[Table("Departments")]
public class Department
{
    [Key, Column("Dept_Code"), MaxLength(15)]
    public string DeptCode { get; set; } = string.Empty;

    [Column("Name_Ar"), MaxLength(255)]
    public string? NameAr { get; set; }

    [Column("Name_En"), MaxLength(255)]
    public string? NameEn { get; set; }

    [Column("Parent_Sector"), MaxLength(255)]
    public string? ParentSector { get; set; }

    [Column("Level")]
    public int? Level { get; set; }

    [Column("Is_Active")]
    public bool IsActive { get; set; } = true;
}
