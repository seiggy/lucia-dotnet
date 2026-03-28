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
}
