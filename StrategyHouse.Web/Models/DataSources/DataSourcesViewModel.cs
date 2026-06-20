using StrategyHouse.Web.Services.Dtos;

namespace StrategyHouse.Web.Models.DataSources;

public record DepartmentOption(string DeptCode, string NameAr);

public class DataSourcesViewModel
{
    public StrategyCountsDto Counts { get; set; } = new(0, 0, 0, 0, 0, StrategyHouse.Web.Services.StrategyDataSource.Empty);
    public StrategyDataSourceTrace Trace { get; set; } = new("—", "—", "—", "—", "—");
    public List<DepartmentOption> Departments { get; set; } = new();
    public string DivisionMapJson { get; set; } = "{}";
}
