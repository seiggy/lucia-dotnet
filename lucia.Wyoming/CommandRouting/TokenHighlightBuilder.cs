using lucia.Agents.CommandTracing;
namespace lucia.Wyoming.CommandRouting;

/// <summary>
/// Computes character-position highlights for matched tokens within the normalized transcript.
/// Called after a successful match to produce overlay data for the dashboard.
/// </summary>
public static class TokenHighlightBuilder
{
    /// <summary>
    /// Builds token highlights by re-walking the matched template against the normalized tokens.
    /// Returns character positions within the <paramref name="normalizedTranscript"/> string.
    /// </summary>
    public static IReadOnlyList<TokenHighlight> Build(
        string normalizedTranscript,
        string[] tokens,
        string template,
        IReadOnlyDictionary<string, string> capturedValues)
    {
        if (string.IsNullOrWhiteSpace(normalizedTranscript) || tokens.Length == 0)
            return [];

        var highlights = new List<TokenHighlight>();
        var segments = ParseTemplate(template);

        // Build a position map: for each token index, what is its start/end char position in normalizedTranscript
        var tokenPositions = ComputeTokenPositions(normalizedTranscript, tokens);
        if (tokenPositions.Count != tokens.Length)
            return [];

        var tokenIndex = 0;

        foreach (var segment in segments)
        {
            if (tokenIndex >= tokens.Length)
                break;

            switch (segment.Kind)
            {
                case SegmentKind.Literal:
                    tokenIndex = TryHighlightLiteral(segment, tokens, tokenIndex, tokenPositions, highlights);
                    break;

                case SegmentKind.OptionalLiteral:
                    tokenIndex = TryHighlightOptional(segment, tokens, tokenIndex, tokenPositions, highlights);
                    break;

                case SegmentKind.OptionalAlternatives:
                    tokenIndex = TryHighlightOptionalAlternatives(segment, tokens, tokenIndex, tokenPositions, highlights);
                    break;

                case SegmentKind.ConstrainedCapture:
                    tokenIndex = TryHighlightConstrainedCapture(segment, tokens, tokenIndex, tokenPositions, capturedValues, highlights);
                    break;

                case SegmentKind.Capture:
                    tokenIndex = TryHighlightCapture(segment, tokens, tokenIndex, tokenPositions, capturedValues, highlights);
                    break;
            }
        }

        return highlights;
    }

    private static int TryHighlightLiteral(
        TemplateSegment segment, string[] tokens, int tokenIndex,
        List<(int Start, int End)> positions, List<TokenHighlight> highlights)
    {
        if (tokenIndex + segment.Tokens.Length > tokens.Length)
            return tokenIndex;

        for (var i = 0; i < segment.Tokens.Length; i++)
        {
            if (!string.Equals(tokens[tokenIndex + i], segment.Tokens[i], StringComparison.OrdinalIgnoreCase))
                return tokenIndex;
        }

        for (var i = 0; i < segment.Tokens.Length; i++)
        {
            var pos = positions[tokenIndex + i];
            highlights.Add(new TokenHighlight
            {
                Start = pos.Start,
                End = pos.End,
                Type = TokenHighlightType.Literal,
                Value = segment.Tokens[i],
            });
        }

        return tokenIndex + segment.Tokens.Length;
    }

    private static int TryHighlightOptional(
        TemplateSegment segment, string[] tokens, int tokenIndex,
        List<(int Start, int End)> positions, List<TokenHighlight> highlights)
    {
        if (tokenIndex + segment.Tokens.Length > tokens.Length)
            return tokenIndex;

        for (var i = 0; i < segment.Tokens.Length; i++)
        {
            if (!string.Equals(tokens[tokenIndex + i], segment.Tokens[i], StringComparison.OrdinalIgnoreCase))
                return tokenIndex; // optional — not present, skip
        }

        for (var i = 0; i < segment.Tokens.Length; i++)
        {
            var pos = positions[tokenIndex + i];
            highlights.Add(new TokenHighlight
            {
                Start = pos.Start,
                End = pos.End,
                Type = TokenHighlightType.Optional,
                Value = segment.Tokens[i],
            });
        }

        return tokenIndex + segment.Tokens.Length;
    }

    private static int TryHighlightOptionalAlternatives(
        TemplateSegment segment, string[] tokens, int tokenIndex,
        List<(int Start, int End)> positions, List<TokenHighlight> highlights)
    {
        foreach (var alt in segment.Alternatives.OrderByDescending(a => a.Length))
        {
            if (tokenIndex + alt.Length > tokens.Length)
                continue;

            var match = true;
            for (var i = 0; i < alt.Length; i++)
            {
                if (!string.Equals(tokens[tokenIndex + i], alt[i], StringComparison.OrdinalIgnoreCase))
                {
                    match = false;
                    break;
                }
            }

            if (!match) continue;

            for (var i = 0; i < alt.Length; i++)
            {
                var pos = positions[tokenIndex + i];
                highlights.Add(new TokenHighlight
                {
                    Start = pos.Start,
                    End = pos.End,
                    Type = TokenHighlightType.Optional,
                    Value = alt[i],
                });
            }

            return tokenIndex + alt.Length;
        }

        return tokenIndex; // optional — not present
    }

