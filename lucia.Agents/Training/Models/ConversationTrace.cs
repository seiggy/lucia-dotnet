using System.Text.Json.Serialization;

namespace lucia.Agents.Training.Models;

/// <summary>
/// Primary document representing a single orchestrator request lifecycle.
/// </summary>
public sealed class ConversationTrace
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public required string SessionId { get; set; }

    public string? TaskId { get; set; }

    public required string UserInput { get; set; }

    public RoutingDecision? Routing { get; set; }

    public List<AgentExecutionRecord> AgentExecutions { get; set; } = [];

    public string? FinalResponse { get; set; }

    public double TotalDurationMs { get; set; }

    public TraceLabel Label { get; set; } = new();

    public bool IsErrored { get; set; }

    public string? ErrorMessage { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = [];
}
