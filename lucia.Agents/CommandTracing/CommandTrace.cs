namespace lucia.Agents.CommandTracing;

/// <summary>
/// A complete trace record for a conversation command processing request.
/// Captures the full lifecycle: input → pattern match → skill execution → response.
/// </summary>
public sealed record CommandTrace
{
    public required string Id { get; init; }
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Original text as received from the client (may include speaker tag).</summary>
    public required string RawText { get; init; }

    /// <summary>Text after speaker tag stripping.</summary>
    public required string CleanText { get; init; }

    public string? SpeakerId { get; init; }

    public required CommandTraceContext RequestContext { get; init; }

    /// <summary>Pattern matching phase result.</summary>
    public required CommandTraceMatch Match { get; init; }

    /// <summary>Skill execution phase (null when falling back to LLM).</summary>
    public CommandTraceExecution? Execution { get; init; }

    /// <summary>LLM fallback details (null when command was handled directly).</summary>
    public CommandTraceLlmFallback? LlmFallback { get; init; }

    /// <summary>Response template rendering details (null when LLM fallback or no template).</summary>
    public CommandTraceTemplateRender? TemplateRender { get; init; }

    public required CommandTraceOutcome Outcome { get; init; }
    public required double TotalDurationMs { get; init; }
    public string? ResponseText { get; init; }
    public string? Error { get; init; }
}
