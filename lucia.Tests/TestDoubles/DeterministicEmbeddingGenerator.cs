using Microsoft.Extensions.AI;

namespace lucia.Tests.TestDoubles;

/// <summary>
/// Embedding generator that produces deterministic vectors from text using
/// bag-of-words hashing.  Strings that share words will have high cosine
/// similarity — e.g. "kitchen light" ↔ "Kitchen Lights light" ≈ 0.82 —
/// which is sufficient for the semantic-search paths in agent skills.
/// No external API calls are made.
/// </summary>
internal sealed class DeterministicEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int Dimensions = 128;

    public EmbeddingGeneratorMetadata Metadata { get; } = new("deterministic-bag-of-words");

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var embeddings = values.Select(v => GenerateEmbedding(v)).ToList();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    /// <summary>
    /// Extension-method–style single-value helper used by skill code via
    /// <c>_embeddingService.GenerateAsync(text)</c>.
    /// </summary>
    private static Embedding<float> GenerateEmbedding(string text)
    {
        var vector = new float[Dimensions];

        // Tokenize: lowercase, split on non-alphanumeric, deduplicate
        var tokens = text
            .ToLowerInvariant()
            .Split([' ', '_', '-', '.', '\'', '\u2019', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(Stem)
            .Distinct()
            .ToList();

        foreach (var token in tokens)
        {
            // Hash each token into multiple dimension indices for spread
            var hash1 = StableHash(token, seed: 0);
            var hash2 = StableHash(token, seed: 31);
            var hash3 = StableHash(token, seed: 97);

            vector[Math.Abs(hash1) % Dimensions] += 1.0f;
            vector[Math.Abs(hash2) % Dimensions] += 0.5f;
            vector[Math.Abs(hash3) % Dimensions] += 0.25f;
        }

        // L2-normalize so cosine similarity = dot product
        var magnitude = MathF.Sqrt(vector.Sum(v => v * v));
        if (magnitude > 0f)
        {
            for (var i = 0; i < Dimensions; i++)
                vector[i] /= magnitude;
        }

        return new Embedding<float>(vector);
    }

    /// <summary>
    /// Minimal stemmer: strips common English suffixes so "lights" matches "light",
    /// "bedroom" stays "bedroom", etc.
    /// </summary>
    private static string Stem(string word)
    {
        if (word.Length > 4 && word.EndsWith('s') && !word.EndsWith("ss"))
            word = word[..^1];
        if (word.Length > 5 && word.EndsWith("ing"))
            word = word[..^3];
        if (word.Length > 4 && word.EndsWith("ed"))
            word = word[..^2];
        return word;
    }

    /// <summary>
    /// Deterministic hash that is stable across runs (unlike <see cref="string.GetHashCode"/>).
    /// FNV-1a variant with an optional seed.
    /// </summary>
    private static int StableHash(string text, int seed)
    {
        unchecked
        {
            var hash = 2166136261u ^ (uint)seed;
            foreach (var c in text)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return (int)hash;
        }
    }
}