    private static int TryHighlightConstrainedCapture(
        TemplateSegment segment, string[] tokens, int tokenIndex,
        List<(int Start, int End)> positions,
        IReadOnlyDictionary<string, string> capturedValues,
        List<TokenHighlight> highlights)
    {
        if (segment.Name is null || !capturedValues.TryGetValue(segment.Name, out var captured))
            return tokenIndex;

        var capturedTokens = captured.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokenIndex + capturedTokens.Length > tokens.Length)
            return tokenIndex;

        for (var i = 0; i < capturedTokens.Length; i++)
        {
            var pos = positions[tokenIndex + i];
            highlights.Add(new TokenHighlight
            {
                Start = pos.Start,
                End = pos.End,
                Type = TokenHighlightType.ConstrainedCapture,
                Value = segment.Name,
            });
        }

        return tokenIndex + capturedTokens.Length;
    }

    private static int TryHighlightCapture(
        TemplateSegment segment, string[] tokens, int tokenIndex,
        List<(int Start, int End)> positions,
        IReadOnlyDictionary<string, string> capturedValues,
        List<TokenHighlight> highlights)
    {
        if (segment.Name is null || !capturedValues.TryGetValue(segment.Name, out var captured))
            return tokenIndex;

        var capturedTokens = captured.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokenIndex + capturedTokens.Length > tokens.Length)
            return tokenIndex;

        for (var i = 0; i < capturedTokens.Length; i++)
        {
            var pos = positions[tokenIndex + i];
            highlights.Add(new TokenHighlight
            {
                Start = pos.Start,
                End = pos.End,
                Type = TokenHighlightType.Capture,
                Value = segment.Name,
            });
        }

        return tokenIndex + capturedTokens.Length;
    }

    private static List<(int Start, int End)> ComputeTokenPositions(string text, string[] tokens)
    {
        var positions = new List<(int Start, int End)>(tokens.Length);
        var searchFrom = 0;

        foreach (var token in tokens)
        {
            var idx = text.IndexOf(token, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                // Fallback: can't find token position
                positions.Add((searchFrom, searchFrom + token.Length));
                searchFrom += token.Length + 1;
            }
            else
            {
                positions.Add((idx, idx + token.Length));
                searchFrom = idx + token.Length;
            }
        }

        return positions;
    }

    private static List<TemplateSegment> ParseTemplate(string template)
    {
        var parts = SplitTemplate(template);
        var segments = new List<TemplateSegment>(parts.Count);

        foreach (var part in parts)
        {
            if (part.Length < 2) continue;

            if (part[0] == '{' && part[^1] == '}')
            {
                var inner = part[1..^1];
                var sep = inner.IndexOf(':');
                if (sep < 0)
                {
                    segments.Add(new TemplateSegment(SegmentKind.Capture, inner, [], []));
                }
                else
                {
                    var name = inner[..sep];
                    var alts = inner[(sep + 1)..].Split('|', StringSplitOptions.RemoveEmptyEntries)
                        .Select(a => NormalizeTokens(a)).ToArray();
                    segments.Add(new TemplateSegment(SegmentKind.ConstrainedCapture, name, [], alts));
                }

                continue;
            }

            if (part[0] == '[' && part[^1] == ']')
            {
                var inner = part[1..^1];
                var alts = inner.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => NormalizeTokens(a)).ToArray();
                if (alts.Length == 1)
                    segments.Add(new TemplateSegment(SegmentKind.OptionalLiteral, null, alts[0], []));
                else
                    segments.Add(new TemplateSegment(SegmentKind.OptionalAlternatives, null, [], alts));
                continue;
            }

            segments.Add(new TemplateSegment(SegmentKind.Literal, null, NormalizeTokens(part), []));
        }

        return segments;
    }

    private static List<string> SplitTemplate(string template)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var braceDepth = 0;
        var bracketDepth = 0;

        foreach (var c in template)
        {
            switch (c)
            {
                case '{': braceDepth++; current.Append(c); break;
                case '}': braceDepth--; current.Append(c); break;
                case '[': bracketDepth++; current.Append(c); break;
                case ']': bracketDepth--; current.Append(c); break;
                default:
                    if (char.IsWhiteSpace(c) && braceDepth == 0 && bracketDepth == 0)
                    {
                        if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); }
                    }
                    else
                    {
                        current.Append(c);
                    }
                    break;
            }
        }

        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }

    private static string[] NormalizeTokens(string value) =>
        TranscriptNormalizer.Tokenize(TranscriptNormalizer.Normalize(value));

    private enum SegmentKind { Literal, OptionalLiteral, OptionalAlternatives, Capture, ConstrainedCapture }

    private readonly record struct TemplateSegment(
        SegmentKind Kind, string? Name, string[] Tokens, string[][] Alternatives);
}
