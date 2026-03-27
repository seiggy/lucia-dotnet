using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Personality;

/// <summary>
/// Expected outcomes for a personality eval scenario. Each field maps to a
/// specific validation check in <see cref="PersonalityEvalRunner"/>.
/// </summary>
public sealed class PersonalityEvalExpectations
{
    /// <summary>
    /// Substrings the rewritten response must contain (case-insensitive).
    /// </summary>
    [JsonPropertyName("mustContain")]
    public List<string> MustContain { get; set; } = [];

    /// <summary>
    /// Substrings the rewritten response must NOT contain (case-insensitive).
    /// </summary>
    [JsonPropertyName("mustNotContain")]
    public List<string> MustNotContain { get; set; } = [];

    /// <summary>
    /// Expected sentiment polarity: "positive", "negative", "neutral", or "mixed".
    /// </summary>
    [JsonPropertyName("sentimentPreserved")]
    public string? SentimentPreserved { get; set; }

    /// <summary>
    /// Maximum character length for the rewritten response.
    /// </summary>
    [JsonPropertyName("maxLength")]
    public int MaxLength { get; set; }

    /// <summary>
    /// When <c>true</c>, the rewritten response must end with '?'.
    /// </summary>
    [JsonPropertyName("isQuestion")]
    public bool IsQuestion { get; set; }
}
