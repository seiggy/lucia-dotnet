namespace lucia.AgentHost.Extensions;

/// <summary>Response returned when an optimization job is started.</summary>
public sealed record StartJobResponse
{
    public required string JobId { get; init; }
}
