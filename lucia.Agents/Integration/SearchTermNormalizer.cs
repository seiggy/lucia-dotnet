namespace lucia.Agents.Integration;

/// <summary>
/// Normalizes search terms for cache-key deduplication.
/// Applies to-lower, article stripping, and basic stemming/singularization
/// so that semantically equivalent queries ("the kitchen lights", "kitchen light")
/// share the same cache entry.
/// </summary>
public static class SearchTermNormalizer
{
    private static readonly HashSet<string> Articles = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an"
    };

    /// <summary>
    /// Produces a stable, normalized form of a search term suitable for use
    /// as an LRU cache key. Does NOT strip domain words (light, lamp, floor, etc.)
    /// — only articles are removed. Each remaining token is stemmed/singularized.
    /// </summary>
    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var normalized = input
            .Replace('\u2019', ' ')
            .Replace('\'', ' ')
            .Replace('-', ' ')
            .Replace('_', ' ');

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Span<char> buffer = stackalloc char[256];
        var pos = 0;

        foreach (var token in tokens)
        {
            if (Articles.Contains(token))
                continue;

            var stemmed = StemToken(token.ToLowerInvariant());
            if (stemmed.Length < 2)
                continue;

            if (pos > 0 && pos < buffer.Length)
                buffer[pos++] = ' ';

            var toCopy = Math.Min(stemmed.Length, buffer.Length - pos);
            stemmed.AsSpan(0, toCopy).CopyTo(buffer[pos..]);
            pos += toCopy;
        }

        return new string(buffer[..pos]);
    }

    /// <summary>
    /// Applies basic English stemming/singularization rules to a single
    /// lowercase token. This is intentionally conservative — we only handle
    /// the most common suffixes to avoid mangling proper nouns or domain terms.
    /// </summary>
    internal static string StemToken(string token)
    {
        if (token.Length <= 3)
            return token;

        // -ies → -y (e.g. "entries" → "entry", "batteries" → "battery")
        if (token.EndsWith("ies", StringComparison.Ordinal) && token.Length > 4)
            return string.Concat(token.AsSpan(0, token.Length - 3), "y");

        // -ves → -f (e.g. "shelves" → "shelf")
        if (token.EndsWith("ves", StringComparison.Ordinal) && token.Length > 4)
            return string.Concat(token.AsSpan(0, token.Length - 3), "f");

        // -sses → -ss (e.g. "addresses" → "address")
        if (token.EndsWith("sses", StringComparison.Ordinal) && token.Length > 5)
            return token[..^2];

        // -ches, -shes, -xes, -zes → drop -es (e.g. "switches" → "switch")
        if (token.EndsWith("es", StringComparison.Ordinal) && token.Length > 4)
        {
            var stem = token[..^2];
            if (stem.EndsWith("ch", StringComparison.Ordinal) ||
                stem.EndsWith("sh", StringComparison.Ordinal) ||
                stem.EndsWith("x", StringComparison.Ordinal) ||
                stem.EndsWith("z", StringComparison.Ordinal) ||
                stem.EndsWith("ss", StringComparison.Ordinal))
                return stem;
        }

        // -ing → drop (e.g. "running" → "runn" — imperfect but good for cache keys)
        if (token.EndsWith("ing", StringComparison.Ordinal) && token.Length > 5)
            return token[..^3];

        // -ed → drop (e.g. "turned" → "turn")
        if (token.EndsWith("ed", StringComparison.Ordinal) && token.Length > 4 &&
            !token.EndsWith("eed", StringComparison.Ordinal))
            return token[..^2];

        // Simple plural -s (but not -ss, -us, -is)
        if (token.EndsWith('s') && !token.EndsWith("ss", StringComparison.Ordinal) &&
            !token.EndsWith("us", StringComparison.Ordinal) &&
            !token.EndsWith("is", StringComparison.Ordinal))
            return token[..^1];

        return token;
    }
}
