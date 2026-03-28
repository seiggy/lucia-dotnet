using System.Text.RegularExpressions;
using lucia.Agents.Models;

namespace lucia.Agents.Services;

internal sealed partial class QueryDecomposer
{
    private static readonly HashSet<string> ActionTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "turn", "switch", "set", "toggle", "dim", "brighten", "increase", "decrease"
    };

    private static readonly HashSet<string> ActionValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "on", "off", "toggle", "dim", "brighten", "set", "increase", "decrease"
    };

    private static readonly HashSet<string> DeviceTypeTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "light", "lights", "lamp", "lamps", "switch", "switches",
        "fan", "fans", "thermostat", "climate", "ac", "heater",
        "music", "speaker", "speakers", "media", "scene", "scenes"
    };

    private static readonly HashSet<string> IgnoreTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "in", "on", "of", "to", "for", "please", "my"
    };

    private static readonly HashSet<string> ConjunctionTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "then", "also"
    };

    private static readonly HashSet<string> ConditionalTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "unless", "when"
    };

    private static readonly HashSet<string> ColorTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "red", "blue", "green", "yellow", "orange", "purple", "pink", "white",
        "warm", "cool", "color", "colour"
    };

    public static QueryIntent Decompose(string userQuery, string? speakerId)
    {
        var normalized = Normalize(userQuery);
        var tokens = Tokenize(normalized);
        var hasMy = tokens.Contains("my", StringComparer.OrdinalIgnoreCase);

        var action = ExtractAction(tokens);
        var deviceType = ExtractDeviceType(tokens);
        var explicitLocation = ExtractExplicitLocation(tokens, deviceType);

        var (isComplex, complexityReason) = DetectComplexity(tokens, normalized);

        var candidateAreas = BuildCandidateAreas(explicitLocation, hasMy, speakerId);
        var candidateEntities = BuildCandidateEntities(tokens, explicitLocation, deviceType, hasMy, speakerId);

        return new QueryIntent
        {
            NormalizedQuery = normalized,
            Action = action,
            ExplicitLocation = explicitLocation,
            DeviceType = deviceType,
            IsComplex = isComplex,
            ComplexityReason = complexityReason,
            CandidateAreaNames = candidateAreas,
            CandidateEntityNames = candidateEntities
        };
    }

    private static string? ExtractAction(IReadOnlyList<string> tokens)
    {
        foreach (var token in tokens)
        {
            if (ActionValues.Contains(token))
                return token.ToLowerInvariant();
        }

        if (tokens.Any(t => string.Equals(t, "turn", StringComparison.OrdinalIgnoreCase)))
            return "turn";

        if (tokens.Any(t => string.Equals(t, "switch", StringComparison.OrdinalIgnoreCase)))
            return "switch";

        if (tokens.Any(t => string.Equals(t, "set", StringComparison.OrdinalIgnoreCase)))
            return "set";

        return null;
    }

    private static string? ExtractDeviceType(IReadOnlyList<string> tokens)
    {
        foreach (var token in tokens)
        {
            if (DeviceTypeTokens.Contains(token))
                return token.ToLowerInvariant();
        }

        return null;
    }

    private static string? ExtractExplicitLocation(IReadOnlyList<string> tokens, string? deviceType)
    {
        if (tokens.Count == 0)
            return null;

        var deviceTypeIndex = deviceType is null
            ? -1
            : IndexOf(tokens, deviceType);

        var inIndex = IndexOf(tokens, "in");
        if (inIndex >= 0 && inIndex < tokens.Count - 1)
        {
            var start = inIndex + 1;
            if (start < tokens.Count && (tokens[start] is "the" or "my"))
                start++;

            return BuildPhrase(tokens, start, tokens.Count - 1, deviceType);
        }

        if (deviceTypeIndex > 0)
        {
            var phrase = BuildPhrase(tokens, 0, deviceTypeIndex - 1, deviceType);
            if (!string.IsNullOrWhiteSpace(phrase))
                return phrase;
        }

        var myIndex = IndexOf(tokens, "my");
        if (myIndex >= 0 && myIndex < tokens.Count - 1)
        {
            var phrase = BuildPhrase(tokens, myIndex + 1, tokens.Count - 1, deviceType);
            if (!string.IsNullOrWhiteSpace(phrase))
                return phrase;
        }

        var theIndex = IndexOf(tokens, "the");
        if (theIndex >= 0 && theIndex < tokens.Count - 1)
        {
            var phrase = BuildPhrase(tokens, theIndex + 1, tokens.Count - 1, deviceType);
            if (!string.IsNullOrWhiteSpace(phrase))
                return phrase;
        }

        return null;
    }

    private static (bool IsComplex, string? Reason) DetectComplexity(IReadOnlyList<string> tokens, string normalized)
    {
        if (TemporalRegex().IsMatch(normalized))
            return (true, "temporal");

        if (tokens.Any(t => ConjunctionTokens.Contains(t)))
            return (true, "conjunction");

        if (tokens.Any(t => ConditionalTokens.Contains(t)))
            return (true, "conditional");

        if (tokens.Any(t => ColorTokens.Contains(t)))
            return (true, "color");

        return (false, null);
    }

    private static IReadOnlyList<string> BuildCandidateAreas(string? explicitLocation, bool hasMy, string? speakerId)
    {
        if (string.IsNullOrWhiteSpace(explicitLocation))
            return [];

        var candidates = new List<string> { explicitLocation };
        if (hasMy && !string.IsNullOrWhiteSpace(speakerId))
        {
            candidates.Add($"{speakerId}'s {explicitLocation}");
            candidates.Add($"{speakerId} {explicitLocation}");
        }

        return candidates;
    }

    private static IReadOnlyList<string> BuildCandidateEntities(
        IReadOnlyList<string> tokens,
        string? explicitLocation,
        string? deviceType,
        bool hasMy,
        string? speakerId)
    {
        var candidates = new List<string>();
        var explicitTokens = explicitLocation is null
            ? []
            : explicitLocation.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var remaining = tokens
            .Where(t => !ActionTokens.Contains(t))
            .Where(t => !IgnoreTokens.Contains(t))
            .Where(t => explicitTokens.Length == 0 || !explicitTokens.Contains(t, StringComparer.OrdinalIgnoreCase))
            .Where(t => deviceType is null || !string.Equals(t, deviceType, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (remaining.Length > 0)
            candidates.Add(string.Join(' ', remaining));

        if (string.IsNullOrWhiteSpace(deviceType))
            return candidates;

        if (candidates.Count == 0)
            candidates.Add(deviceType);

        if (hasMy && !string.IsNullOrWhiteSpace(speakerId))
        {
            candidates.Add($"{speakerId}'s {deviceType}");
            candidates.Add($"{speakerId} {deviceType}");
        }

        return candidates;
    }

    private static string? BuildPhrase(IReadOnlyList<string> tokens, int start, int end, string? deviceType)
    {
        if (start < 0 || end < start || start >= tokens.Count)
            return null;

        var phraseTokens = new List<string>();
        for (var i = start; i <= end && i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (IgnoreTokens.Contains(token))
                continue;

            if (deviceType is not null && string.Equals(token, deviceType, StringComparison.OrdinalIgnoreCase))
                break;

            if (ActionTokens.Contains(token))
                continue;

            phraseTokens.Add(token);
        }

        return phraseTokens.Count == 0
            ? null
            : string.Join(' ', phraseTokens);
    }

    private static int IndexOf(IReadOnlyList<string> tokens, string value)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (string.Equals(tokens[i], value, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static string Normalize(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return string.Empty;

        var result = transcript.ToLowerInvariant().Trim();
        result = Punctuation().Replace(result, "");
        result = MultipleSpaces().Replace(result, " ").Trim();
        return result;
    }

    private static string[] Tokenize(string normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
            return [];

        return normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpaces();

    [GeneratedRegex(@"[^\w\s]")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\b(in|after|before)\s+\d+|\b\d+\s*(seconds?|minutes?|hours?)\b|\bat\s+\d+|\btomorrow\b|\btonight\b",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex TemporalRegex();
}
