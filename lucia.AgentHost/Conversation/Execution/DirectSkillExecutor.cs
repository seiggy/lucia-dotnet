using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

using lucia.AgentHost.Conversation.Models;
using lucia.AgentHost.Conversation.Tracing;
using lucia.Agents.CommandTracing;
using lucia.Agents.Skills;
using lucia.Wyoming.CommandRouting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace lucia.AgentHost.Conversation.Execution;

/// <summary>
/// Executes matched command routes by calling skill methods directly, bypassing LLM processing.
/// Dispatches to <see cref="LightControlSkill"/>, <see cref="ClimateControlSkill"/>, or
/// <see cref="SceneControlSkill"/> based on the <see cref="CommandPattern.SkillId"/> and
/// <see cref="CommandPattern.Action"/> from the matched route.
/// </summary>
public sealed partial class DirectSkillExecutor : IDirectSkillExecutor
{
    /// <summary>Default comfort temperature adjustment in degrees Fahrenheit.</summary>
    private const double DefaultComfortAdjustmentF = 3.0;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DirectSkillExecutor> _logger;

    public DirectSkillExecutor(IServiceProvider serviceProvider, ILogger<DirectSkillExecutor> logger)
    {
        _serviceProvider = serviceProvider;
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

        LogSkillDispatch(pattern.SkillId, pattern.Action);

        try
        {
            var collector = new ToolCallCollector();
            var response = await DispatchAsync(
                pattern.SkillId, pattern.Action, route, context, collector, ct).ConfigureAwait(false);

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
        catch (Exception ex)
        {
            sw.Stop();
            LogSkillFailure(pattern.SkillId, pattern.Action, ex);
            return SkillExecutionResult.Failed(pattern.SkillId, pattern.Action, ex.Message, sw.Elapsed);
        }
    }

    private Task<string> DispatchAsync(
        string skillId, string action, CommandRouteResult route,
        ConversationContext context, ToolCallCollector collector, CancellationToken ct) =>
        (skillId, action) switch
        {
            ("LightControlSkill", "toggle") => ExecuteLightToggleAsync(route, context, collector),
            ("LightControlSkill", "brightness") => ExecuteLightBrightnessAsync(route, context, collector),
            ("ClimateControlSkill", "set_temperature") => ExecuteClimateSetTemperatureAsync(route, context, collector),
            ("ClimateControlSkill", "adjust") => ExecuteClimateAdjustAsync(route, context, collector),
            ("SceneControlSkill", "activate") => ExecuteSceneActivateAsync(route, context, collector, ct),
            _ => throw new NotSupportedException(
                $"No executor registered for skill '{skillId}' action '{action}'"),
        };

    private async Task<string> ExecuteLightToggleAsync(
        CommandRouteResult route, ConversationContext context, ToolCallCollector collector)
    {
        var skill = _serviceProvider.GetRequiredService<LightControlSkill>();
        var captures = route.CapturedValues ?? new Dictionary<string, string>();

        var state = captures.GetValueOrDefault("action", "on");
        var searchTerms = BuildSearchTerms(route, context);

        return await collector.RecordAsync(
            nameof(LightControlSkill.ControlLightsAsync),
            new { searchTerms, state },
            () => skill.ControlLightsAsync(searchTerms, state)).ConfigureAwait(false);
    }

    private async Task<string> ExecuteLightBrightnessAsync(
        CommandRouteResult route, ConversationContext context, ToolCallCollector collector)
    {
        var skill = _serviceProvider.GetRequiredService<LightControlSkill>();
        var captures = route.CapturedValues ?? new Dictionary<string, string>();

        if (!captures.TryGetValue("value", out var valueStr) ||
            !int.TryParse(valueStr, CultureInfo.InvariantCulture, out var brightness))
        {
            throw new InvalidOperationException(
                "Brightness value not captured or not a valid integer");
        }

        var searchTerms = BuildSearchTerms(route, context);

        return await collector.RecordAsync(
            nameof(LightControlSkill.ControlLightsAsync),
            new { searchTerms, state = "on", brightness },
            () => skill.ControlLightsAsync(searchTerms, "on", brightness)).ConfigureAwait(false);
    }

    private async Task<string> ExecuteClimateSetTemperatureAsync(
        CommandRouteResult route, ConversationContext context, ToolCallCollector collector)
    {
        var skill = _serviceProvider.GetRequiredService<ClimateControlSkill>();
        var captures = route.CapturedValues ?? new Dictionary<string, string>();

        if (!captures.TryGetValue("value", out var tempStr) ||
            !double.TryParse(tempStr, CultureInfo.InvariantCulture, out var temperature))
        {
            throw new InvalidOperationException(
                "Temperature value not captured or not a valid number");
        }

        var entityId = ResolveEntityId(route);

        return await collector.RecordAsync(
            nameof(ClimateControlSkill.SetClimateTemperatureAsync),
            new { entityId, temperature },
            () => skill.SetClimateTemperatureAsync(entityId, temperature)).ConfigureAwait(false);
    }

    private async Task<string> ExecuteClimateAdjustAsync(
        CommandRouteResult route, ConversationContext context, ToolCallCollector collector)
    {
        var skill = _serviceProvider.GetRequiredService<ClimateControlSkill>();
        var captures = route.CapturedValues ?? new Dictionary<string, string>();

        var direction = captures.GetValueOrDefault("action", "warmer");
        var entityId = ResolveEntityId(route);

        // Fetch current state to determine the baseline temperature
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
        CommandRouteResult route, ConversationContext context, ToolCallCollector collector, CancellationToken ct)
    {
        var skill = _serviceProvider.GetRequiredService<SceneControlSkill>();
        var entityId = ResolveEntityId(route, captureKey: "scene");

        return await collector.RecordAsync(
            nameof(SceneControlSkill.ActivateSceneAsync),
            new { entityId },
            () => skill.ActivateSceneAsync(entityId, ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds search terms from captured values and context for skills that accept free-text search
    /// (e.g., <see cref="LightControlSkill.ControlLightsAsync"/>).
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

        // Fall back to area context when no explicit entity was captured
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
            : throw new InvalidOperationException(
                "No entity name or area context available to identify the target device");
    }

    /// <summary>
    /// Resolves the Home Assistant entity ID from the route's pre-resolved value or captured text.
    /// </summary>
    private static string ResolveEntityId(CommandRouteResult route, string captureKey = "entity")
    {
        if (!string.IsNullOrWhiteSpace(route.ResolvedEntityId))
        {
            return route.ResolvedEntityId;
        }

        if (route.CapturedValues?.TryGetValue(captureKey, out var captured) == true
            && !string.IsNullOrWhiteSpace(captured))
        {
            return captured;
        }

        throw new InvalidOperationException(
            $"No resolved entity ID or '{captureKey}' capture available. " +
            "The command router must resolve the entity ID for this action.");
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
}
