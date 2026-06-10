namespace StrategyHouse.Domain.Entities;

/// <summary>
/// A department in the organization. ~18 in the GAC rollout.
/// </summary>
public class Department
{
    public int Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? DescriptionAr { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<DepartmentProject> Projects { get; set; } = new List<DepartmentProject>();
    public ICollection<DepartmentKpi> Kpis { get; set; } = new List<DepartmentKpi>();
    public ICollection<DepartmentRole> Roles { get; set; } = new List<DepartmentRole>();
}

/// <summary>
/// A strategic or operational project owned by a department.
/// Links to one or more objectives or pillars in the active framework.
/// </summary>
public class DepartmentProject
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public Department? Department { get; set; }

    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? DescriptionAr { get; set; }

    /// <summary>Strategic vs Operational project — display label.</summary>
    public string Kind { get; set; } = "Strategic";

    /// <summary>Optional default link to a framework element (objective or pillar).</summary>
    public int? DefaultElementId { get; set; }
    public FrameworkElement? DefaultElement { get; set; }
}

/// <summary>
/// A KPI owned by a department.
/// </summary>
public class DepartmentKpi
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public Department? Department { get; set; }

    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public string? Target { get; set; }

    public int? DefaultElementId { get; set; }
    public FrameworkElement? DefaultElement { get; set; }
}

/// <summary>
/// A role/job-title within a department.
/// </summary>
public class DepartmentRole
{
    public int Id { get; set; }
    public int DepartmentId { get; set; }
    public Department? Department { get; set; }

    public string TitleAr { get; set; } = string.Empty;
    public string TitleEn { get; set; } = string.Empty;
}
