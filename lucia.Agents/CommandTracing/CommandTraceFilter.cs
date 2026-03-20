namespace lucia.Agents.CommandTracing;

/// <summary>
/// Filter criteria for querying command traces.
/// </summary>
public sealed record CommandTraceFilter
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? Search { get; init; }
    public CommandTraceOutcome? Outcome { get; init; }
    public string? SkillId { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
}
