using Microsoft.Extensions.AI;

namespace lucia.Agents.Services;

/// <summary>
/// An entity that can be matched against a search term using the
/// <see cref="IHybridEntityMatcher"/> hybrid similarity algorithm.
/// Implement this interface on any cached entity model (lights, scenes,
/// media players, etc.) to enable name-based fuzzy search with embedding,
/// string, and phonetic similarity.
/// </summary>
public interface IMatchableEntity
{
    /// <summary>
    /// The human-readable name used for similarity comparison
    /// (e.g. "Kitchen Lights light", "Zack's Light").
    /// </summary>
    string MatchableName { get; }

    /// <summary>
    /// Pre-computed embedding vector for <see cref="MatchableName"/>.
    /// Generated once at cache time by the skill's embedding provider.
    /// </summary>
    Embedding<float>? NameEmbedding { get; }

    /// <summary>
    /// Pre-computed Metaphone phonetic keys for the discriminating
    /// (non-stop-word) tokens in <see cref="MatchableName"/>.
    /// Built once at cache time via <see cref="StringSimilarity.BuildPhoneticKeys"/>.
    /// </summary>
    string[] PhoneticKeys { get; }
}
