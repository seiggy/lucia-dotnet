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
            _logger.LogDebug("Command routing disabled, falling back to LLM");
            return Task.FromResult(CommandRouteResult.NoMatch(TimeSpan.Zero));
        }

        var result = _matcher.Match(transcript);

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
                "No fast-path match for '{Transcript}', routing to LLM (match took {Duration}ms)",
                transcript, result.MatchDuration.TotalMilliseconds);
        }

        return Task.FromResult(result);
    }
}
