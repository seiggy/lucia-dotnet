namespace lucia.Agents.Orchestration;

/// <summary>
/// Options for the personality prompt feature.
/// When <see cref="Instructions"/> is set, the result aggregator rewrites
/// the composed agent response through an LLM using these instructions
/// as the system prompt, giving Lucia a configurable personality.
/// </summary>
public sealed class PersonalityPromptOptions
{
    /// <summary>Fallback output ceiling, not an input-context limit or a target response length.</summary>
    public const int DefaultMaxOutputTokens = 4096;

    /// <summary>Optional positive output-token ceiling shared by fast-path and orchestrated rewrites.</summary>
    public int? MaxOutputTokens { get; set; }

    public int EffectiveMaxOutputTokens => ResolveMaxOutputTokens(MaxOutputTokens);

    internal static int ResolveMaxOutputTokens(int? configured) => configured switch
    {
        null => DefaultMaxOutputTokens,
        > 0 => configured.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(configured), "Personality output-token limit must be positive.")
    };

    /// <summary>
    /// Master switch for the personality response engine.
    /// When <c>false</c> (default), fast-path command responses use canned templates.
    /// When <c>true</c>, responses are passed through the personality LLM prompt.
    /// </summary>
    public bool UsePersonalityResponses { get; set; }

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

    /// <summary>
    /// When <c>true</c>, the personality response renderer may include SSML or
    /// voice-tag markup in its output for speech synthesis platforms.
    /// Default is <c>false</c>.
    /// </summary>
    public bool SupportVoiceTags { get; set; }
}
