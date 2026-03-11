namespace lucia.Agents.Orchestration;

/// <summary>
/// Options for the personality prompt feature.
/// When <see cref="Instructions"/> is set, the result aggregator rewrites
/// the composed agent response through an LLM using these instructions
/// as the system prompt, giving Lucia a configurable personality.
/// </summary>
public sealed class PersonalityPromptOptions
{
    /// <summary>
    /// System prompt that defines Lucia's personality and communication style.
    /// When set, agent responses are rewritten using this prompt before being returned.
    /// Leave null or empty to return raw agent responses (default behavior).
    /// </summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// Optional model provider name for personality rewriting.
    /// When set, resolves a separate <c>IChatClient</c> via <c>IChatClientResolver</c>.
    /// When null or empty, falls back to the orchestrator's default chat client.
    /// </summary>
    public string? ModelConnectionName { get; set; }
}
