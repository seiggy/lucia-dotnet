using System.Text.RegularExpressions;

namespace lucia.Wyoming.CommandRouting;

/// <summary>
/// Normalizes transcribed text for command pattern matching.
/// Removes filler words, normalizes case and whitespace.
/// </summary>
public static partial class TranscriptNormalizer
{
    private static readonly HashSet<string> FillerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "um", "uh", "er", "ah", "like", "you know", "basically",
        "actually", "well", "so", "okay", "ok",
    };

    private static readonly HashSet<string> PoliteWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "please", "thanks", "thank you", "kindly",
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpaces();

    [GeneratedRegex(@"[^\w\s]")]
    private static partial Regex Punctuation();

    /// <summary>
    /// Normalize transcript for pattern matching.
    /// </summary>
    public static string Normalize(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return string.Empty;

        var result = transcript.ToLowerInvariant().Trim();
        result = Punctuation().Replace(result, "");

        var words = result.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        words = words.Where(w => !FillerWords.Contains(w) && !PoliteWords.Contains(w)).ToArray();

        result = string.Join(' ', words);

        return result;
    }

    /// <summary>
    /// Split normalized text into tokens for matching.
    /// </summary>
    public static string[] Tokenize(string normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
            return [];

        return normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
