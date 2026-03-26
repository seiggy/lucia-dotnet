namespace lucia.EvalHarness.DataPipeline.Models;

/// <summary>
/// Represents an evaluation scenario that can be exported to YAML format.
/// Intermediate representation between raw data sources (issues, traces) and YAML test cases.
/// </summary>
public sealed class EvalScenario
{
    public required string Id { get; set; }

    public required string Description { get; set; }

    public required string Category { get; set; }

    public required string UserPrompt { get; set; }

    /// <summary>
    /// Agent that should handle this request (for orchestrator tests) or is being tested (for agent-specific tests).
    /// </summary>
    public string? ExpectedAgent { get; set; }

    /// <summary>
    /// Expected tool calls in format: tool name -> arguments.
    /// Empty list means no tools should be called (e.g., out-of-domain requests).
    /// </summary>
    public List<ExpectedToolCall> ExpectedToolCalls { get; set; } = [];

    /// <summary>
    /// Phrases that must appear in the response.
    /// </summary>
    public List<string> ResponseMustContain { get; set; } = [];

    /// <summary>
    /// Phrases that must NOT appear in the response.
    /// </summary>
    public List<string> ResponseMustNotContain { get; set; } = [];

    /// <summary>
    /// Success criteria for evaluation.
    /// </summary>
    public List<string> Criteria { get; set; } = [];

    /// <summary>
    /// Additional metadata for categorization and filtering.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Source of this scenario (e.g., "github-issue-107", "trace-abc123").
    /// </summary>
    public required string Source { get; set; }

    /// <summary>
    /// Optional: Initial Home Assistant state for stateful scenarios.
    /// </summary>
    public Dictionary<string, EntityState>? InitialState { get; set; }

    /// <summary>
    /// Optional: Expected final Home Assistant state after execution.
    /// </summary>
    public Dictionary<string, EntityState>? ExpectedFinalState { get; set; }
}

/// <summary>
/// Represents an expected tool call for evaluation.
/// </summary>
public sealed class ExpectedToolCall
{
    public required string Tool { get; set; }

    public Dictionary<string, string> Arguments { get; set; } = [];
}

/// <summary>
/// Represents Home Assistant entity state.
/// </summary>
public sealed class EntityState
{
    public required string State { get; set; }

    public Dictionary<string, object> Attributes { get; set; } = [];
}
