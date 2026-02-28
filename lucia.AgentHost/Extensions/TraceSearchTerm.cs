namespace lucia.AgentHost.Extensions;

/// <summary>A search term extracted from conversation traces.</summary>
public sealed record TraceSearchTerm
{
    public required string SearchTerm { get; init; }
    public int OccurrenceCount { get; init; }
    public DateTime LastSeen { get; init; }
    public string? TraceId { get; init; }
}
