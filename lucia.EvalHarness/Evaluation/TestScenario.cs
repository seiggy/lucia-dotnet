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
    /// When set, the scenario runner prefixes the user prompt with speaker metadata
    /// using the same bracketed header format as <c>EvalRunner.BuildScenarioPrompt</c>,
    /// mirroring how the Wyoming voice pipeline tags speaker identity.
    /// </summary>
    public string? SpeakerId { get; init; }

    /// <summary>
    /// Optional device area context (e.g., "Zack's Office").
    /// When set, included in the request metadata header.
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

    /// <summary>Optional semantic response criteria evaluated by the configured LLM judge.</summary>
    public string? ResponseCriteria { get; init; }

    /// <summary>
    /// Expected entity states after the scenario completes.
    /// Validated against the FakeHA client's in-memory state.
    /// </summary>
    public Dictionary<string, EntityStateAssertion> ExpectedFinalState { get; init; } = [];
}
