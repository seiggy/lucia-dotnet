using System.Text.RegularExpressions;

namespace lucia.Wyoming.CommandRouting;

/// <summary>
/// Normalizes transcribed text for command pattern matching.
/// Removes filler words, normalizes case and whitespace.
/// </summary>
public static partial class TranscriptNormalizer
{
    private static readonly HashSet<string> SingleWordFillers = new(StringComparer.OrdinalIgnoreCase)
    {
        "um", "uh", "er", "ah", "like", "basically",
        "actually", "well", "so", "okay", "please", "kindly",
    };

    private static readonly string[] MultiWordFillers =
    [
        "you know", "thank you", "thanks",
    ];

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

        foreach (var phrase in MultiWordFillers)
        {
            result = result.Replace(phrase, " ", StringComparison.Ordinal);
        }

        var words = result.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        words = words.Where(w => !SingleWordFillers.Contains(w)).ToArray();

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
