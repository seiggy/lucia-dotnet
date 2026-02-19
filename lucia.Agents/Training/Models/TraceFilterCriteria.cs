namespace lucia.Agents.Training.Models;

/// <summary>
/// Filter criteria for querying traces in the dashboard.
/// </summary>
public sealed class TraceFilterCriteria
{
    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public string? AgentFilter { get; set; }

    public string? ModelFilter { get; set; }

    public LabelStatus? LabelFilter { get; set; }

    public string? SearchText { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 25;
}
