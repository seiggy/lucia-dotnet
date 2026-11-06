namespace lucia.Agents.Family;

/// <summary>
/// A prepared capability invocation that needs or had a human decision. The decision is bound
/// to <see cref="PayloadHash"/>; a changed target or argument needs a new record.
/// </summary>
public sealed class ActionRecord
{
    public required string Id { get; init; }

    public required string RequesterId { get; init; }

    public required CapabilityKind Kind { get; init; }

    public required string CapabilityId { get; init; }

    public string? AgentId { get; init; }

    /// <summary>
    /// Normalized arguments as JSON.
    /// </summary>
    public string ArgumentsJson { get; init; } = "{}";

    public required string PayloadHash { get; init; }

    /// <summary>
    /// Human-readable description generated from the record, not from model output.
    /// </summary>
    public required string Summary { get; init; }

    public required List<string> ApproverIds { get; init; }

    public ActionStatus Status { get; set; } = ActionStatus.AwaitingDecision;

    public string? DecidedBy { get; set; }

    public DateTime? DecidedAt { get; set; }

    public string? Outcome { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public required DateTime ExpiresAt { get; init; }
}
