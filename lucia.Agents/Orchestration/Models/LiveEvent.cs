using System.Text.Json.Serialization;

namespace lucia.Agents.Orchestration.Models;

/// <summary>
/// SSE event payload representing a single orchestration lifecycle event
/// for the live activity dashboard.
/// </summary>
public sealed class LiveEvent
{
    public required string Type { get; init; }

    public string? AgentName { get; init; }

    public string? ToolName { get; init; }

    public string? State { get; init; }

    public string? Message { get; init; }

    public bool IsRemote { get; init; }

    public double? Confidence { get; init; }

    public long? DurationMs { get; init; }

    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; init; }

    // Well-known event types
    public static class Types
    {
        public const string RequestStart = "requestStart";
        public const string Connected = "connected";
        public const string Routing = "routing";
        public const string AgentStart = "agentStart";
        public const string ToolCall = "toolCall";
        public const string ToolResult = "toolResult";
        public const string AgentComplete = "agentComplete";
        public const string RequestComplete = "requestComplete";
        public const string Error = "error";
    }

    // Well-known agent states
    public static class States
    {
        public const string ProcessingPrompt = "Processing Prompt...";
        public const string CallingTools = "Calling Tools...";
        public const string GeneratingResponse = "Generating Response...";
        public const string Processing = "Processing...";
        public const string Idle = "Idle";
        public const string Error = "Error";
    }
}
