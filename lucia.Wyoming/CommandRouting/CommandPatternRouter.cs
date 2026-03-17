using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.CommandRouting;

/// <summary>
/// Routes transcribed text: high-confidence pattern match → fast-path, else → LLM fallback.
/// </summary>
public sealed class CommandPatternRouter : ICommandRouter
{
    private readonly CommandPatternMatcher _matcher;
    private readonly CommandRoutingOptions _options;
    private readonly ILogger<CommandPatternRouter> _logger;

    public bool FallbackToLlmEnabled => _options.FallbackToLlm;

    public CommandPatternRouter(
        CommandPatternMatcher matcher,
        IOptions<CommandRoutingOptions> options,
        ILogger<CommandPatternRouter> logger)
    {
        _matcher = matcher;
        _options = options.Value;
        _logger = logger;
    }

    public Task<CommandRouteResult> RouteAsync(string transcript, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Command routing disabled");
            return Task.FromResult(CommandRouteResult.NoMatch(TimeSpan.Zero));
        }

        var result = _matcher.Match(transcript);

        if (result.IsMatch && result.Confidence < _options.ConfidenceThreshold)
        {
            _logger.LogDebug(
                "Match for '{Transcript}' below global threshold ({Confidence:F2} < {Threshold:F2}), treating as no match",
                transcript,
                result.Confidence,
                _options.ConfidenceThreshold);
            result = CommandRouteResult.NoMatch(result.MatchDuration);
        }

        if (!result.IsMatch && !_options.FallbackToLlm)
        {
            _logger.LogDebug("No match and FallbackToLlm=false, returning no-match without LLM fallback");
        }

        if (result.IsMatch)
        {
            _logger.LogInformation(
                "Fast-path match: '{Transcript}' → {SkillId}.{Action} (confidence: {Confidence:F2}, {Duration}ms)",
                transcript, result.MatchedPattern!.SkillId, result.MatchedPattern.Action,
                result.Confidence, result.MatchDuration.TotalMilliseconds);
        }
        else
        {
            _logger.LogDebug(
                "No fast-path match for '{Transcript}' (match took {Duration}ms)",
                transcript, result.MatchDuration.TotalMilliseconds);
        }

        return Task.FromResult(result);
    }
}
