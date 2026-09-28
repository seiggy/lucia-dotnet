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
        var tokens = StripPoliteWrapper(TranscriptNormalizer.Tokenize(normalizedTranscript));
        if (tokens.Length is 0)
        {
            return CommandRouteResult.NoMatch(Stopwatch.GetElapsedTime(startedAt));
        }

        // Bail immediately when the transcript contains words that signal a complex
        // intent the fast-path cannot safely handle (yes/no status questions,
        // temporal scheduling, color control, or multi-step conjunctions).
        // These fall to the LLM/orchestrator so read-only queries never mutate state.
        if (ContainsBailSignalTokens(tokens) || transcript.Contains('?') || IsStatusQuestion(tokens))
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

                // If a light-control pattern matched but the captured entity
                // contains a non-light device keyword (fan, ac, heater, etc.),
                // skip it — the LLM should handle non-light devices. This does
                // not screen the area capture.
                if (pattern.SkillId == "LightControlSkill" && CapturesContainNonLightDevice(captures))
                {
                    continue;
                }

                // "set the office lights to 50" asks for brightness, not a thermostat setting.
                if (pattern.SkillId == "ClimateControlSkill" && EntityCaptureNamesLight(captures))
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

        return (true, match.Captures, CalculateConfidence(match.ConstrainedCaptureMatches));
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
            // Every word must belong to the template. Words left over are often the
            // condition or question that makes the utterance something other than a command.
            return tokenIndex == transcriptTokens.Count
                ? new MatchState(
                    Matched: true,
                    Captures: captures,
                    ConstrainedCaptureMatches: constrainedCaptureMatches)
                : default;
        }

        var segment = segments[segmentIndex];
        return segment.Kind switch
        {
            SegmentKind.Literal => MatchLiteral(segment, segments, transcriptTokens, segmentIndex, tokenIndex, captures, constrainedCaptureMatches),
            SegmentKind.OptionalLiteral => ChooseBetterMatch(
                MatchSegments(segments, transcriptTokens, segmentIndex + 1, tokenIndex, CloneCaptures(captures), constrainedCaptureMatches),
                MatchOptionalLiteral(segment, segments, transcriptTokens, segmentIndex, tokenIndex, captures, constrainedCaptureMatches)),
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

            bestMatch = ChooseBetterMatch(bestMatch, candidate);
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

            bestMatch = ChooseBetterMatch(bestMatch, candidate);
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
        var hasNameToken = false;
        for (var captureEnd = tokenIndex + 1; captureEnd <= maximumExclusive; captureEnd++)
        {
            var token = transcriptTokens[captureEnd - 1];

            // A capture holds a name. Once it would take in a clause word, it is swallowing a
            // question, negation or condition, and every longer capture would too.
            if (ClauseTokens.Contains(token))
            {
                break;
            }

            // "the" or "all the" alone is not a name.
            hasNameToken |= !DeterminerTokens.Contains(token);
            if (!hasNameToken)
            {
                continue;
            }

            var nextCaptures = CloneCaptures(captures);
            nextCaptures[segment.Name!] = string.Join(' ', transcriptTokens.Skip(tokenIndex).Take(captureEnd - tokenIndex));

            var candidate = MatchSegments(
                segments,
                transcriptTokens,
                segmentIndex + 1,
                captureEnd,
                nextCaptures,
                constrainedCaptureMatches);

            bestMatch = ChooseBetterMatch(bestMatch, candidate);
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

    private static MatchState ChooseBetterMatch(MatchState current, MatchState candidate)
    {
        if (!candidate.Matched)
        {
            return current;
        }

        if (!current.Matched)
        {
            return candidate;
        }

        // Complete parses of one template cover the same words, so prefer the one whose
        // optional literals explain the most of them: "dim the office lights to 20 percent"
        // captures "office lights" and "20", not "office lights to 20" and "percent".
        return CountCapturedTokens(candidate.Captures) <= CountCapturedTokens(current.Captures)
            ? candidate
            : current;
    }

    private static int CountCapturedTokens(Dictionary<string, string> captures)
    {
        var count = 0;
        foreach (var value in captures.Values)
        {
            count += value.Count(static character => character == ' ') + 1;
        }

        return count;
    }

    // Templates consume the whole utterance, so confidence only reflects whether a
    // constrained capture such as {action:on|off} anchored the match.
    private static float CalculateConfidence(int constrainedCaptureMatches) =>
        constrainedCaptureMatches > 0 ? 0.9f : 0.6f;

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
        "morning", "afternoon", "evening", "noon", "midnight", "bedtime",
        "sunrise", "sunset", "dawn", "dusk",
        // Color
        "red", "blue", "green", "warm", "cool", "color",
        // Multi-step conjunctions
        "and", "then", "also",
    };

    /// <summary>
    /// Words that never belong in a device, area, scene or value name. A free capture
    /// stops before them, so "[the] {entity} {action:on|off}" cannot read "can you tell
    /// me whether the office light is on" as a command, and "turn {action:on|off} [the]
    /// {entity}" cannot drop the condition from "turn on the lights if nobody is home".
    /// </summary>
    private static readonly HashSet<string> ClauseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        // Questions and embedded questions
        "who", "whom", "whose", "what", "which", "where", "why", "how", "whether", "if",
        // Auxiliaries and modals. "can", "will" and "may" are left out because they
        // also name things: can lights, Will's room.
        "is", "are", "am", "was", "were", "be", "been", "being",
        "do", "does", "did", "has", "have", "had",
        "could", "would", "should", "shall", "might", "must",
        // Pronouns and pointing words that need conversation context
        "i", "me", "you", "u", "we", "us", "he", "him", "she", "they", "them", "it",
        "this", "that", "these", "those",
        "someone", "somebody", "anyone", "anybody", "everyone", "everybody", "nobody",
        // Negation and cancellation. "don't" normalizes to "don t".
        "not", "no", "never", "t", "dont", "doesnt", "didnt", "cant", "wont", "isnt",
        "arent", "wasnt", "werent", "shouldnt", "wouldnt", "couldnt",
        "cancel", "nevermind", "wait",
        // Conditions, exceptions, alternatives and times
        "or", "but", "unless", "until", "till", "before", "while", "because", "since",
        "except", "than", "every", "at",
        // A second command verb means the capture took in another clause
        "turn", "turned", "turning", "switched", "switching",
    };

    private static readonly HashSet<string> DeterminerTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "all", "some", "any", "my", "our", "your", "his", "her", "their",
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
    /// captured <c>{entity}</c> value inside a LightControlSkill match, the
    /// fast-path bails so the LLM can route to the correct skill.
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

    private static bool IsStatusQuestion(IReadOnlyList<string> tokens)
    {
        return tokens[0] is "is" or "are" or "do" or "does" or "did" or "am"
            or "was" or "were" or "what" or "why" or "when" or "where" or "which"
            or "how" or "has" or "have" or "who" or "whose"
            // STT can transcribe "are the ... on" as "or the ... on".
            || (tokens.Count >= 3 && tokens[0] == "or" && tokens[1] is "the" or "my" or "our")
            // Polite "can you ..." requests are unwrapped before this check, so a leading
            // modal asks a question: "should the porch light be on".
            || tokens[0] is "can" or "could" or "would" or "will" or "should" or "shall"
                or "may" or "might" or "must";
    }

    /// <summary>
    /// Removes the polite wrapper from a request, so "can you turn off the lights for me"
    /// matches the same templates as "turn off the lights". Other leading modals stay in
    /// place for <see cref="IsStatusQuestion"/> to read as questions.
    /// </summary>
    private static string[] StripPoliteWrapper(string[] tokens)
    {
        var start = tokens.Length > 2
            && tokens[0] is "can" or "could" or "would" or "will"
            && tokens[1] is "you" or "u"
                ? 2
                : 0;
        var end = tokens.Length - start > 2 && tokens[^2] == "for" && tokens[^1] == "me"
            ? tokens.Length - 2
            : tokens.Length;

        return start == 0 && end == tokens.Length ? tokens : tokens[start..end];
    }

    private static bool EntityCaptureNamesLight(Dictionary<string, string> captures) =>
        captures.TryGetValue("entity", out var entityValue)
        && entityValue.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(LightIdentifyingTokens.Contains);

    /// <summary>
    /// Tokens that identify a light entity in a capture value.  When a capture
    /// contains both a non-light device token AND a light token (e.g. "garage
    /// lights", "fan light"), the light token wins and the match is kept.
    /// </summary>
    private static readonly HashSet<string> LightIdentifyingTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "light", "lights", "lamp", "lamps",
    };

    /// <summary>
    /// Returns <c>true</c> when the <c>{entity}</c> capture contains a token that
    /// identifies a non-light device, UNLESS a light-identifying word is also present.
    /// "garage" bails, but "garage lights" does not. Area captures are skipped because
    /// areas are locations (e.g. "lights on in the garage"), not devices.
    /// </summary>
    private static bool CapturesContainNonLightDevice(Dictionary<string, string> captures)
    {
        // Only inspect the "entity" capture — area/action captures are not device identifiers.
        if (!captures.TryGetValue("entity", out var entityValue))
            return false;

        var words = entityValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hasNonLight = false;
        var hasLight = false;

        foreach (var word in words)
        {
            if (NonLightDeviceTokens.Contains(word))
                hasNonLight = true;
            if (LightIdentifyingTokens.Contains(word))
                hasLight = true;
        }

        return hasNonLight && !hasLight;
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
        Dictionary<string, string> Captures,
        int ConstrainedCaptureMatches);
}
