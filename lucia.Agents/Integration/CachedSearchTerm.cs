using Microsoft.Extensions.AI;

namespace lucia.Agents.Integration;

/// <summary>
/// Pre-computed search data for a normalized search term.
/// </summary>
public readonly record struct CachedSearchTerm(
    Embedding<float> Embedding,
    string[] PhoneticKeys,
    string NormalizedText);

