namespace lucia.MusicAgent;

/// <summary>
/// Configurable options for <see cref="MusicPlaybackSkill"/>.
/// Stored in MongoDB configuration under the <c>MusicPlaybackSkill</c> section
/// and hot-reloaded via <see cref="lucia.Agents.Configuration.MongoConfigurationProvider"/>.
/// </summary>
public sealed class MusicPlaybackSkillOptions
{
    public const string SectionName = "MusicPlaybackSkill";

    /// <summary>
    /// Minimum hybrid similarity score (0–1) for a media player entity to be
    /// included in search results.
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
    public double EmbeddingResolutionMargin { get; set; } = 0.30;

    /// <summary>
    /// How often the entity cache is refreshed from Home Assistant, in minutes.
    /// </summary>
    public int CacheRefreshMinutes { get; set; } = 30;

    /// <summary>
    /// The Home Assistant entity domains this skill operates on.
    /// </summary>
    public List<string> EntityDomains { get; set; } = ["media_player"];
}
