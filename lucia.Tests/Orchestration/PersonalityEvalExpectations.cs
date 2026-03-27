using System.Text.Json.Serialization;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Expected outcomes for a personality eval scenario. Each field maps to a
/// specific validation check in <see cref="PersonalityEvalTests"/>.
/// </summary>
public sealed class PersonalityEvalExpectations
{
    /// <summary>
    /// Substrings the rewritten response must contain (case-insensitive).
    /// Catches information loss — dropped entity names, rooms, or values.
    /// </summary>
    [JsonPropertyName("mustContain")]
    public List<string> MustContain { get; set; } = [];

    /// <summary>
    /// Substrings the rewritten response must NOT contain (case-insensitive).
    /// Primary defense against refusal injection ("I'm an AI", "I can't").
    /// </summary>
    [JsonPropertyName("mustNotContain")]
    public List<string> MustNotContain { get; set; } = [];

    /// <summary>
    /// Expected sentiment polarity: "positive", "negative", "neutral", or "mixed".
    /// Catches meaning inversion (success → failure or vice versa).
    /// </summary>
    [JsonPropertyName("sentimentPreserved")]
    public string? SentimentPreserved { get; set; }

    /// <summary>
    /// Maximum character length for the rewritten response.
    /// Catches excessive verbosity — critical for voice/TTS outputs.
    /// </summary>
    [JsonPropertyName("maxLength")]
    public int MaxLength { get; set; }

    /// <summary>
    /// When <c>true</c>, the rewritten response must end with '?'.
    /// Catches question destruction — clarification questions converted to statements.
    /// </summary>
    [JsonPropertyName("isQuestion")]
    public bool IsQuestion { get; set; }
}
