using System.Collections.Concurrent;
using System.Diagnostics;

namespace lucia.Wyoming.CommandRouting;

public sealed class CommandPatternMatcher(CommandPatternRegistry registry)
{
    private readonly ConcurrentDictionary<string, Segment[]> _templateCache = new(StringComparer.Ordinal);

    public CommandRouteResult Match(string transcript)
    {
        var startedAt = Stopwatch.GetTimestamp();

        if (string.IsNullOrWhiteSpace(transcript))
        {
            return CommandRouteResult.NoMatch(Stopwatch.GetElapsedTime(startedAt));
        }

        var normalizedTranscript = TranscriptNormalizer.Normalize(transcript);
        var tokens = TranscriptNormalizer.Tokenize(normalizedTranscript);
        if (tokens.Length is 0)
        {
            return CommandRouteResult.NoMatch(Stopwatch.GetElapsedTime(startedAt));
        }

        // Bail immediately when the transcript contains words that signal a complex
        // intent the fast-path cannot safely handle (temporal scheduling, color
        // control, or multi-step conjunctions). These ALWAYS fall to LLM.
        if (ContainsBailSignalTokens(tokens))
        {
            return CommandRouteResult.NoMatch(Stopwatch.GetElapsedTime(startedAt));
        }

        CommandPattern? matchedPattern = null;
        Dictionary<string, string>? capturedValues = null;
        string? bestTemplate = null;
        var bestConfidence = 0f;
        var bestSpecificity = 0;
        var bestPriority = int.MinValue;

        foreach (var pattern in registry.GetAllPatterns())
        {
            foreach (var template in pattern.Templates)
            {
                var (matched, captures, confidence) = TryMatchTemplate(template, tokens);
                if (!matched || confidence < pattern.MinConfidence)
                {
                    continue;
                }

                var specificity = GetTemplateSpecificity(template);
                if (!IsBetterMatch(confidence, specificity, pattern.Priority, bestConfidence, bestSpecificity, bestPriority))
                {
                    continue;
                }

                // If a light-control pattern matched but the captured entity/area
                // contains a non-light device keyword (fan, ac, heater, etc.),
                // skip it — the LLM should handle non-light devices.
                if (pattern.SkillId == "LightControlSkill" && CapturesContainNonLightDevice(captures))
                {
                    continue;
                }

                matchedPattern = pattern;
                capturedValues = captures;
                bestTemplate = template;
                bestConfidence = confidence;
                bestSpecificity = specificity;
                bestPriority = pattern.Priority;
            }
        }

        var duration = Stopwatch.GetElapsedTime(startedAt);
        if (matchedPattern is null)
        {
            return CommandRouteResult.NoMatch(duration);
        }

        return new CommandRouteResult
        {
            IsMatch = true,
            Confidence = bestConfidence,
            MatchedPattern = matchedPattern,
            CapturedValues = capturedValues,
            MatchDuration = duration,
            MatchedTemplate = bestTemplate,
            NormalizedTranscript = normalizedTranscript,
        };
    }

    public (bool matched, Dictionary<string, string> captures, float confidence) TryMatchTemplate(
        string template,
        IReadOnlyList<string> transcriptTokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        ArgumentNullException.ThrowIfNull(transcriptTokens);

        var segments = _templateCache.GetOrAdd(template, ParseTemplate);
        var match = MatchSegments(
            segments,
            transcriptTokens,
            segmentIndex: 0,
            tokenIndex: 0,
            captures: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            constrainedCaptureMatches: 0);

        if (!match.Matched)
        {
            return (false, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), 0f);
        }

