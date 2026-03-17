using System.Text.Json.Serialization;

namespace lucia.Wyoming.Models;

/// <summary>
/// Represents a single stage within a multi-stage background task.
/// </summary>
public sealed record BackgroundTaskStage
{
    public required string Name { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required BackgroundTaskStatus Status { get; init; }

    public int ProgressPercent { get; init; }
    public string? ProgressMessage { get; init; }
}
