using System.Text.Json.Serialization;

namespace lucia.Agents.Services;

/// <summary>
/// Aggregate statistics about archived tasks.
/// </summary>
public sealed class TaskStats
{
    [JsonPropertyName("totalTasks")]
    public int TotalTasks { get; set; }

    [JsonPropertyName("completedCount")]
    public int CompletedCount { get; set; }

    [JsonPropertyName("failedCount")]
    public int FailedCount { get; set; }

    [JsonPropertyName("canceledCount")]
    public int CanceledCount { get; set; }

    [JsonPropertyName("byAgent")]
    public Dictionary<string, int> ByAgent { get; set; } = new();
}
