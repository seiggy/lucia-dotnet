using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Evaluation;

/// <summary>
/// A test scenario with known initial HA state and expected tool call assertions.
/// Unlike generic AgentEval TestCases, scenarios precisely define what state
/// the mock HA should be in, and what tool calls (in order, with specific arguments)
/// the agent should make.
/// </summary>
public sealed class TestScenario
{
    /// <summary>Unique scenario identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable description of what's being tested.</summary>
    public string? Description { get; init; }

    /// <summary>Category for grouping (e.g., "control", "query", "stt-robustness", "out-of-domain").</summary>
    public string? Category { get; init; }

    /// <summary>
    /// Optional speaker identity for voice-pipeline scenarios.
    /// When set, prefixed to the user prompt as a speaker tag: "&lt;Dianna /&gt; turn on my office lights"
    /// This mirrors how the Wyoming voice pipeline tags speaker identity.
    /// </summary>
    public string? SpeakerId { get; init; }

    /// <summary>
    /// Optional device area context (e.g., "Zack's Office").
    /// When set, included in the system prompt context block.
    /// </summary>
    public string? DeviceArea { get; init; }

    /// <summary>
    /// Optional device location context (e.g., "Home").
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Initial entity states to load before running the scenario.
    /// Keys are entity IDs, values define the state + attributes.
    /// </summary>
    public Dictionary<string, EntitySetup> InitialState { get; init; } = [];

    /// <summary>The user prompt sent to the agent.</summary>
    public required string UserPrompt { get; init; }

    /// <summary>
    /// Expected tool call chain in order. Each entry defines a tool name
    /// and optional argument matchers. Use <c>null</c> for "any value".
    /// </summary>
    public List<ExpectedToolCall> ExpectedToolCalls { get; init; } = [];

    /// <summary>
    /// Substrings that MUST appear in the agent's final text response.
    /// Case-insensitive matching.
    /// </summary>
    public List<string> ResponseMustContain { get; init; } = [];

    /// <summary>
    /// Substrings that must NOT appear in the agent's final text response.
    /// </summary>
    public List<string> ResponseMustNotContain { get; init; } = [];

    /// <summary>
    /// Expected entity states after the scenario completes.
    /// Validated against the FakeHA client's in-memory state.
    /// </summary>
    public Dictionary<string, EntityStateAssertion> ExpectedFinalState { get; init; } = [];
}

/// <summary>
/// Defines an entity's initial state for scenario setup.
/// </summary>
public sealed class EntitySetup
{
    public required string State { get; init; }
    public Dictionary<string, object>? Attributes { get; init; }
}

/// <summary>
/// An expected tool call with optional argument assertions.
/// </summary>
public sealed class ExpectedToolCall
{
    /// <summary>The tool function name (e.g., "ControlLights", "GetLightsState").
    /// AIFunctionFactory strips the "Async" suffix from method names; the validator
    /// normalizes both expected and actual names so either form matches.</summary>
    public required string Tool { get; init; }

    /// <summary>
    /// Argument matchers. Key = argument name, Value = expected value.
    /// Use "*" for "any value", use "contains:text" for substring match.
    /// Omitted arguments are not checked.
    /// </summary>
    public Dictionary<string, string> Arguments { get; init; } = [];
}

/// <summary>
/// Post-scenario state assertion for an entity.
/// </summary>
public sealed class EntityStateAssertion
{
    public required string State { get; init; }
    public Dictionary<string, string>? Attributes { get; init; }
}
