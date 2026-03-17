using System.Text.Json.Serialization;

namespace lucia.Wyoming.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackgroundTaskStatus
{
    Queued,
    Running,
    Complete,
    Failed,
    Cancelled,
}
