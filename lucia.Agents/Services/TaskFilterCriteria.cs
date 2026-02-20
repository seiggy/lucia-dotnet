namespace lucia.Agents.Services;

/// <summary>
/// Filter criteria for querying archived tasks.
/// </summary>
public sealed class TaskFilterCriteria
{
    /// <summary>Filter by terminal status (Completed, Failed, Canceled).</summary>
    public string? Status { get; set; }

    /// <summary>Filter by agent ID that participated in the task.</summary>
    public string? AgentId { get; set; }

    /// <summary>Filter tasks archived on or after this date.</summary>
    public DateTime? FromDate { get; set; }

    /// <summary>Filter tasks archived on or before this date.</summary>
    public DateTime? ToDate { get; set; }

    /// <summary>Free-text search over user input.</summary>
    public string? Search { get; set; }

    /// <summary>Page number (1-based).</summary>
    public int Page { get; set; } = 1;

    /// <summary>Page size.</summary>
    public int PageSize { get; set; } = 25;
}
