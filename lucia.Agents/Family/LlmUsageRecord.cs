namespace lucia.Agents.Family;

/// <summary>
/// Token use and estimated cost of one model call made for a member.
/// </summary>
public sealed class LlmUsageRecord
{
    public required string Id { get; init; }

    public required string MemberId { get; init; }

    public required DateTime Timestamp { get; init; }

    public required string ProviderId { get; init; }

    public string? Model { get; init; }

    public string? AgentId { get; init; }

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public decimal CostUsd { get; init; }
}
