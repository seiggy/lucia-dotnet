namespace lucia.Agents.CommandTracing;

/// <summary>
/// A single skill method invocation recorded during command execution.
/// Captures the method name, serialized arguments, response, and timing.
/// </summary>
public sealed record CommandTraceToolCall
{
    /// <summary>Skill method name (e.g., "ControlLightsAsync", "SetClimateTemperatureAsync").</summary>
    public required string MethodName { get; init; }

    /// <summary>JSON-serialized arguments passed to the skill method.</summary>
    public string? Arguments { get; init; }

    /// <summary>JSON-serialized or text response returned by the skill method.</summary>
    public string? Response { get; init; }

    /// <summary>Duration of this individual tool call in milliseconds.</summary>
    public required double DurationMs { get; init; }

    public required bool Success { get; init; }

    public string? Error { get; init; }
}
