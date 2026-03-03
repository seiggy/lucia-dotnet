using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using lucia.Agents.Abstractions;
using lucia.HomeAssistant.Services;
using lucia.HomeAssistant.Models;
using lucia.Agents.Models;
using lucia.Agents.Services;
using lucia.Agents.Configuration;
using lucia.Agents.Integration;
using lucia.Agents.Models.HomeAssistant;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Skills;

/// <summary>
/// Semantic Kernel plugin for Home Assistant light control with caching and similarity search
/// </summary>
public class LightControlSkill : IAgentSkill, IOptimizableSkill
{
    private readonly IHomeAssistantClient _homeAssistantClient;
    private readonly ILogger<LightControlSkill> _logger;
    private readonly IEntityLocationService _locationService;
    private readonly IOptionsMonitor<LightControlSkillOptions> _options;

    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.LightControl", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Skills.LightControl", "1.0.0");
    private static readonly Counter<long> LightStateQueries = Meter.CreateCounter<long>("light.state.queries", "{count}", "Number of light state queries.");
    private static readonly Counter<long> LightStateFailures = Meter.CreateCounter<long>("light.state.failures", "{count}", "Number of failed light state queries.");
    private static readonly Histogram<double> LightStateDurationMs = Meter.CreateHistogram<double>("light.state.duration", "ms", "Duration of light state queries.");
    private static readonly Counter<long> LightControlRequests = Meter.CreateCounter<long>("light.control.requests", "{count}", "Number of light control requests.");
    private static readonly Counter<long> LightControlFailures = Meter.CreateCounter<long>("light.control.failures", "{count}", "Number of failed light control requests.");
    private static readonly Histogram<double> LightControlDurationMs = Meter.CreateHistogram<double>("light.control.duration", "ms", "Duration of light control operations.");

    public LightControlSkill(
        IHomeAssistantClient homeAssistantClient,
        ILogger<LightControlSkill> logger,
        IEntityLocationService locationService,
        IOptionsMonitor<LightControlSkillOptions> options)
    {
        _homeAssistantClient = homeAssistantClient;
        _logger = logger;
        _locationService = locationService;
        _options = options;
    }

    public IList<AITool> GetTools()
    {
        return [
            AIFunctionFactory.Create(GetLightsStateAsync),
            AIFunctionFactory.Create(ControlLightsAsync),
            ];
    }

    // ── IOptimizableSkill ─────────────────────────────────────────

    /// <inheritdoc/>
    public string SkillDisplayName => "Light Control";

    /// <inheritdoc/>
    public string SkillId => "light-control";

    public string AgentId { get; set; } = string.Empty;

    /// <inheritdoc/>
    public string ConfigSectionName => LightControlSkillOptions.SectionName;

    /// <inheritdoc/>
    public IReadOnlyList<string> EntityDomains { get; } = ["light", "switch"];

    /// <inheritdoc/>
    public HybridMatchOptions GetCurrentMatchOptions()
    {
        var opts = _options.CurrentValue;
        return new HybridMatchOptions
        {
            Threshold = opts.HybridSimilarityThreshold,
            EmbeddingWeight = opts.EmbeddingWeight,
            ScoreDropoffRatio = opts.ScoreDropoffRatio,
            DisagreementPenalty = opts.DisagreementPenalty,
            EmbeddingResolutionMargin = opts.EmbeddingResolutionMargin
        };
    }

    /// <summary>
    /// Initialize the plugin.
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("LightControlSkill initialized — entity search delegated to IEntityLocationService.");
        return Task.CompletedTask;
    }

    [Description("Find light(s) in an area by name of the Area")]
    [Obsolete("Use ControlLightsAsync or GetLightsStateAsync instead. Will be removed in a future version.")]
    public Task<string> FindLightsByAreaAsync(
        [Description("The Area name (e.g 'kitchen', 'office', 'main bedroom')")] string area)
        => throw new NotSupportedException("FindLightsByAreaAsync is deprecated. Use ControlLightsAsync or GetLightsStateAsync.");

    [Description("Find a light entity by name or description using natural language")]
    [Obsolete("Use ControlLightsAsync or GetLightsStateAsync instead. Will be removed in a future version.")]
    public Task<string> FindLightAsync(
        [Description("Name or description of the light (e.g., 'living room light', 'kitchen ceiling', 'bedroom lamp')")] string searchTerm)
        => throw new NotSupportedException("FindLightAsync is deprecated. Use ControlLightsAsync or GetLightsStateAsync.");

    [Obsolete("Use GetLightsStateAsync instead. Will be removed in a future version.")]
    [Description("Get the current state of a specific light entity")]
    public Task<string> GetLightStateAsync(
        [Description("The entity ID of the light (e.g., 'light.living_room' or 'switch.kitchen_light')")] string entityId)
        => throw new NotSupportedException("GetLightStateAsync is deprecated. Use GetLightsStateAsync.");

    [Obsolete("Use ControlLightsAsync instead. Will be removed in a future version.")]
    [Description("Control a light or set of lights to a new state - turn on/off, set brightness, or change color")]
    public Task<string> SetLightStateAsync(
        [Description("The entity ID(s) of the light(s) (e.g., ['light.living_room'])")] string[] entityIds,
        [Description("Desired state: 'on' or 'off'")] string state,
        [Description("Brightness 0-100 for 'on' state; use -1 to omit")] int brightness = -1,
        [Description("Color name like 'red', 'blue', 'warm_white'; use empty string to omit")] string color = "")
        => throw new NotSupportedException("SetLightStateAsync is deprecated. Use ControlLightsAsync.");

    // ── New unified tools ─────────────────────────────────────────

    [Description("Get the current state of lights matching one or more search terms. Use natural language names, areas, or floor names (e.g., 'kitchen lights', 'upstairs', 'bedroom lamp').")]
    public async Task<string> GetLightsStateAsync(
        [Description("Search terms to find lights (e.g., ['living room lights', 'kitchen'])")] string[] searchTerms)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.terms", string.Join(", ", searchTerms));
        LightStateQueries.Add(1);
        var start = Stopwatch.GetTimestamp();

        try
        {
            var matchOptions = GetCurrentMatchOptions();
            var allEntities = new Dictionary<string, HomeAssistantEntity>(StringComparer.OrdinalIgnoreCase);

            foreach (var term in searchTerms)
            {
                var hierarchyResult = await _locationService.SearchHierarchyAsync(
                    term, matchOptions, (IReadOnlyList<string>)["light", "switch"]).ConfigureAwait(false);

                activity?.SetTag($"resolution.{term}", hierarchyResult.ResolutionStrategy.ToString());

                foreach (var entity in hierarchyResult.ResolvedEntities)
                    if (entity.IncludeForAgent is null || entity.IncludeForAgent.Contains(AgentId))
                        allEntities.TryAdd(entity.EntityId, entity);
            }

            if (allEntities.Count == 0)
            {
                LightStateFailures.Add(1, [new KeyValuePair<string, object?>("reason", "no-match")]);
                return $"No lights found matching: {string.Join(", ", searchTerms)}";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {allEntities.Count} light(s):");

            foreach (var entity in allEntities.Values)
            {
                var state = await _homeAssistantClient.GetEntityStateAsync(entity.EntityId)
                    .ConfigureAwait(false);
                if (state is null) continue;

                var displayName = entity.FriendlyName ?? entity.EntityId;
                sb.Append($"- {displayName} ({entity.EntityId}): {state.State}");

                if (state.State == "on" && state.Attributes.TryGetValue("brightness", out var brightnessObj) &&
                    int.TryParse(brightnessObj?.ToString(), out var brightness))
                {
                    sb.Append($" at {(int)Math.Round(brightness / 255.0 * 100)}%");
                }

                if (state.State == "on" && state.Attributes.TryGetValue("color_temp", out var colorTempObj))
                    sb.Append($", color temp {colorTempObj}");

                sb.AppendLine();
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting light states for: {SearchTerms}", string.Join(", ", searchTerms));
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LightStateFailures.Add(1, [new KeyValuePair<string, object?>("reason", "exception")]);
            return $"Failed to get light states: {ex.Message}";
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            LightStateDurationMs.Record(elapsedMs);
            activity?.SetTag("elapsed_ms", elapsedMs);
        }
    }

    [Description("Control lights matching one or more search terms — turn on/off, set brightness, or change color. Uses fuzzy search to find lights by name, area, or floor.")]
    public async Task<string> ControlLightsAsync(
        [Description("Search terms to find lights (e.g., ['living room', 'kitchen lights', 'upstairs'])")] string[] searchTerms,
        [Description("Desired state: 'on' or 'off'")] string state,
        [Description("Brightness 0-100 for 'on' state; use -1 to omit")] int brightness = -1,
        [Description("Color name like 'red', 'blue', 'warm_white'; use empty string to omit")] string color = "")
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.terms", string.Join(", ", searchTerms));
        activity?.SetTag("desired.state", state);
        if (brightness >= 0)
            activity?.SetTag("desired.brightness", brightness);
        if (!string.IsNullOrEmpty(color))
            activity?.SetTag("desired.color", color);

        LightControlRequests.Add(1);
        var start = Stopwatch.GetTimestamp();

        try
        {
            // Resolve all search terms into a deduplicated set of entities
            var matchOptions = GetCurrentMatchOptions();
            var allEntities = new Dictionary<string, HomeAssistantEntity>(StringComparer.OrdinalIgnoreCase);

            foreach (var term in searchTerms)
            {
                var hierarchyResult = await _locationService.SearchHierarchyAsync(
                    term, matchOptions, (IReadOnlyList<string>)["light", "switch"]).ConfigureAwait(false);

                activity?.SetTag($"resolution.{term}", hierarchyResult.ResolutionStrategy.ToString());

                foreach (var entity in hierarchyResult.ResolvedEntities)
                    if (entity.IncludeForAgent is null || entity.IncludeForAgent.Contains(AgentId))
                        allEntities.TryAdd(entity.EntityId, entity);
            }

            if (allEntities.Count == 0)
            {
                LightControlFailures.Add(1, [new KeyValuePair<string, object?>("reason", "no-match")]);
                return $"No lights found matching: {string.Join(", ", searchTerms)}";
            }

            activity?.SetTag("match.count", allEntities.Count);

            var normalizedState = state.ToLowerInvariant();
            if (normalizedState is not ("on" or "off"))
            {
                LightControlFailures.Add(1, [new KeyValuePair<string, object?>("reason", "invalid-state")]);
                return $"Invalid state '{state}'. Use 'on' or 'off'.";
            }

            var sb = new StringBuilder();
            foreach (var entity in allEntities.Values)
            {
                var entityId = entity.EntityId;
                var displayName = entity.FriendlyName ?? entityId;
                var isSwitch = entityId.StartsWith("switch.", StringComparison.Ordinal);
                var domain = isSwitch ? "switch" : "light";

                var request = new ServiceCallRequest
                {
                    ["entity_id"] = entityId
                };

                if (normalizedState == "off")
                {
                    await _homeAssistantClient.CallServiceAsync(domain, "turn_off", parameters: null, request)
                        .ConfigureAwait(false);
                    sb.AppendLine($"'{displayName}' turned off.");
                }
                else
                {
                    if (brightness >= 0 && !isSwitch)
                    {
                        var haBrightness = Math.Max(1, Math.Min(255, (int)Math.Round(brightness / 100.0 * 255)));
                        request["brightness"] = haBrightness;
                    }

                    if (!string.IsNullOrEmpty(color) && !isSwitch)
                        request["color_name"] = color;

                    await _homeAssistantClient.CallServiceAsync(domain, "turn_on", parameters: null, request)
                        .ConfigureAwait(false);

                    var result = $"'{displayName}' turned on";
                    if (brightness >= 0 && !isSwitch)
                        result += $" at {brightness}%";
                    if (!string.IsNullOrEmpty(color) && !isSwitch)
                        result += $" with {color} color";
                    sb.AppendLine($"{result}.");
                }
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error controlling lights for: {SearchTerms}", string.Join(", ", searchTerms));
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LightControlFailures.Add(1, [new KeyValuePair<string, object?>("reason", "exception")]);
            return $"Failed to control lights: {ex.Message}";
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            LightControlDurationMs.Record(elapsedMs);
            activity?.SetTag("elapsed_ms", elapsedMs);
        }
    }
}
