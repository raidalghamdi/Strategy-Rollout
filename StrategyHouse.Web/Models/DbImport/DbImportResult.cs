namespace StrategyHouse.Web.Models.DbImport;

// Phase 19.22 — outcome of an applied Excel → SQLite full-mirror import. On success the
// transaction committed; on failure it was rolled back and Error carries the message.
public class DbImportResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? BackupFileName { get; set; }

    public int TotalInserts { get; set; }
    public int TotalUpdates { get; set; }
    public int TotalDeletes { get; set; }

    public List<string> Warnings { get; set; } = new();
}
