using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Personality;

/// <summary>
/// Scores returned by the LLM judge for a single personality eval scenario.
/// </summary>
public sealed class JudgeResult
{
    [JsonPropertyName("personalityScore")]
    public int PersonalityScore { get; set; }

    [JsonPropertyName("personalityReason")]
    public string PersonalityReason { get; set; } = string.Empty;

    [JsonPropertyName("meaningScore")]
    public int MeaningScore { get; set; }

    [JsonPropertyName("meaningReason")]
    public string MeaningReason { get; set; } = string.Empty;

    /// <summary>
    /// Combined score (average of personality + meaning, 1–5 scale).
    /// </summary>
    [JsonIgnore]
    public double CombinedScore => (PersonalityScore + MeaningScore) / 2.0;

    /// <summary>
    /// True when the judge call exceeded its configured deadline. Distinguishes a
    /// timeout from a genuine zero-score judgement so reports don't conflate the two.
    /// </summary>
    [JsonIgnore]
    public bool TimedOut { get; set; }
}
