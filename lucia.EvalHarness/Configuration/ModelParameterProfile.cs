namespace lucia.EvalHarness.Configuration;

/// <summary>
/// Defines inference parameters for an Ollama model evaluation run.
/// These parameters are passed via <see cref="Microsoft.Extensions.AI.ChatOptions"/>
/// and recorded in eval results for reproducibility.
/// </summary>
public sealed record ModelParameterProfile
{
    /// <summary>
    /// Human-readable name for this profile (e.g., "default", "creative", "precise").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Controls randomness. Lower values make output more deterministic.
    /// Ollama default: 0.8.
    /// </summary>
    public double Temperature { get; init; } = 0.8;

    /// <summary>
    /// Limits token selection to the top K most likely tokens.
    /// Ollama default: 40.
    /// </summary>
    public int TopK { get; init; } = 40;

    /// <summary>
    /// Nucleus sampling: considers tokens with cumulative probability ≥ TopP.
    /// Ollama default: 0.9.
    /// </summary>
    public double TopP { get; init; } = 0.9;

    /// <summary>
    /// Maximum number of tokens to generate. -1 means unlimited.
    /// Ollama default: -1.
    /// </summary>
    public int NumPredict { get; init; } = -1;

    /// <summary>
    /// Penalizes repeated tokens. Higher values reduce repetition.
    /// Ollama default: 1.1.
    /// </summary>
    public double RepeatPenalty { get; init; } = 1.1;

    /// <summary>
    /// Random seed for reproducibility. Null means non-deterministic.
    /// </summary>
    public int? Seed { get; init; }

    /// <summary>
    /// The default Ollama parameter profile matching Ollama's built-in defaults.
    /// </summary>
    public static ModelParameterProfile Default { get; } = new() { Name = "default" };

    /// <summary>
    /// A low-temperature, high-precision profile for deterministic outputs.
    /// </summary>
    public static ModelParameterProfile Precise { get; } = new()
    {
        Name = "precise",
        Temperature = 0.1,
        TopK = 10,
        TopP = 0.5,
        RepeatPenalty = 1.2
    };

    /// <summary>
    /// A higher-temperature profile for more creative/varied responses.
    /// </summary>
    public static ModelParameterProfile Creative { get; } = new()
    {
        Name = "creative",
        Temperature = 1.2,
        TopK = 80,
        TopP = 0.95,
        RepeatPenalty = 1.0
    };

    /// <summary>
    /// Returns built-in profiles keyed by name.
    /// </summary>
    public static IReadOnlyDictionary<string, ModelParameterProfile> BuiltInProfiles { get; } =
        new Dictionary<string, ModelParameterProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["default"] = Default,
            ["precise"] = Precise,
            ["creative"] = Creative
        };

    /// <summary>
    /// Formats parameters as a compact summary string for display.
    /// </summary>
    public string ToSummary() =>
        $"temp={Temperature}, top_k={TopK}, top_p={TopP}, repeat={RepeatPenalty}";
}
