namespace lucia.Agents.Configuration;

/// <summary>
/// Configurable options for <see cref="Skills.FanControlSkill"/>.
/// Stored in MongoDB configuration under the <c>FanControlSkill</c> section
/// and hot-reloaded via <see cref="MongoConfigurationProvider"/>.
/// </summary>
public sealed class FanControlSkillOptions
{
    public const string SectionName = "FanControlSkill";

    /// <summary>
    /// Minimum hybrid similarity score (0–1) for a fan entity to be included
    /// in search results. The hybrid score blends embedding cosine similarity
    /// with string-level (Levenshtein / token-core / phonetic) similarity.
    /// </summary>
    public double HybridSimilarityThreshold { get; set; } = 0.55;

    /// <summary>
    /// Weight applied to the embedding cosine similarity component of the
    /// hybrid score. The string similarity weight is <c>1 − EmbeddingWeight</c>.
    /// </summary>
    public double EmbeddingWeight { get; set; } = 0.4;

    /// <summary>
    /// After sorting matches by score, only keep results whose score is at
    /// least this fraction of the top match's score. Set to 0 to disable.
    /// </summary>
    public double ScoreDropoffRatio { get; set; } = 0.80;

    /// <summary>
    /// How often the fan entity cache is refreshed from Home Assistant, in minutes.
    /// </summary>
    public int CacheRefreshMinutes { get; set; } = 5;
}
