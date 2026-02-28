namespace lucia.Agents.Configuration;

/// <summary>
/// Configurable options for <see cref="Skills.LightControlSkill"/>.
/// Stored in MongoDB configuration under the <c>LightControlSkill</c> section
/// and hot-reloaded via <see cref="MongoConfigurationProvider"/>.
/// </summary>
public sealed class LightControlSkillOptions
{
    public const string SectionName = "LightControlSkill";

    /// <summary>
    /// Minimum hybrid similarity score (0–1) for a light entity to be included
    /// in search results. The hybrid score blends embedding cosine similarity
    /// with string-level (Levenshtein / token-core / phonetic) similarity to
    /// reduce false positives from shared generic terms like "light".
    /// </summary>
    public double HybridSimilarityThreshold { get; set; } = 0.55;

    /// <summary>
    /// Weight applied to the embedding cosine similarity component of the
    /// hybrid score. The string similarity weight is <c>1 − EmbeddingWeight</c>.
    /// </summary>
    public double EmbeddingWeight { get; set; } = 0.4;

    /// <summary>
    /// After sorting matches by score, only keep results whose score is at
    /// least this fraction of the top match's score. For example 0.80 means
    /// a result must score ≥ 80 % of the best match to be included.
    /// This eliminates "long-tail" false positives that pass the absolute
    /// threshold but are clearly worse than the best match.
    /// Set to 0 to disable relative filtering.
    /// </summary>
    public double ScoreDropoffRatio { get; set; } = 0.80;

    /// <summary>
    /// How often the light entity cache is refreshed from Home Assistant, in minutes.
    /// </summary>
    public int CacheRefreshMinutes { get; set; } = 30;
}
