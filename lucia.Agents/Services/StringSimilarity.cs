using System.Buffers;
using System.Text;

namespace lucia.Agents.Services;

/// <summary>
/// Lightweight string-similarity algorithms for entity-name matching.
/// Used alongside embedding cosine similarity to filter false positives
/// caused by shared generic terms (e.g. "light", "lamp").
/// </summary>
public static class StringSimilarity
{
    /// <summary>
    /// Common terms that appear in almost every light entity name.
    /// These are stripped before computing token-core similarity so that
    /// only the discriminating parts of the name are compared.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "light", "lights", "lamp", "lamps", "led", "bulb", "bulbs",
        "the", "a", "an", "in", "on", "of", "and", "my"
    };

    // ── Levenshtein ────────────────────────────────────────────────

    /// <summary>
    /// Computes the normalized Levenshtein similarity between two strings.
    /// Returns a value in [0, 1] where 1.0 means identical strings.
    /// </summary>
    public static double NormalizedLevenshtein(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        if (a.IsEmpty && b.IsEmpty) return 1.0;
        if (a.IsEmpty || b.IsEmpty) return 0.0;

        var distance = LevenshteinDistance(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return 1.0 - ((double)distance / maxLen);
    }

    // ── Token-core ─────────────────────────────────────────────────

    /// <summary>
    /// Token-core similarity: strips stop-words from both strings, then computes
    /// the best average fuzzy match between the remaining "core" tokens.
    /// Returns a value in [0, 1] where 1.0 means all core tokens match perfectly.
    /// </summary>
    public static double TokenCoreSimilarity(string a, string b)
    {
        var coreA = ExtractCoreTokens(a);
        var coreB = ExtractCoreTokens(b);

        if (coreA.Length == 0 && coreB.Length == 0) return 1.0;
        if (coreA.Length == 0 || coreB.Length == 0) return 0.0;

        // For each token in A, find the best matching token in B
        double totalSim = 0;
        foreach (var tokenA in coreA)
        {
            double bestMatch = 0;
            foreach (var tokenB in coreB)
            {
                var sim = NormalizedLevenshtein(tokenA.AsSpan(), tokenB.AsSpan());
                if (sim > bestMatch) bestMatch = sim;
            }
            totalSim += bestMatch;
        }

        // Average over the smaller set to not penalize extra context words
        return totalSim / coreA.Length;
    }

    // ── Phonetic (Metaphone) ───────────────────────────────────────

    /// <summary>
    /// Phonetic similarity: computes Metaphone keys for core tokens in both
    /// strings, then compares them using normalized Levenshtein distance.
    /// This catches STT artifacts where words sound the same but are spelled
    /// differently (e.g. "Zack" ↔ "Sack", "Kitchen" ↔ "Kitchin").
    /// Returns a value in [0, 1] where 1.0 means phonetically identical.
    /// </summary>
    public static double PhoneticSimilarity(string a, string b)
    {
        var keysA = ExtractCoreTokens(a).Select(Metaphone).Where(k => k.Length > 0).ToArray();
        var keysB = ExtractCoreTokens(b).Select(Metaphone).Where(k => k.Length > 0).ToArray();
        return PhoneticSimilarity(keysA, keysB);
    }

    /// <summary>
    /// Phonetic similarity using pre-computed Metaphone key arrays.
    /// Use this overload in hot paths where keys have already been built
    /// (e.g. cached entity keys + per-query search keys).
    /// </summary>
    public static double PhoneticSimilarity(string[] keysA, string[] keysB)
    {
        if (keysA.Length == 0 && keysB.Length == 0) return 1.0;
        if (keysA.Length == 0 || keysB.Length == 0) return 0.0;

        double totalSim = 0;
        foreach (var keyA in keysA)
        {
            double bestMatch = 0;
            foreach (var keyB in keysB)
            {
                var sim = NormalizedLevenshtein(keyA.AsSpan(), keyB.AsSpan());
                if (sim > bestMatch) bestMatch = sim;
            }
            totalSim += bestMatch;
        }

        return totalSim / keysA.Length;
    }

    /// <summary>
    /// Generates a Metaphone phonetic key for a single word. The Metaphone
    /// algorithm maps English words to a consonant-based code that represents
    /// their approximate pronunciation, ignoring spelling variations.
    /// <para>Examples: Kitchen → KTXN, Kitchin → KTXN, Zack → SK, Sack → SK,
    /// Garage → KRJ, Garaj → KRJ, Light → LT, Lite → LT.</para>
    /// </summary>
    internal static string Metaphone(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return string.Empty;

        // Strip non-alpha, uppercase
        Span<char> buf = stackalloc char[word.Length];
        var len = 0;
        foreach (var c in word)
        {
            if (char.IsLetter(c))
                buf[len++] = char.ToUpperInvariant(c);
        }
        if (len == 0) return string.Empty;

        var input = buf[..len];

        // Drop initial silent letter pairs
        if (len >= 2)
        {
            var c0 = input[0];
            var c1 = input[1];
            if ((c0 == 'A' && c1 == 'E') ||
                (c0 == 'G' && c1 == 'N') ||
                (c0 == 'K' && c1 == 'N') ||
                (c0 == 'P' && c1 == 'N') ||
                (c0 == 'W' && c1 == 'R'))
            {
                input = input[1..];
                len--;
            }
        }

        var result = new StringBuilder(8);
        var i = 0;

        while (i < len && result.Length < 8)
        {
            var c = input[i];

            // Skip duplicate adjacent letters (except C)
            if (i > 0 && c == input[i - 1] && c != 'C')
            {
                i++;
                continue;
            }

            switch (c)
            {
                case 'A' or 'E' or 'I' or 'O' or 'U':
                    // Vowels only kept at the start of the word
                    if (i == 0) result.Append(c);
                    break;

                case 'B':
                    // Silent B after M at end of word (e.g. "dumb")
                    if (i == 0 || input[i - 1] != 'M' || i + 1 < len)
                        result.Append('B');
                    break;

                case 'C':
                    if (i + 1 < len && input[i + 1] == 'H')
                    {
                        result.Append('X'); // CH → X (as in "church")
                        i++;
                    }
                    else if (i + 1 < len && input[i + 1] is 'E' or 'I' or 'Y')
                    {
                        result.Append('S'); // soft C (as in "city")
                    }
                    else
                    {
                        result.Append('K'); // hard C (as in "cat")
                    }
                    break;

                case 'D':
                    if (i + 1 < len && input[i + 1] == 'G' &&
                        i + 2 < len && input[i + 2] is 'E' or 'I' or 'Y')
                    {
                        result.Append('J'); // DGE/DGI/DGY → J
                    }
                    else
                    {
                        result.Append('T');
                    }
                    break;

                case 'F':
                    result.Append('F');
                    break;

                case 'G':
                    // Silent GH before non-vowel or at end (e.g. "light", "high")
                    if (i + 1 < len && input[i + 1] == 'H')
                    {
                        if (i + 2 < len && IsVowel(input[i + 2]))
                        {
                            result.Append('K'); // GH before vowel → K (e.g. "ghost")
                            i++;
                        }
                        else
                        {
                            i++; // silent GH (e.g. "light", "night")
                        }
                        break;
                    }
                    // Silent G before N at end (e.g. "sign")
                    if (i + 1 < len && input[i + 1] == 'N' &&
                        (i + 2 >= len || (i + 2 < len && input[i + 2] == 'E' && i + 3 >= len)))
                        break;
                    // Soft G before E, I, Y (e.g. "gem", "giant")
                    if (i + 1 < len && input[i + 1] is 'E' or 'I' or 'Y')
                        result.Append('J');
                    else
                        result.Append('K');
                    break;

                case 'H':
                    // H is sounded only before a vowel and not after CSPTG
                    if (i + 1 < len && IsVowel(input[i + 1]) &&
                        (i == 0 || input[i - 1] is not ('C' or 'S' or 'P' or 'T' or 'G')))
                        result.Append('H');
                    break;

                case 'J':
                    result.Append('J');
                    break;

                case 'K':
                    // Silent K after C (e.g. "kick" → already got K from C)
                    if (i == 0 || input[i - 1] != 'C')
                        result.Append('K');
                    break;

                case 'L':
                    result.Append('L');
                    break;

                case 'M':
                    result.Append('M');
                    break;

                case 'N':
                    result.Append('N');
                    break;

                case 'P':
                    if (i + 1 < len && input[i + 1] == 'H')
                    {
                        result.Append('F'); // PH → F
                        i++;
                    }
                    else
                    {
                        result.Append('P');
                    }
                    break;

                case 'Q':
                    result.Append('K');
                    break;

                case 'R':
                    result.Append('R');
                    break;

                case 'S':
                    if (i + 1 < len && input[i + 1] == 'H')
                    {
                        result.Append('X'); // SH → X
                        i++;
                    }
                    else if (i + 2 < len && input[i + 1] == 'I' && input[i + 2] is 'A' or 'O')
                    {
                        result.Append('X'); // SIA/SIO → X
                        i += 2;
                    }
                    else
                    {
                        result.Append('S');
                    }
                    break;

                case 'T':
                    if (i + 1 < len && input[i + 1] == 'H')
                    {
                        result.Append('0'); // TH → θ (encoded as 0)
                        i++;
                    }
                    else if (i + 2 < len && input[i + 1] == 'I' && input[i + 2] is 'A' or 'O')
                    {
                        result.Append('X'); // TIA/TIO → X
                        i += 2;
                    }
                    else
                    {
                        result.Append('T');
                    }
                    break;

                case 'V':
                    result.Append('F');
                    break;

                case 'W':
                case 'Y':
                    // W/Y only kept before a vowel
                    if (i + 1 < len && IsVowel(input[i + 1]))
                        result.Append(c);
                    break;

                case 'X':
                    result.Append("KS");
                    break;

                case 'Z':
                    result.Append('S'); // Z → S (key for "Zack" ↔ "Sack")
                    break;
            }
            i++;
        }

        return result.ToString();
    }

    private static bool IsVowel(char c) => c is 'A' or 'E' or 'I' or 'O' or 'U';

    // ── Hybrid score ───────────────────────────────────────────────

    /// <summary>
    /// Computes a hybrid similarity score that blends embedding similarity
    /// with string-level similarity. This reduces false positives from
    /// semantically similar but textually different entity names.
    /// The string-level component takes the best of Levenshtein,
    /// token-core, and phonetic (Metaphone) similarity.
    /// </summary>
    /// <param name="embeddingSimilarity">Cosine similarity from embedding vectors [0,1].</param>
    /// <param name="searchTerm">The user's search query.</param>
    /// <param name="entityName">The entity's friendly name.</param>
    /// <param name="embeddingWeight">Weight for the embedding component (0–1). String weight is 1 − this value.</param>
    /// <returns>A blended score in [0, 1].</returns>
    public static double HybridScore(double embeddingSimilarity, string searchTerm, string entityName, double embeddingWeight = 0.4)
    {
        var searchKeys = ExtractCoreTokens(searchTerm).Select(Metaphone).Where(k => k.Length > 0).ToArray();
        var entityKeys = ExtractCoreTokens(entityName).Select(Metaphone).Where(k => k.Length > 0).ToArray();
        return HybridScore(embeddingSimilarity, searchTerm, entityName, embeddingWeight, searchKeys, entityKeys);
    }

    /// <summary>
    /// Overload that accepts pre-computed Metaphone keys to avoid
    /// re-extracting and encoding tokens on every comparison.
    /// Use in hot paths where the search term keys are computed once
    /// and entity keys are pre-cached on <see cref="lucia.Agents.Models.LightEntity"/>.
    /// </summary>
    public static double HybridScore(
        double embeddingSimilarity,
        string searchTerm,
        string entityName,
        double embeddingWeight,
        string[] searchPhoneticKeys,
        string[] entityPhoneticKeys)
    {
        var levenshtein = NormalizedLevenshtein(
            searchTerm.AsSpan(),
            entityName.AsSpan());

        var tokenCore = TokenCoreSimilarity(searchTerm, entityName);
        var phonetic = PhoneticSimilarity(searchPhoneticKeys, entityPhoneticKeys);

        // String-level score: take the best of all three metrics.
        // Phonetic catches STT artifacts (Zack↔Sack, Kitchen↔Kitchin)
        // that Levenshtein and token-core may score lower on.
        var stringSimilarity = Math.Max(Math.Max(levenshtein, tokenCore), phonetic);

        var stringWeight = 1.0 - embeddingWeight;
        return (embeddingWeight * embeddingSimilarity) + (stringWeight * stringSimilarity);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Extracts tokens from a string, removing stop-words and punctuation.
    /// </summary>
    internal static string[] ExtractCoreTokens(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];

        // Normalize: replace common punctuation with spaces
        var normalized = input
            .Replace('\u2019', ' ') // right single quote (smart apostrophe)
            .Replace('\'', ' ')
            .Replace('-', ' ')
            .Replace('_', ' ');

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return tokens
            .Where(t => !StopWords.Contains(t))
            .Select(t => t.ToLowerInvariant())
            .ToArray();
    }

    /// <summary>
    /// Convenience method to build Metaphone phonetic keys for a name.
    /// Call this once at cache-build time for each entity, then pass the
    /// result to <see cref="HybridScore(double,string,string,double,string[],string[])"/>
    /// or <see cref="PhoneticSimilarity(string[],string[])"/>.
    /// </summary>
    public static string[] BuildPhoneticKeys(string name) =>
        ExtractCoreTokens(name)
            .Select(Metaphone)
            .Where(k => k.Length > 0)
            .ToArray();

    /// <summary>
    /// Computes the Levenshtein edit distance between two character spans.
    /// Uses a single-row DP approach for O(min(m,n)) memory.
    /// Case-insensitive comparison.
    /// </summary>
    private static int LevenshteinDistance(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        // Ensure a is the shorter span for memory efficiency
        if (a.Length > b.Length)
        {
            var temp = a;
            a = b;
            b = temp;
        }

        var rowLen = a.Length + 1;
        int[]? rented = null;
        Span<int> row = rowLen <= 256
            ? stackalloc int[rowLen]
            : (rented = ArrayPool<int>.Shared.Rent(rowLen)).AsSpan(0, rowLen);

        try
        {
            for (var i = 0; i < rowLen; i++)
                row[i] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var previous = row[0];
                row[0] = j;
                var bChar = char.ToLowerInvariant(b[j - 1]);

                for (var i = 1; i <= a.Length; i++)
                {
                    var aChar = char.ToLowerInvariant(a[i - 1]);
                    var cost = aChar == bChar ? 0 : 1;
                    var current = row[i];

                    row[i] = Math.Min(
                        Math.Min(row[i] + 1, row[i - 1] + 1),
                        previous + cost);

                    previous = current;
                }
            }

            return row[a.Length];
        }
        finally
        {
            if (rented is not null)
                ArrayPool<int>.Shared.Return(rented);
        }
    }
}
