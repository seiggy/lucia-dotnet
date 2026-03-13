using System.Diagnostics;
using lucia.Agents.Orchestration;
using lucia.Wyoming.Diarization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace lucia.Wyoming.CommandRouting;

/// <summary>
/// Dispatches commands to the Lucia orchestrator.
/// Fast-path: formats a precise command for the LLM to execute quickly.
/// Fallback: sends raw transcript with enrichment metadata.
/// </summary>
public sealed class SkillDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SkillDispatcher> _logger;

    public SkillDispatcher(
        IServiceProvider serviceProvider,
        ILogger<SkillDispatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Dispatch a fast-path matched command through the orchestrator.
    /// Formats a precise, structured command string that the LLM can execute quickly.
    /// </summary>
    public async Task<SkillDispatchResult> DispatchFastPathAsync(
        CommandRouteResult route,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(route);

        var sw = Stopwatch.StartNew();

        try
        {
            if (route is not { IsMatch: true, MatchedPattern: not null })
            {
                return SkillDispatchResult.Failed(
                    route.MatchedPattern?.SkillId ?? "unknown",
                    "Fast-path dispatch requires a matched command pattern",
                    sw.Elapsed);
            }

            var formattedCommand = FormatFastPathCommand(route);

            _logger.LogInformation(
                "Fast-path dispatch: {SkillId}.{Action} → '{Command}'",
                route.MatchedPattern.SkillId,
                route.MatchedPattern.Action,
                formattedCommand);

            var engine = ResolveLuciaEngine();
            if (engine is null)
            {
                _logger.LogError("LuciaEngine is not available for fast-path dispatch");
                return SkillDispatchResult.Failed(
                    route.MatchedPattern.SkillId,
                    "Orchestrator engine not available",
                    sw.Elapsed);
            }

            var result = await engine
                .ProcessRequestAsync(formattedCommand, cancellationToken: ct)
                .ConfigureAwait(false);

            return new SkillDispatchResult
            {
                Success = true,
                ResponseText = string.IsNullOrWhiteSpace(result.Text) ? "Done" : result.Text,
                SkillId = route.MatchedPattern.SkillId,
                ExecutionDuration = sw.Elapsed,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fast-path dispatch failed for {SkillId}", route.MatchedPattern?.SkillId);
            return SkillDispatchResult.Failed(
                route.MatchedPattern?.SkillId ?? "unknown",
                ex.Message,
                sw.Elapsed);
        }
    }

    /// <summary>
    /// Fallback to LLM orchestration with enriched context.
    /// </summary>
    public async Task<SkillDispatchResult> FallbackToLlmAsync(
        string transcript,
        CommandRouteResult? partialMatch,
        SpeakerIdentification? speaker,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var enrichedText = EnrichTranscript(transcript, partialMatch, speaker);

            _logger.LogInformation("Dispatching transcript via LLM fallback: {Transcript}", enrichedText);

            var engine = ResolveLuciaEngine();
            if (engine is null)
            {
                _logger.LogError("LuciaEngine is not available for LLM fallback dispatch");
                return SkillDispatchResult.Failed(
                    "orchestrator",
                    "Orchestrator engine not available",
                    sw.Elapsed);
            }

            var result = await engine
                .ProcessRequestAsync(enrichedText, cancellationToken: ct)
                .ConfigureAwait(false);

            return new SkillDispatchResult
            {
                Success = true,
                ResponseText = string.IsNullOrWhiteSpace(result.Text)
                    ? "I'm not sure how to help with that."
                    : result.Text,
                SkillId = "orchestrator",
                ExecutionDuration = sw.Elapsed,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM fallback failed for transcript: {Transcript}", transcript);
            return SkillDispatchResult.Failed("orchestrator", ex.Message, sw.Elapsed);
        }
    }

    private static string FormatFastPathCommand(CommandRouteResult route)
    {
        var captures = route.CapturedValues ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pattern = route.MatchedPattern!;

        return pattern.Action switch
        {
            "toggle" when captures.TryGetValue("action", out var action) =>
                $"Turn {action} {captures.GetValueOrDefault("entity", captures.GetValueOrDefault("area", "the lights"))}",
            "brightness" when captures.TryGetValue("value", out var brightness) =>
                $"Set {captures.GetValueOrDefault("entity", "the lights")} brightness to {brightness} percent",
            "set_temperature" when captures.TryGetValue("value", out var temperature) =>
                $"Set {captures.GetValueOrDefault("entity", captures.GetValueOrDefault("area", "thermostat"))} temperature to {temperature}",
            "activate" when captures.TryGetValue("scene", out var scene) =>
                $"Activate the {scene} scene",
            _ => string.Join(" ", captures.Values),
        };
    }

    private static string EnrichTranscript(
        string transcript,
        CommandRouteResult? partialMatch,
        SpeakerIdentification? speaker)
    {
        var parts = new List<string> { transcript };

        if (!string.IsNullOrWhiteSpace(speaker?.Name))
        {
            parts.Add($"[Speaker: {speaker.Name}]");
        }

        if (partialMatch is { IsMatch: false, MatchedPattern: not null })
        {
            parts.Add(
                $"[Possible intent: {partialMatch.MatchedPattern.SkillId}.{partialMatch.MatchedPattern.Action} (confidence: {partialMatch.Confidence:F2})]");
        }

        if (!string.IsNullOrWhiteSpace(partialMatch?.ResolvedEntityId))
        {
            parts.Add($"[Resolved entity: {partialMatch.ResolvedEntityId}]");
        }

        if (!string.IsNullOrWhiteSpace(partialMatch?.ResolvedAreaId))
        {
            parts.Add($"[Resolved area: {partialMatch.ResolvedAreaId}]");
        }

        return string.Join(" ", parts);
    }

    private LuciaEngine? ResolveLuciaEngine() => _serviceProvider.GetService<LuciaEngine>();
}
