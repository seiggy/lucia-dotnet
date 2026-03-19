namespace lucia.Agents.CommandTracing;

/// <summary>
/// Device and session context captured from the original conversation request.
/// </summary>
public sealed record CommandTraceContext
{
    public string? ConversationId { get; init; }
    public string? DeviceId { get; init; }
    public string? DeviceArea { get; init; }
    public string? DeviceType { get; init; }
    public string? UserId { get; init; }
    public string? SpeakerId { get; init; }
    public string? Location { get; init; }
}
