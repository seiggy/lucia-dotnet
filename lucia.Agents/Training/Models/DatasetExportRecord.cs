namespace lucia.Agents.Training.Models;

/// <summary>
/// Record of a completed JSONL dataset export operation.
/// </summary>
public sealed class DatasetExportRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public ExportFilterCriteria FilterCriteria { get; set; } = new();

    public int RecordCount { get; set; }

    public long FileSizeBytes { get; set; }

    public string? FilePath { get; set; }

    public bool IsComplete { get; set; }
}
