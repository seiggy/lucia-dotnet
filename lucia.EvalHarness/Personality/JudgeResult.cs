using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Personality;

/// <summary>
/// Scores returned by the LLM judge for a single personality eval scenario.
/// </summary>
public sealed class JudgeResult
{
    [JsonPropertyName("personalityScore")]
    public int? PersonalityScore { get; init; }

    [JsonPropertyName("personalityReason")]
    public string? PersonalityReason { get; init; }

    [JsonPropertyName("meaningScore")]
    public int? MeaningScore { get; init; }

    [JsonPropertyName("meaningReason")]
    public string? MeaningReason { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("unavailableReason")]
    public string? UnavailableReason { get; init; }

    /// <summary>
    /// Combined score (average of personality + meaning, 1–5 scale).
    /// </summary>
    [JsonIgnore]
    public double? CombinedScore =>
        PersonalityScore.HasValue && MeaningScore.HasValue
            ? (PersonalityScore.Value + MeaningScore.Value) / 2.0
            : null;
}
