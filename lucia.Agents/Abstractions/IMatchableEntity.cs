using lucia.Agents.Integration;
using lucia.Agents.Services;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Abstractions;

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

    /// <summary>
    /// Alternative names for this entity (e.g. area aliases, entity aliases).
    /// The <see cref="IHybridEntityMatcher"/> scores each alias and takes the
    /// maximum score across <see cref="MatchableName"/> + aliases.
    /// </summary>
    IReadOnlyList<string> MatchableAliases => [];

    /// <summary>
    /// Pre-computed Metaphone phonetic keys for each alias in <see cref="MatchableAliases"/>.
    /// Parallel array — <c>AliasPhoneticKeys[i]</c> corresponds to <c>MatchableAliases[i]</c>.
    /// Built once at cache time via <see cref="StringSimilarity.BuildPhoneticKeys"/>.
    /// </summary>
    IReadOnlyList<string[]> AliasPhoneticKeys => [];
}
