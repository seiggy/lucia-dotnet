using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.CommandRouting;

/// <summary>
/// Routes transcribed text: high-confidence pattern match → fast-path, else → LLM fallback.
/// </summary>
public sealed class CommandPatternRouter : ICommandRouter
{
    private readonly CommandPatternMatcher _matcher;
    private readonly IOptionsMonitor<CommandRoutingOptions> _optionsMonitor;
    private readonly ILogger<CommandPatternRouter> _logger;

    public bool FallbackToLlmEnabled => _optionsMonitor.CurrentValue.FallbackToLlm;

    public CommandPatternRouter(
        CommandPatternMatcher matcher,
        IOptionsMonitor<CommandRoutingOptions> optionsMonitor,
        ILogger<CommandPatternRouter> logger)
    {
        _matcher = matcher;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public Task<CommandRouteResult> RouteAsync(string transcript, CancellationToken ct)
    {
        var options = _optionsMonitor.CurrentValue;

        if (!options.Enabled)
        {
            _logger.LogDebug("Command routing disabled");
            return Task.FromResult(CommandRouteResult.NoMatch(TimeSpan.Zero));
        }

        var result = _matcher.Match(transcript);

        if (result.IsMatch && result.Confidence < options.ConfidenceThreshold)
        {
            _logger.LogDebug(
                "Match for '{Transcript}' below global threshold ({Confidence:F2} < {Threshold:F2}), treating as no match",
                transcript,
                result.Confidence,
                options.ConfidenceThreshold);
            result = CommandRouteResult.NoMatch(result.MatchDuration);
        }

        if (!result.IsMatch && !options.FallbackToLlm)
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
