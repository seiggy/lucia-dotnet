using System.Text.RegularExpressions;

namespace lucia.AgentHost.Conversation;

/// <summary>
/// Extracts an optional speaker identification tag from the beginning of a transcript.
/// The Wyoming voice platform prefixes transcripts with <c>&lt;SpeakerName /&gt;</c>
/// when speaker verification identifies the user.
/// </summary>
public static partial class SpeakerTagParser
{
    /// <summary>
    /// Strips a leading <c>&lt;Name /&gt;</c> tag from <paramref name="text"/>,
    /// returning the speaker name and the remaining clean text.
    /// Returns <c>null</c> speaker when no tag is present.
    /// </summary>
    public static (string? SpeakerId, string CleanText) Parse(string text)
    {
        var match = SpeakerTagPattern().Match(text);
        if (!match.Success)
            return (null, text);

        var speakerId = match.Groups[1].Value.Trim();
        var cleanText = text[match.Length..].TrimStart();
        return (speakerId, cleanText);
    }

    // Matches: <Name /> at the start of string, allowing whitespace variations
    [GeneratedRegex(@"^<\s*([^/<>]+?)\s*/>", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex SpeakerTagPattern();
}
