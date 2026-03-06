namespace lucia.Agents.Configuration.UserConfiguration;

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
    /// Penalty applied when string-level similarity metrics disagree (0–1).
    /// Higher values penalize spread between best and mean string scores.
    /// </summary>
    public double DisagreementPenalty { get; set; } = 0.4;

    /// <summary>
    /// When multiple candidates have embedding similarities within this margin,
    /// string-level scores resolve the tie. Range 0–1.
    /// </summary>
    public double EmbeddingResolutionMargin { get; set; } = 0.30;

    /// <summary>
    /// How often the fan entity cache is refreshed from Home Assistant, in minutes.
    /// </summary>
    public int CacheRefreshMinutes { get; set; } = 5;

    /// <summary>
    /// The Home Assistant entity domains this skill operates on.
    /// Configurable so users can extend or restrict which domains the fan agent searches.
    /// </summary>
    public List<string> EntityDomains { get; set; } = ["fan"];
}
