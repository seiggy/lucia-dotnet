namespace lucia.Agents.Training.Models;

/// <summary>
/// Filter criteria used when exporting a JSONL dataset.
/// </summary>
public sealed class ExportFilterCriteria
{
    public LabelStatus? LabelFilter { get; set; }

    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public string? AgentFilter { get; set; }

    public string? ModelFilter { get; set; }

    public bool IncludeCorrections { get; set; }
}
