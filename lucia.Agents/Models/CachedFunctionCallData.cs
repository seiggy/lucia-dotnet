namespace lucia.Agents.Models;

/// <summary>
/// Serializable representation of a function/tool call from a cached LLM response.
/// Used by <see cref="CachedChatResponseData"/> to replay tool calls through the
/// function-invoking layer while skipping the LLM planning call.
/// </summary>
public sealed class CachedFunctionCallData
{
    /// <summary>Unique identifier for this tool call (e.g., "call_abc123").</summary>
    public string CallId { get; set; } = string.Empty;

    /// <summary>Name of the function/tool to invoke.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Serialized JSON arguments for the function call.</summary>
    public string? ArgumentsJson { get; set; }
}
