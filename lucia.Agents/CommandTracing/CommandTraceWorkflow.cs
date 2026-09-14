namespace lucia.Agents.CommandTracing;

public sealed record CommandTraceWorkflow
{
    public required string Name { get; init; }
    public string? Stage { get; init; }
    public string? ConversationId { get; init; }
    public bool NeedsInput { get; init; }
}
