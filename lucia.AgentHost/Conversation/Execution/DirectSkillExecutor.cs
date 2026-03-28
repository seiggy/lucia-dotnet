using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

using lucia.AgentHost.Conversation.Models;
using lucia.AgentHost.Conversation.Tracing;
using lucia.Agents.Abstractions;
using lucia.Agents.CommandTracing;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Services;
using lucia.Agents.Skills;
using lucia.Wyoming.CommandRouting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

namespace lucia.AgentHost.Conversation.Execution;

/// <summary>
/// Executes matched command routes by calling skill methods directly, bypassing LLM processing.
/// Entity resolution uses exact-match lookups against the in-memory cache only.
/// If the cache is not loaded or no exact match is found, execution bails immediately
/// so the orchestrator can handle the request via LLM.
/// </summary>
public sealed partial class DirectSkillExecutor : IDirectSkillExecutor
{
    /// <summary>Default comfort temperature adjustment in degrees Fahrenheit.</summary>
    private const double DefaultComfortAdjustmentF = 3.0;

    private static readonly IReadOnlyList<string> LightDomains = ["light", "switch"];
    private static readonly IReadOnlyList<string> ClimateDomains = ["climate"];
    private static readonly IReadOnlyList<string> SceneDomains = ["scene"];
    private const string UseCascadingResolverFeature = "UseCascadingResolver";

    private readonly IServiceProvider _serviceProvider;
    private readonly IEntityLocationService _entityLocationService;
    private readonly ICascadingEntityResolver _cascadingEntityResolver;
    private readonly IFeatureManager _featureManager;
    private readonly ILogger<DirectSkillExecutor> _logger;

