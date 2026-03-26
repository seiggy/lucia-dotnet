namespace lucia.Wyoming.CommandRouting;

public sealed class CommandRoutingOptions
{
    public const string SectionName = "Wyoming:CommandRouting";

    public bool Enabled { get; set; } = true;

    public float ConfidenceThreshold { get; set; } = 0.8f;

    public bool FallbackToLlm { get; set; } = true;

    /// <summary>
    /// When enabled, fast-path command responses are passed through the personality
    /// LLM prompt instead of returning canned template responses. Adds one lightweight
    /// LLM round-trip (~200-500ms). Default is OFF (zero-latency canned responses).
    /// </summary>
    public bool UsePersonalityResponses { get; set; }

    /// <summary>
    /// System prompt that defines the assistant's personality and communication style.
    /// Only used when <see cref="UsePersonalityResponses"/> is <c>true</c>.
    /// When null or empty with personality mode enabled, falls back to canned responses.
    /// </summary>
    public string? PersonalityPrompt { get; set; }

    /// <summary>
    /// Optional model provider connection name for personality rewriting.
    /// When null or empty, uses the default chat provider via <c>IChatClientResolver</c>.
    /// </summary>
    public string? PersonalityModelConnectionName { get; set; }
}