        return (true, match.Captures, CalculateConfidence(transcriptTokens.Count - match.TokenIndex, match.ConstrainedCaptureMatches));
    }

    private static bool IsBetterMatch(
        float candidateConfidence,
        int candidateSpecificity,
        int candidatePriority,
        float bestConfidence,
        int bestSpecificity,
        int bestPriority)
    {
        if (candidateConfidence > bestConfidence)
        {
            return true;
        }

        if (candidateConfidence < bestConfidence)
        {
            return false;
        }

        if (candidatePriority > bestPriority)
        {
            return true;
        }

        if (candidatePriority < bestPriority)
        {
            return false;
        }

        return candidateSpecificity > bestSpecificity;
    }

    private static int GetTemplateSpecificity(string template) => ParseTemplate(template)
        .Count(static segment => segment.Kind is SegmentKind.Literal or SegmentKind.ConstrainedCapture);

    private static Segment[] ParseTemplate(string template)
    {
        var parts = SplitTemplate(template);
        var segments = new List<Segment>(parts.Count);

        foreach (var part in parts)
        {
            if (part.Length < 2)
            {
                continue;
            }

            if (part[0] is '{' && part[^1] is '}')
            {
                var inner = part[1..^1];
                var separatorIndex = inner.IndexOf(':');
                if (separatorIndex < 0)
                {
                    segments.Add(new Segment(
                        Kind: SegmentKind.Capture,
                        Name: inner,
                        Tokens: [],
                        Alternatives: []));
                    continue;
                }

                var name = inner[..separatorIndex];
                var alternatives = ParseAlternatives(inner[(separatorIndex + 1)..]);
                segments.Add(new Segment(
                    Kind: SegmentKind.ConstrainedCapture,
                    Name: name,
                    Tokens: [],
                    Alternatives: alternatives));
                continue;
            }

            if (part[0] is '[' && part[^1] is ']')
            {
                var inner = part[1..^1];
                var alternatives = ParseAlternatives(inner);
                if (alternatives.Length is 1)
                {
                    segments.Add(new Segment(
                        Kind: SegmentKind.OptionalLiteral,
                        Name: null,
                        Tokens: alternatives[0],
                        Alternatives: []));
                    continue;
                }

                segments.Add(new Segment(
                    Kind: SegmentKind.OptionalAlternatives,
                    Name: null,
                    Tokens: [],
                    Alternatives: alternatives));
                continue;
            }

            segments.Add(new Segment(
                Kind: SegmentKind.Literal,
                Name: null,
                Tokens: NormalizeTokens(part),
                Alternatives: []));
        }

        return [.. segments];
    }

    private static List<string> SplitTemplate(string template)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var braceDepth = 0;
        var bracketDepth = 0;

        foreach (var character in template)
        {
            switch (character)
            {
                case '{':
                    braceDepth++;
                    current.Append(character);
                    break;
                case '}':
                    braceDepth--;
                    current.Append(character);
                    break;
                case '[':
                    bracketDepth++;
                    current.Append(character);
                    break;
                case ']':
                    bracketDepth--;
                    current.Append(character);
                    break;
                default:
                    if (char.IsWhiteSpace(character) && braceDepth is 0 && bracketDepth is 0)
                    {
                        if (current.Length > 0)
                        {
                            parts.Add(current.ToString());
                            current.Clear();
                        }

                        break;
                    }

                    current.Append(character);
                    break;
            }
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    private static string[][] ParseAlternatives(string value) => value
        .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(NormalizeTokens)
        .Where(static tokens => tokens.Length > 0)
        .ToArray();

    private static string[] NormalizeTokens(string value) => TranscriptNormalizer.Tokenize(TranscriptNormalizer.Normalize(value));

    private MatchState MatchSegments(
        IReadOnlyList<Segment> segments,
        IReadOnlyList<string> transcriptTokens,
        int segmentIndex,
        int tokenIndex,
        Dictionary<string, string> captures,
        int constrainedCaptureMatches)
    {
        if (segmentIndex >= segments.Count)
        {
            return new MatchState(
                Matched: true,
                TokenIndex: tokenIndex,
                Captures: captures,
                ConstrainedCaptureMatches: constrainedCaptureMatches);
        }

        var segment = segments[segmentIndex];
        return segment.Kind switch
        {
            SegmentKind.Literal => MatchLiteral(segment, segments, transcriptTokens, segmentIndex, tokenIndex, captures, constrainedCaptureMatches),
            SegmentKind.OptionalLiteral => ChooseBetterMatch(
                MatchSegments(segments, transcriptTokens, segmentIndex + 1, tokenIndex, CloneCaptures(captures), constrainedCaptureMatches),
                MatchOptionalLiteral(segment, segments, transcriptTokens, segmentIndex, tokenIndex, captures, constrainedCaptureMatches),
                transcriptTokens.Count),
            SegmentKind.OptionalAlternatives => MatchOptionalAlternatives(segment, segments, transcriptTokens, segmentIndex, tokenIndex, captures, constrainedCaptureMatches),
            SegmentKind.Capture => MatchCapture(segment, segments, transcriptTokens, segmentIndex, tokenIndex, captures, constrainedCaptureMatches),
            SegmentKind.ConstrainedCapture => MatchConstrainedCapture(segment, segments, transcriptTokens, segmentIndex, tokenIndex, captures, constrainedCaptureMatches),
            _ => default,
        };
    }

    private MatchState MatchLiteral(
        Segment segment,
        IReadOnlyList<Segment> segments,
        IReadOnlyList<string> transcriptTokens,
        int segmentIndex,
        int tokenIndex,
        Dictionary<string, string> captures,
        int constrainedCaptureMatches)
    {
        if (!MatchesTokens(transcriptTokens, tokenIndex, segment.Tokens))
        {
            return default;
        }

        return MatchSegments(
            segments,
            transcriptTokens,
            segmentIndex + 1,
            tokenIndex + segment.Tokens.Length,
            CloneCaptures(captures),
            constrainedCaptureMatches);
    }

    private MatchState MatchOptionalLiteral(
        Segment segment,
        IReadOnlyList<Segment> segments,
        IReadOnlyList<string> transcriptTokens,
        int segmentIndex,
        int tokenIndex,
        Dictionary<string, string> captures,
        int constrainedCaptureMatches)
    {
        if (!MatchesTokens(transcriptTokens, tokenIndex, segment.Tokens))
        {
            return default;
        }

        return MatchSegments(
            segments,
            transcriptTokens,
            segmentIndex + 1,
            tokenIndex + segment.Tokens.Length,
            CloneCaptures(captures),
            constrainedCaptureMatches);
    }

    private MatchState MatchOptionalAlternatives(
        Segment segment,
        IReadOnlyList<Segment> segments,
        IReadOnlyList<string> transcriptTokens,
        int segmentIndex,
        int tokenIndex,
        Dictionary<string, string> captures,
        int constrainedCaptureMatches)
    {
        var bestMatch = MatchSegments(
            segments,
            transcriptTokens,
            segmentIndex + 1,
            tokenIndex,
            CloneCaptures(captures),
            constrainedCaptureMatches);

        foreach (var alternative in segment.Alternatives)
        {
            if (!MatchesTokens(transcriptTokens, tokenIndex, alternative))
            {
                continue;
            }

            var candidate = MatchSegments(
                segments,
                transcriptTokens,
                segmentIndex + 1,
                tokenIndex + alternative.Length,
                CloneCaptures(captures),
                constrainedCaptureMatches);

            bestMatch = ChooseBetterMatch(bestMatch, candidate, transcriptTokens.Count);
        }

        return bestMatch;
    }

    private MatchState MatchConstrainedCapture(
        Segment segment,
        IReadOnlyList<Segment> segments,
        IReadOnlyList<string> transcriptTokens,
        int segmentIndex,
        int tokenIndex,
        Dictionary<string, string> captures,
        int constrainedCaptureMatches)
    {
        var bestMatch = default(MatchState);

        foreach (var alternative in segment.Alternatives.OrderByDescending(static option => option.Length))
        {
            if (!MatchesTokens(transcriptTokens, tokenIndex, alternative))
            {
                continue;
            }

            var nextCaptures = CloneCaptures(captures);
            nextCaptures[segment.Name!] = string.Join(' ', alternative);

            var candidate = MatchSegments(
                segments,
                transcriptTokens,
                segmentIndex + 1,
                tokenIndex + alternative.Length,
                nextCaptures,
                constrainedCaptureMatches + 1);

            bestMatch = ChooseBetterMatch(bestMatch, candidate, transcriptTokens.Count);
        }

        return bestMatch;
    }

    private MatchState MatchCapture(
        Segment segment,
        IReadOnlyList<Segment> segments,
        IReadOnlyList<string> transcriptTokens,
        int segmentIndex,
        int tokenIndex,
        Dictionary<string, string> captures,
        int constrainedCaptureMatches)
    {
        var minimumRemainingTokens = GetMinimumRequiredTokens(segments, segmentIndex + 1);
        var maximumExclusive = transcriptTokens.Count - minimumRemainingTokens;
        if (maximumExclusive <= tokenIndex)
        {
            return default;
        }

        var bestMatch = default(MatchState);
        for (var captureEnd = tokenIndex + 1; captureEnd <= maximumExclusive; captureEnd++)
        {
            var nextCaptures = CloneCaptures(captures);
            nextCaptures[segment.Name!] = string.Join(' ', transcriptTokens.Skip(tokenIndex).Take(captureEnd - tokenIndex));

            var candidate = MatchSegments(
                segments,
                transcriptTokens,
                segmentIndex + 1,
                captureEnd,
                nextCaptures,
                constrainedCaptureMatches);

            bestMatch = ChooseBetterMatch(bestMatch, candidate, transcriptTokens.Count);
        }

        return bestMatch;
    }

    private static int GetMinimumRequiredTokens(IReadOnlyList<Segment> segments, int startIndex)
    {
        var total = 0;

        for (var index = startIndex; index < segments.Count; index++)
        {
            total += segments[index].Kind switch
            {
                SegmentKind.OptionalLiteral or SegmentKind.OptionalAlternatives => 0,
                SegmentKind.Capture => 1,
                SegmentKind.ConstrainedCapture => segments[index].Alternatives.Min(static option => option.Length),
                _ => segments[index].Tokens.Length,
            };
        }

        return total;
    }

    private static bool MatchesTokens(IReadOnlyList<string> transcriptTokens, int tokenIndex, IReadOnlyList<string> expectedTokens)
    {
        if (expectedTokens.Count is 0 || tokenIndex + expectedTokens.Count > transcriptTokens.Count)
        {
            return false;
        }

        for (var index = 0; index < expectedTokens.Count; index++)
        {
            if (!string.Equals(transcriptTokens[tokenIndex + index], expectedTokens[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static MatchState ChooseBetterMatch(MatchState current, MatchState candidate, int totalTokens)
    {
        if (!candidate.Matched)
        {
            return current;
        }

        if (!current.Matched)
        {
            return candidate;
        }

        var currentConfidence = CalculateConfidence(totalTokens - current.TokenIndex, current.ConstrainedCaptureMatches);
        var candidateConfidence = CalculateConfidence(totalTokens - candidate.TokenIndex, candidate.ConstrainedCaptureMatches);

        if (candidateConfidence > currentConfidence)
        {
            return candidate;
        }

        if (candidateConfidence < currentConfidence)
        {
            return current;
        }

        if (candidate.TokenIndex > current.TokenIndex)
        {
            return candidate;
        }

        if (candidate.TokenIndex < current.TokenIndex)
        {
            return current;
        }

        return candidate.Captures.Count >= current.Captures.Count ? candidate : current;
    }

    private static float CalculateConfidence(int leftoverTokens, int constrainedCaptureMatches)
    {
        var confidence = 0.5f;
        if (constrainedCaptureMatches > 0)
        {
            confidence += 0.3f;
        }

        confidence += leftoverTokens is 0
            ? 0.1f
            : -0.05f * leftoverTokens;

        return Math.Clamp(confidence, 0f, 1f);
    }

    private static Dictionary<string, string> CloneCaptures(Dictionary<string, string> captures) =>
        new(captures, StringComparer.OrdinalIgnoreCase);

    // ── Bail signal detection ─────────────────────────────────────

    /// <summary>
    /// Tokens that signal the intent is too complex for the fast-path.
    /// Temporal words, color names, and multi-step conjunctions all indicate
    /// the user wants scheduling, color control, or chained actions that
    /// only the LLM orchestrator can handle correctly.
    /// </summary>
    private static readonly HashSet<string> BailSignalTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        // Temporal (unambiguous — always bail)
        "when", "after",
        "minutes", "minute", "hours", "hour", "seconds", "second",
        "tomorrow", "tonight", "later", "timer",
        // Color
        "red", "blue", "green", "warm", "cool", "color",
        // Multi-step conjunctions
        "and", "then", "also",
    };

    /// <summary>
    /// Temporal prepositions that only signal scheduling when followed by a
    /// number or duration word. "in the kitchen" is a location; "in 5 minutes"
    /// is a time delay. We check the next token to disambiguate.
    /// </summary>
    private static readonly HashSet<string> TemporalPrepositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "in", "at",
    };

    private static readonly HashSet<string> DurationFollowers = new(StringComparer.OrdinalIgnoreCase)
    {
        "minutes", "minute", "min", "mins",
        "hours", "hour", "hr", "hrs",
        "seconds", "second", "sec", "secs",
    };

    /// <summary>
    /// Device keywords that indicate a non-light entity. When these appear in a
    /// captured <c>{entity}</c> or <c>{area}</c> value inside a LightControlSkill
    /// match, the fast-path bails so the LLM can route to the correct skill.
    /// </summary>
    private static readonly HashSet<string> NonLightDeviceTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "fan", "fans",
        "ac", "aircon",
        "heater", "heating",
        "thermostat",
        "blinds", "blind", "shades", "shade", "curtain", "curtains",
        "lock", "locks",
        "door", "garage",
        "speaker", "speakers",
        "tv", "television",
        "camera", "cameras",
        "vacuum",
        "humidifier", "dehumidifier",
        "purifier",
    };

    private static bool ContainsBailSignalTokens(IReadOnlyList<string> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (BailSignalTokens.Contains(token))
                return true;

            // "in"/"at" only bail when followed by a number, duration word, or
            // combined time token like "7pm"/"10am", not "in the kitchen".
            if (TemporalPrepositions.Contains(token) && i + 1 < tokens.Count)
            {
                var next = tokens[i + 1];
                if (int.TryParse(next, out _) || DurationFollowers.Contains(next) || StartsWithDigit(next))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when any captured value (entity, area, etc.) contains a
    /// token that identifies a non-light device. This prevents the light fast-path
    /// from swallowing commands meant for fans, ACs, locks, and similar devices.
    /// </summary>
    private static bool CapturesContainNonLightDevice(Dictionary<string, string> captures)
    {
        foreach (var (_, value) in captures)
        {
            var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                if (NonLightDeviceTokens.Contains(word))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when the token begins with a digit, catching combined
    /// time tokens like "7pm", "10am", "5min" that <see cref="int.TryParse"/> misses.
    /// </summary>
    private static bool StartsWithDigit(string token) =>
        token.Length > 0 && char.IsAsciiDigit(token[0]);

    private enum SegmentKind
    {
        Literal,
        OptionalLiteral,
        OptionalAlternatives,
        Capture,
        ConstrainedCapture,
    }

    private readonly record struct Segment(
        SegmentKind Kind,
        string? Name,
        string[] Tokens,
        string[][] Alternatives);

    private readonly record struct MatchState(
        bool Matched,
        int TokenIndex,
        Dictionary<string, string> Captures,
        int ConstrainedCaptureMatches);
}
