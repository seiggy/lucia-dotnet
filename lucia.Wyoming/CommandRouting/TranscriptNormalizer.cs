using System.Text.RegularExpressions;

namespace lucia.Wyoming.CommandRouting;

/// <summary>
/// Normalizes transcribed text for command pattern matching.
/// Removes filler words, applies STT error corrections, normalizes case and whitespace.
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

    /// <summary>
    /// Common STT transcription errors mapped to their intended words.
    /// Applied as whole-word replacements after lowercasing.
    /// </summary>
    private static readonly KeyValuePair<string, string>[] SttCorrections =
    [
        // "of" → "off" when preceded by "turn" (most common STT error)
        // Handled as a phrase correction below
    ];

    /// <summary>
    /// Phrase-level STT corrections applied before tokenization.
    /// Order matters — more specific phrases should come first.
    /// </summary>
    private static readonly (string Pattern, string Replacement)[] PhraseCorrections =
    [
        ("turn of ", "turn off "),
        ("shut of ", "shut off "),
        ("trun ", "turn "),
        ("tern ", "turn "),
    ];

    /// <summary>
    /// Single-word STT corrections applied after tokenization.
    /// </summary>
    private static readonly Dictionary<string, string> WordCorrections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lite"] = "light",
        ["lites"] = "lights",
        ["seen"] = "scene",
        ["seene"] = "scene",
        ["termstat"] = "thermostat",
        ["thermastat"] = "thermostat",
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

        // Apply phrase-level STT corrections before word filtering
        foreach (var (pattern, replacement) in PhraseCorrections)
        {
            result = result.Replace(pattern, replacement, StringComparison.Ordinal);
        }

        foreach (var phrase in MultiWordFillers)
        {
            result = result.Replace(phrase, " ", StringComparison.Ordinal);
        }

        var words = result.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        words = words
            .Where(w => !SingleWordFillers.Contains(w))
            .Select(w => WordCorrections.TryGetValue(w, out var corrected) ? corrected : w)
            .ToArray();

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
