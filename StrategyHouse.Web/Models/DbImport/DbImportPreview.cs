namespace StrategyHouse.Web.Models.DbImport;

// Phase 19.22 — dry-run summary of an Excel → SQLite full-mirror import. Computed by
// DbImportService.AnalyzeAsync without writing anything; rendered on the Preview page
// so the admin sees exactly what will change before entering their password.
public class DbImportPreview
{
    public Guid PendingId { get; set; }
    public string FileName { get; set; } = "";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    public List<SheetDiff> Sheets { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> SkippedSheets { get; set; } = new();

    public int TotalInserts => Sheets.Sum(s => s.Inserts);
    public int TotalUpdates => Sheets.Sum(s => s.Updates);
    public int TotalDeletes => Sheets.Sum(s => s.Deletes);
    public bool HasChanges => TotalInserts + TotalUpdates + TotalDeletes > 0;

    public class SheetDiff
    {
        public string Table { get; set; } = "";
        public int Inserts { get; set; }
        public int Updates { get; set; }
        public int Deletes { get; set; }
        public int ExcelRows { get; set; }
        public int DbRows { get; set; }
        public List<string> Notes { get; set; } = new();
    }
}
