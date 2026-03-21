namespace lucia.Agents.Configuration;

/// <summary>
/// Configurable options for <see cref="Skills.SceneControlSkill"/>.
/// Stored in MongoDB configuration under the <c>SceneControlSkill</c> section
/// and hot-reloaded via <see cref="MongoConfigurationProvider"/>.
/// </summary>
public sealed class SceneControlSkillOptions
{
    public const string SectionName = "SceneControlSkill";

    /// <summary>
    /// Minimum hybrid similarity score (0–1) for a scene entity to be included
    /// in search results.
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
    /// </summary>
    public double DisagreementPenalty { get; set; } = 0.4;

    /// <summary>
    /// When multiple candidates have embedding similarities within this margin,
    /// string-level scores resolve the tie. Range 0–1.
    /// </summary>
    public double EmbeddingResolutionMargin { get; set; } = 0.10;

    /// <summary>
    /// How often the scene entity cache is refreshed from Home Assistant, in minutes.
    /// </summary>
    public int CacheRefreshMinutes { get; set; } = 30;

    /// <summary>
    /// The Home Assistant entity domains this skill operates on.
    /// </summary>
    public List<string> EntityDomains { get; set; } = ["scene"];
}