    public DirectSkillExecutor(
        IServiceProvider serviceProvider,
        IEntityLocationService entityLocationService,
        ICascadingEntityResolver cascadingEntityResolver,
        IFeatureManager featureManager,
        ILogger<DirectSkillExecutor> logger)
    {
        _serviceProvider = serviceProvider;
        _entityLocationService = entityLocationService;
        _cascadingEntityResolver = cascadingEntityResolver;
        _featureManager = featureManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SkillExecutionResult> ExecuteAsync(
        CommandRouteResult route, ConversationContext context, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        if (!route.IsMatch || route.MatchedPattern is null)
        {
            return SkillExecutionResult.Failed("unknown", "unknown", "No matched pattern in route", sw.Elapsed);
        }

        var pattern = route.MatchedPattern;
        var captures = route.CapturedValues ?? new Dictionary<string, string>();

        var useCascadingResolver = await _featureManager
            .IsEnabledAsync(UseCascadingResolverFeature)
            .ConfigureAwait(false);

        if (!useCascadingResolver && !_entityLocationService.IsCacheReady)
        {
            LogCacheMiss(pattern.SkillId, pattern.Action);
            Activity.Current?.SetTag("fast_path_bail_reason", "cache_miss");
            return SkillExecutionResult.Bail(
                pattern.SkillId, pattern.Action,
                "cache_miss",
                "Entity location cache not loaded; deferring to orchestrator",
                sw.Elapsed);
        }

        LogSkillDispatch(pattern.SkillId, pattern.Action);

        try
        {
            var collector = new ToolCallCollector();
            var response = await DispatchAsync(
                pattern.SkillId, pattern.Action, route, context, collector, useCascadingResolver, ct)
                .ConfigureAwait(false);

            sw.Stop();
            LogSkillSuccess(pattern.SkillId, pattern.Action, sw.ElapsedMilliseconds);

            return new SkillExecutionResult
            {
                Success = true,
                SkillId = pattern.SkillId,
                Action = pattern.Action,
                Captures = captures,
                ResponseText = response,
                ExecutionDuration = sw.Elapsed,
                ToolCalls = collector.ToolCalls,
            };
        }
        catch (EntityResolutionBailException ex)
        {
            sw.Stop();
            LogEntityResolutionBail(pattern.SkillId, pattern.Action, ex.BailReason);
            Activity.Current?.SetTag("fast_path_bail_reason", ex.BailReason);
            return SkillExecutionResult.Bail(
                pattern.SkillId, pattern.Action,
                ex.BailReason, ex.Message, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogSkillFailure(pattern.SkillId, pattern.Action, ex);
            Activity.Current?.SetTag("fast_path_bail_reason", "execution_error");
            return SkillExecutionResult.Bail(
                pattern.SkillId, pattern.Action,
                "execution_error", ex.Message, sw.Elapsed);
        }
    }

    private Task<string> DispatchAsync(
        string skillId, string action, CommandRouteResult route,
        ConversationContext context, ToolCallCollector collector, bool useCascadingResolver, CancellationToken ct) =>
        (skillId, action) switch
        {
            ("LightControlSkill", "toggle") => ExecuteLightToggleAsync(route, context, collector, useCascadingResolver, ct),
            ("LightControlSkill", "brightness") => ExecuteLightBrightnessAsync(route, context, collector, useCascadingResolver, ct),
            ("ClimateControlSkill", "set_temperature") => ExecuteClimateSetTemperatureAsync(route, context, collector, useCascadingResolver, ct),
            ("ClimateControlSkill", "adjust") => ExecuteClimateAdjustAsync(route, context, collector, useCascadingResolver, ct),
            ("SceneControlSkill", "activate") => ExecuteSceneActivateAsync(route, context, collector, useCascadingResolver, ct),
            _ => throw new NotSupportedException(
                $"No executor registered for skill '{skillId}' action '{action}'"),
        };

    private async Task<string> ExecuteLightToggleAsync(
        CommandRouteResult route, ConversationContext context, ToolCallCollector collector, bool useCascadingResolver, CancellationToken ct)
    {
        var skill = _serviceProvider.GetRequiredService<LightControlSkill>();
        var captures = route.CapturedValues ?? new Dictionary<string, string>();

        var state = captures.GetValueOrDefault("action", "on");
        var resolvedIds = useCascadingResolver
            ? ResolveEntitiesWithCascade(route, context, LightDomains, requireSingle: false, ct)
            : ResolveSearchTermsToEntityIds(BuildSearchTerms(route, context), LightDomains);

        return await collector.RecordAsync(
            nameof(LightControlSkill.ControlLightsAsync),
            new { searchTerms = resolvedIds, state, preResolved = useCascadingResolver },
            () => skill.ControlLightsAsync(resolvedIds, state, preResolved: useCascadingResolver)).ConfigureAwait(false);
    }

    private async Task<string> ExecuteLightBrightnessAsync(
        CommandRouteResult route, ConversationContext context, ToolCallCollector collector, bool useCascadingResolver, CancellationToken ct)
    {
        var skill = _serviceProvider.GetRequiredService<LightControlSkill>();
        var captures = route.CapturedValues ?? new Dictionary<string, string>();

        if (!captures.TryGetValue("value", out var valueStr) ||
            !int.TryParse(valueStr, CultureInfo.InvariantCulture, out var brightness))
        {
            throw new InvalidOperationException(
                "Brightness value not captured or not a valid integer");
        }

        var resolvedIds = useCascadingResolver
            ? ResolveEntitiesWithCascade(route, context, LightDomains, requireSingle: false, ct)
            : ResolveSearchTermsToEntityIds(BuildSearchTerms(route, context), LightDomains);

        return await collector.RecordAsync(
            nameof(LightControlSkill.ControlLightsAsync),
            new { searchTerms = resolvedIds, state = "on", brightness, preResolved = useCascadingResolver },
            () => skill.ControlLightsAsync(resolvedIds, "on", brightness, preResolved: useCascadingResolver)).ConfigureAwait(false);
    }

    private async Task<string> ExecuteClimateSetTemperatureAsync(
        CommandRouteResult route, ConversationContext context, ToolCallCollector collector, bool useCascadingResolver, CancellationToken ct)
    {
        var skill = _serviceProvider.GetRequiredService<ClimateControlSkill>();
        var captures = route.CapturedValues ?? new Dictionary<string, string>();

        if (!captures.TryGetValue("value", out var tempStr) ||
            !double.TryParse(tempStr, CultureInfo.InvariantCulture, out var temperature))
        {
            throw new InvalidOperationException(
                "Temperature value not captured or not a valid number");
        }

        var entityId = useCascadingResolver
            ? ResolveSingleEntityWithCascade(route, context, ClimateDomains, ct)
            : ResolveEntityIdFromCache(route, ClimateDomains);

        return await collector.RecordAsync(
            nameof(ClimateControlSkill.SetClimateTemperatureAsync),
            new { entityId, temperature },
            () => skill.SetClimateTemperatureAsync(entityId, temperature)).ConfigureAwait(false);
    }

    private async Task<string> ExecuteClimateAdjustAsync(
        CommandRouteResult route, ConversationContext context, ToolCallCollector collector, bool useCascadingResolver, CancellationToken ct)
    {
        var skill = _serviceProvider.GetRequiredService<ClimateControlSkill>();
        var captures = route.CapturedValues ?? new Dictionary<string, string>();

        var direction = captures.GetValueOrDefault("action", "warmer");
        var entityId = useCascadingResolver
            ? ResolveSingleEntityWithCascade(route, context, ClimateDomains, ct)
            : ResolveEntityIdFromCache(route, ClimateDomains);

        var stateInfo = await collector.RecordAsync(
            nameof(ClimateControlSkill.GetClimateStateAsync),
            new { entityId },
            () => skill.GetClimateStateAsync(entityId)).ConfigureAwait(false);

        var currentTemp = ExtractTemperature(stateInfo);

        if (currentTemp is null)
        {
            throw new InvalidOperationException(
                $"Could not determine current temperature for '{entityId}' to apply comfort adjustment");
        }

        var adjustment = direction switch
        {
            "cooler" or "colder" => -DefaultComfortAdjustmentF,
            _ => DefaultComfortAdjustmentF,
        };

        var newTemp = currentTemp.Value + adjustment;

        return await collector.RecordAsync(
            nameof(ClimateControlSkill.SetClimateTemperatureAsync),
            new { entityId, temperature = newTemp },
            () => skill.SetClimateTemperatureAsync(entityId, newTemp)).ConfigureAwait(false);
    }

    private async Task<string> ExecuteSceneActivateAsync(
        CommandRouteResult route,
        ConversationContext context,
        ToolCallCollector collector,
        bool useCascadingResolver,
        CancellationToken ct)
    {
        var skill = _serviceProvider.GetRequiredService<SceneControlSkill>();
        var entityId = useCascadingResolver
            ? ResolveSingleEntityWithCascade(route, context, SceneDomains, ct)
            : ResolveEntityIdFromCache(route, SceneDomains, captureKey: "scene");

        return await collector.RecordAsync(
            nameof(SceneControlSkill.ActivateSceneAsync),
            new { entityId },
            () => skill.ActivateSceneAsync(entityId, ct)).ConfigureAwait(false);
    }

    // ── Entity resolution helpers ────────────────────────────────────

    private string[] ResolveEntitiesWithCascade(
        CommandRouteResult route,
        ConversationContext context,
        IReadOnlyList<string> domains,
        bool requireSingle,
        CancellationToken ct)
    {
        var query = ResolveCascadeQuery(route);
        var result = _cascadingEntityResolver.Resolve(
            query,
            context.DeviceArea,
            context.SpeakerId,
            domains,
            ct);

        if (!result.IsResolved)
        {
            var reason = result.BailReason?.ToString() ?? "cascade_bail";
            var explanation = result.Explanation ?? "Cascading resolver bailed; deferring to orchestrator";
            throw new EntityResolutionBailException(reason, explanation);
        }

        if (result.ResolvedEntityIds.Count == 0)
        {
            throw new EntityResolutionBailException(
                BailReason.NoMatch.ToString(),
                "Cascading resolver returned no entity IDs");
        }

        if (requireSingle && result.ResolvedEntityIds.Count != 1)
        {
            throw new EntityResolutionBailException(
                BailReason.Ambiguous.ToString(),
                "Multiple entities resolved for single-target command");
        }

        return result.ResolvedEntityIds.ToArray();
    }

    private string ResolveSingleEntityWithCascade(
        CommandRouteResult route,
        ConversationContext context,
        IReadOnlyList<string> domains,
        CancellationToken ct)
    {
        var resolvedIds = ResolveEntitiesWithCascade(route, context, domains, requireSingle: true, ct);
        return resolvedIds[0];
    }

    private static string ResolveCascadeQuery(CommandRouteResult route)
    {
        if (!string.IsNullOrWhiteSpace(route.NormalizedTranscript))
            return route.NormalizedTranscript!;

        if (route.CapturedValues?.TryGetValue("entity", out var entity) == true
            && !string.IsNullOrWhiteSpace(entity))
        {
            return entity;
        }

        if (route.CapturedValues?.TryGetValue("scene", out var scene) == true
            && !string.IsNullOrWhiteSpace(scene))
        {
            return scene;
        }

        return route.MatchedTemplate ?? string.Empty;
    }

    /// <summary>
    /// Resolves each search term to cached entity IDs via exact match.
    /// Throws <see cref="EntityResolutionBailException"/> if any term has no match.
    /// </summary>
    private string[] ResolveSearchTermsToEntityIds(string[] searchTerms, IReadOnlyList<string> domainFilter)
    {
        var entityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var term in searchTerms)
        {
            var matches = _entityLocationService.ExactMatchEntities(term, domainFilter);
            if (matches.Count == 0)
            {
                throw new EntityResolutionBailException(
                    "no_exact_match",
                    $"No exact cache match for '{term}'; deferring to orchestrator");
            }

            foreach (var entity in matches)
            {
                entityIds.Add(entity.EntityId);
            }
        }

        return [.. entityIds];
    }

    /// <summary>
    /// Resolves a single entity ID from the route's pre-resolved value or captured text,
    /// validating against the cache via exact match.
    /// </summary>
    private string ResolveEntityIdFromCache(
        CommandRouteResult route, IReadOnlyList<string> domainFilter, string captureKey = "entity")
    {
        // Prefer pre-resolved entity ID if available and valid in cache
        if (!string.IsNullOrWhiteSpace(route.ResolvedEntityId))
        {
            var directMatches = _entityLocationService.ExactMatchEntities(route.ResolvedEntityId, domainFilter);
            if (directMatches.Count > 0)
                return directMatches[0].EntityId;
        }

        // Fall back to captured text and resolve from cache
        if (route.CapturedValues?.TryGetValue(captureKey, out var captured) == true
            && !string.IsNullOrWhiteSpace(captured))
        {
            var capturedMatches = _entityLocationService.ExactMatchEntities(captured, domainFilter);
            if (capturedMatches.Count > 0)
                return capturedMatches[0].EntityId;

            throw new EntityResolutionBailException(
                "no_exact_match",
                $"No exact cache match for captured '{captureKey}' value '{captured}'; deferring to orchestrator");
        }

        // Fall back to area context
        var areaName = route.ResolvedAreaId ?? route.CapturedValues?.GetValueOrDefault("entity");
        if (!string.IsNullOrWhiteSpace(areaName))
        {
            var areaMatches = _entityLocationService.ExactMatchEntities(areaName, domainFilter);
            if (areaMatches.Count > 0)
                return areaMatches[0].EntityId;
        }

        throw new EntityResolutionBailException(
            "no_exact_match",
            $"No resolved entity ID or '{captureKey}' capture with exact cache match");
    }

    /// <summary>
    /// Builds search terms from captured values and context for skills that accept free-text search.
    /// </summary>
    private static string[] BuildSearchTerms(CommandRouteResult route, ConversationContext context)
    {
        var terms = new List<string>();
        var captures = route.CapturedValues;

        if (captures?.TryGetValue("entity", out var entity) == true
            && !string.IsNullOrWhiteSpace(entity))
        {
            terms.Add(entity);
        }

        if (terms.Count == 0 && !string.IsNullOrWhiteSpace(route.ResolvedAreaId))
        {
            terms.Add(route.ResolvedAreaId);
        }

        if (terms.Count == 0 && !string.IsNullOrWhiteSpace(context.DeviceArea))
        {
            terms.Add(context.DeviceArea);
        }

        return terms.Count > 0
            ? terms.ToArray()
            : throw new EntityResolutionBailException(
                "no_exact_match",
                "No entity name or area context available to identify the target device");
    }

    /// <summary>
    /// Best-effort extraction of a temperature value from a skill's formatted state string.
    /// </summary>
    private static double? ExtractTemperature(string stateInfo)
    {
        var match = TemperaturePattern().Match(stateInfo);
        return match.Success
            && double.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var temp)
                ? temp
                : null;
    }

    [GeneratedRegex(
        @"temp(?:erature)?\D*?(\d+\.?\d*)",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TemperaturePattern();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Dispatching direct skill execution: {SkillId}/{Action}")]
    private partial void LogSkillDispatch(string skillId, string action);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Skill execution succeeded: {SkillId}/{Action} in {ElapsedMs}ms")]
    private partial void LogSkillSuccess(string skillId, string action, long elapsedMs);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Skill execution failed: {SkillId}/{Action}")]
    private partial void LogSkillFailure(string skillId, string action, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Fast-path bail (cache miss): {SkillId}/{Action} — deferring to orchestrator")]
    private partial void LogCacheMiss(string skillId, string action);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Fast-path bail ({BailReason}): {SkillId}/{Action} — deferring to orchestrator")]
    private partial void LogEntityResolutionBail(string skillId, string action, string bailReason);
}
