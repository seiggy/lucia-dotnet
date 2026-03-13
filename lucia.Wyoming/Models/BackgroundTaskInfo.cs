namespace lucia.Wyoming.Models;

public sealed record BackgroundTaskInfo
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public required BackgroundTaskStatus Status { get; init; }
    public int ProgressPercent { get; init; }
    public string? ProgressMessage { get; init; }
    public string? Error { get; init; }
    public string? ResultData { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; init; }
}
