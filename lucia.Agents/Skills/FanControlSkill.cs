using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Integration;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using lucia.Agents.Services;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Skills;

/// <summary>
/// Skill for controlling fans in Home Assistant.
/// Provides tools for discovering fans, checking state, and controlling speed/direction.
/// </summary>
public sealed class FanControlSkill : ILuciaSkill, IOptimizableSkill
{
    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.FanControl", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Skills.FanControl", "1.0.0");

    private static readonly Counter<long> FanControlRequests = Meter.CreateCounter<long>("fan.control.requests");
    private static readonly Counter<long> FanControlFailures = Meter.CreateCounter<long>("fan.control.failures");
    private static readonly Histogram<double> FanControlDurationMs = Meter.CreateHistogram<double>("fan.control.duration", "ms");
    private static readonly Counter<long> CacheRefreshes = Meter.CreateCounter<long>("fan.cache.refreshes");

    private readonly IHomeAssistantClient _homeAssistantClient;
    private readonly IDeviceCacheService _cacheService;
    private readonly IEntityLocationService _locationService;
    private readonly IHybridEntityMatcher _entityMatcher;
    private readonly IOptionsMonitor<FanControlSkillOptions> _options;
    private readonly ILogger<FanControlSkill> _logger;
    private readonly SkillEntityIndex<FanEntity> _index;

    public FanControlSkill(
        IHomeAssistantClient homeAssistantClient,
        IEmbeddingProviderResolver embeddingResolver,
        IDeviceCacheService cacheService,
        IEntityLocationService locationService,
        IHybridEntityMatcher entityMatcher,
        IOptionsMonitor<FanControlSkillOptions> options,
        ILogger<FanControlSkill> logger)
    {
        _homeAssistantClient = homeAssistantClient;
        _cacheService = cacheService;
        _locationService = locationService;
        _entityMatcher = entityMatcher;
        _options = options;
        _logger = logger;
        _index = new SkillEntityIndex<FanEntity>(
            embeddingResolver,
            logger,
            "fan",
            LoadFansAsync,
            () => TimeSpan.FromMinutes(options.CurrentValue.CacheRefreshMinutes));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing FanControlSkill...");

        await _index.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (_index.Embeddings is null)
            return;

        _logger.LogInformation("FanControlSkill initialized with {Count} fans", _index.Items.Length);
    }

    /// <summary>
    /// Re-resolves the embedding generator using the specified provider name.
    /// Called by the owning agent when the embedding configuration changes.
    /// </summary>
    public Task UpdateEmbeddingProviderAsync(string? providerName, CancellationToken cancellationToken = default)
        => _index.UpdateEmbeddingProviderAsync(providerName, cancellationToken);

    public IList<AITool> GetTools()
    {
        return [
            AIFunctionFactory.Create(FindFanAsync),
            AIFunctionFactory.Create(FindFansByAreaAsync),
            AIFunctionFactory.Create(GetFanStateAsync),
            AIFunctionFactory.Create(SetFanStateAsync),
            AIFunctionFactory.Create(SetFanSpeedAsync),
            AIFunctionFactory.Create(SetFanDirectionAsync),
            AIFunctionFactory.Create(SetFanOscillationAsync),
            AIFunctionFactory.Create(SetFanPresetModeAsync)
        ];
    }

    // ── IOptimizableSkill ─────────────────────────────────────────

    /// <inheritdoc/>
    public string SkillDisplayName => "Fan Control";

    /// <inheritdoc/>
    public string SkillId => "fan-control";

    /// <inheritdoc/>
    public string AgentId { get; set; } = string.Empty;

    /// <inheritdoc/>
    public IReadOnlyList<string> SearchToolNames { get; } = ["FindFan", "FindFansByArea"];

    /// <inheritdoc/>
    public string ConfigSectionName => FanControlSkillOptions.SectionName;

    /// <inheritdoc/>
    public IReadOnlyList<string> EntityDomains => _options.CurrentValue.EntityDomains;

    /// <inheritdoc/>
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

    [Description("Find fans matching a search term using natural language. Returns all fans above the similarity threshold.")]
    public async Task<string> FindFanAsync(
        [Description("The search term to find fans (e.g., 'bedroom fan', 'ceiling fan', 'office')")] string searchTerm)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search_term", searchTerm);
        FanControlRequests.Add(1);

        return await ToolRunner.RunAsync(_logger, activity, "Unable to search for fans. Please try again.", async () =>
        {
            await _index.EnsureCurrentAsync().ConfigureAwait(false);

            var fans = _index.Items;
            if (fans.IsEmpty)
                return "No fans found. The fan cache may be empty.";

            var embeddings = _index.Embeddings;
            if (embeddings is null)
            {
                FanControlFailures.Add(1);
                return "Embedding provider not available for fan search.";
            }

            var matches = await _entityMatcher.FindMatchesAsync(
                searchTerm,
                (IReadOnlyList<FanEntity>)fans,
                embeddings,
                GetCurrentMatchOptions()).ConfigureAwait(false);

            if (matches.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Found {matches.Count} fan(s) matching '{searchTerm}':");
                foreach (var match in matches)
                {
                    sb.AppendLine($"  - {match.Entity.FriendlyName} ({match.Entity.EntityId}) in {match.Entity.Area ?? "unknown area"} [score: {match.HybridScore:F2}]");
                }
                activity?.SetStatus(ActivityStatusCode.Ok);
                return sb.ToString();
            }

            // Fall back to hierarchical search using location service
            var hierarchyResult = await _locationService.SearchHierarchyAsync(
                searchTerm, GetCurrentMatchOptions(), EntityDomains, CancellationToken.None).ConfigureAwait(false);
            var locationEntities = hierarchyResult.ResolvedEntities;

            activity?.SetTag("match.resolution", hierarchyResult.ResolutionStrategy.ToString());

            if (hierarchyResult.ResolutionStrategy != ResolutionStrategy.None && locationEntities.Count > 0)
            {
                var matchedEntityIds = locationEntities.Select(e => e.EntityId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var areaFans = fans.Where(f => matchedEntityIds.Contains(f.EntityId)).ToList();
                if (areaFans.Count > 0)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"Found {areaFans.Count} fan(s) matching '{searchTerm}':");
                    foreach (var fan in areaFans)
                    {
                        sb.AppendLine($"  - {fan.FriendlyName} ({fan.EntityId}) in {fan.Area ?? "unknown area"}");
                    }
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return sb.ToString();
                }
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"No fans found matching '{searchTerm}'.";
        }, onFailure: () => FanControlFailures.Add(1), duration: FanControlDurationMs, detail: searchTerm).ConfigureAwait(false);
    }

    [Description("Find all fans in a specific area/room.")]
    public async Task<string> FindFansByAreaAsync(
        [Description("The area/room name to search in (e.g., 'bedroom', 'living room')")] string areaName)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("area_name", areaName);
        FanControlRequests.Add(1);

        return await ToolRunner.RunAsync(_logger, activity, "Unable to search for fans in that area. Please try again.", async () =>
        {
            await _index.EnsureCurrentAsync().ConfigureAwait(false);

            var fans = _index.Items;
            if (fans.IsEmpty)
                return "No fans found. The fan cache may be empty.";

            var hierarchyResult = await _locationService.SearchHierarchyAsync(
                areaName, GetCurrentMatchOptions(), EntityDomains, CancellationToken.None).ConfigureAwait(false);
            var locationEntities = hierarchyResult.ResolvedEntities;

            activity?.SetTag("match.resolution", hierarchyResult.ResolutionStrategy.ToString());

            if (hierarchyResult.ResolutionStrategy == ResolutionStrategy.None || locationEntities.Count == 0)
                return $"No fans found matching '{areaName}'. {hierarchyResult.ResolutionReason}";

            var matchedEntityIds = locationEntities.Select(e => e.EntityId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var areaFans = fans.Where(f => matchedEntityIds.Contains(f.EntityId)).ToList();

            if (areaFans.Count == 0)
                return $"No fans found matching '{areaName}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Fans matching '{areaName}':");
            foreach (var fan in areaFans)
            {
                var features = new List<string>();
                if (fan.SupportsSpeed) features.Add("speed");
                if (fan.SupportsDirection) features.Add("direction");
                if (fan.SupportsOscillate) features.Add("oscillate");
                if (fan.SupportsPresetMode) features.Add($"presets: [{string.Join(", ", fan.PresetModes)}]");
                var featureStr = features.Count > 0 ? $" [{string.Join(", ", features)}]" : "";
                sb.AppendLine($"  - {fan.FriendlyName} ({fan.EntityId}){featureStr}");
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return sb.ToString();
        }, onFailure: () => FanControlFailures.Add(1), duration: FanControlDurationMs, detail: areaName).ConfigureAwait(false);
    }

    [Description("Get the current state of a fan including on/off status, speed percentage, and direction.")]
    public async Task<string> GetFanStateAsync(
        [Description("The entity ID of the fan (e.g., 'fan.office_fan_fan')")] string entityId)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);
        FanControlRequests.Add(1);

        return await ToolRunner.RunAsync(_logger, activity, $"Unable to get the state of fan '{entityId}'. Please try again.", async () =>
        {
            var state = await _homeAssistantClient.GetEntityStateAsync(entityId).ConfigureAwait(false);
            if (state is null)
                return $"Fan '{entityId}' not found.";

            var device = _index.Items.FirstOrDefault(f => f.EntityId == entityId);

            activity?.SetStatus(ActivityStatusCode.Ok);
            return FormatFanState(state, device);
        }, onFailure: () => FanControlFailures.Add(1), duration: FanControlDurationMs, detail: entityId).ConfigureAwait(false);
    }

    [Description("Turn a fan on or off.")]
    public async Task<string> SetFanStateAsync(
        [Description("The entity ID of the fan")] string entityId,
        [Description("The desired state: 'on' or 'off'")] string state)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);
        activity?.SetTag("state", state);
        FanControlRequests.Add(1);

        return await ToolRunner.RunAsync(_logger, activity, "Unable to set the fan state. Please try again.", async () =>
        {
            var service = state.Equals("on", StringComparison.OrdinalIgnoreCase) ? "turn_on" : "turn_off";
            var request = new ServiceCallRequest { EntityId = entityId };

            await _homeAssistantClient.CallServiceAsync("fan", service, request: request).ConfigureAwait(false);

            _logger.LogInformation("Set fan {EntityId} to {State}", entityId, state);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"Turned {state} fan '{entityId}'.";
        }, onFailure: () => FanControlFailures.Add(1), duration: FanControlDurationMs, detail: entityId).ConfigureAwait(false);
    }

    [Description("Set the speed of a fan as a percentage (0-100). Setting to 0 turns the fan off.")]
    public Task<string> SetFanSpeedAsync(
        [Description("The entity ID of the fan")] string entityId,
        [Description("The speed percentage (0-100)")] int percentage)
        => CallFanServiceAsync(
            "set_percentage", entityId, "percentage", percentage, "speed",
            $"Set fan '{entityId}' speed to {percentage}%.");

    [Description("Set the direction of a fan (forward or reverse). Reverse is commonly used in winter to circulate warm air.")]
    public Task<string> SetFanDirectionAsync(
        [Description("The entity ID of the fan")] string entityId,
        [Description("The direction: 'forward' or 'reverse'")] string direction)
        => CallFanServiceAsync(
            "set_direction", entityId, "direction", direction, "direction",
            $"Set fan '{entityId}' direction to '{direction}'.");

    [Description("Toggle oscillation on a fan that supports it.")]
    public Task<string> SetFanOscillationAsync(
        [Description("The entity ID of the fan")] string entityId,
        [Description("Whether to enable oscillation (true/false)")] bool oscillating)
        => CallFanServiceAsync(
            "oscillate", entityId, "oscillating", oscillating, "oscillation",
            $"Set fan '{entityId}' oscillation to {(oscillating ? "on" : "off")}.");

    /// <summary>
    /// Calls one <c>fan</c> service with a single data field. The control tools differ only in the
    /// service name, the field, and the wording.
    /// </summary>
    private async Task<string> CallFanServiceAsync(
        string service,
        string entityId,
        string dataKey,
        object value,
        string what,
        string successMessage)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);
        activity?.SetTag(dataKey, value);
        FanControlRequests.Add(1);

        return await ToolRunner.RunAsync(_logger, activity, $"Unable to set fan {what}. Please try again.", async () =>
        {
            var request = new ServiceCallRequest
            {
                EntityId = entityId,
                [dataKey] = value
            };

            await _homeAssistantClient.CallServiceAsync("fan", service, request: request).ConfigureAwait(false);

            _logger.LogInformation("Set fan {EntityId} {What} to {Value}", entityId, what, value);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return successMessage;
        }, onFailure: () => FanControlFailures.Add(1), duration: FanControlDurationMs, detail: entityId).ConfigureAwait(false);
    }

    [Description("Set a fan's preset mode (e.g., auto, nature, sleep). Use GetFanState to check available presets first.")]
    public async Task<string> SetFanPresetModeAsync(
        [Description("The entity ID of the fan")] string entityId,
        [Description("The preset mode to set (e.g., 'auto', 'nature', 'sleep')")] string presetMode)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);
        activity?.SetTag("preset_mode", presetMode);
        FanControlRequests.Add(1);

        return await ToolRunner.RunAsync(_logger, activity, "Unable to set the fan preset mode. Please try again.", async () =>
        {
            await _index.EnsureCurrentAsync().ConfigureAwait(false);
            var fan = _index.Items.FirstOrDefault(f => f.EntityId.Equals(entityId, StringComparison.OrdinalIgnoreCase));

            // If the fan has a companion select entity for modes, use that instead
            if (fan?.ModeSelectEntityId is { Length: > 0 } selectEntityId)
            {
                // Match option case-insensitively against available modes
                var matchedMode = fan.PresetModes
                    .FirstOrDefault(m => m.Equals(presetMode, StringComparison.OrdinalIgnoreCase))
                    ?? presetMode;

                var selectRequest = new ServiceCallRequest
                {
                    EntityId = selectEntityId,
                    ["option"] = matchedMode
                };

                await _homeAssistantClient.CallServiceAsync("select", "select_option", request: selectRequest).ConfigureAwait(false);

                _logger.LogInformation("Set fan {EntityId} mode to {PresetMode} via select entity {SelectEntity}",
                    entityId, matchedMode, selectEntityId);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return $"Set fan '{entityId}' mode to '{matchedMode}'.";
            }

            // Fall back to native fan preset mode service
            var request = new ServiceCallRequest
            {
                EntityId = entityId,
                ["preset_mode"] = presetMode
            };

            await _homeAssistantClient.CallServiceAsync("fan", "set_preset_mode", request: request).ConfigureAwait(false);

            _logger.LogInformation("Set fan {EntityId} preset mode to {PresetMode}", entityId, presetMode);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"Set fan '{entityId}' preset mode to '{presetMode}'.";
        }, onFailure: () => FanControlFailures.Add(1), duration: FanControlDurationMs, detail: entityId).ConfigureAwait(false);
    }

    private static string FormatFanState(HomeAssistantState state, FanEntity? device)
    {
        var sb = new StringBuilder();
        sb.Append($"State: {state.State}");

        if (state.Attributes.TryGetValue("percentage", out var pctObj))
            sb.Append($", Speed: {pctObj}%");

        if (state.Attributes.TryGetValue("direction", out var dirObj))
            sb.Append($", Direction: {dirObj}");

        if (state.Attributes.TryGetValue("preset_mode", out var presetObj))
            sb.Append($", Preset: {presetObj}");

        if (state.Attributes.TryGetValue("oscillating", out var oscObj))
            sb.Append($", Oscillating: {oscObj}");

        if (device is not null)
        {
            // Show companion mode info so the LLM knows modes are available
            if (device.ModeSelectEntityId is not null && device.PresetModes.Count > 0)
                sb.Append($", Mode source: select entity, Available modes: [{string.Join(", ", device.PresetModes)}]");

            var features = new List<string>();
            if (device.SupportsSpeed) features.Add("speed");
            if (device.SupportsDirection) features.Add("direction");
            if (device.SupportsOscillate) features.Add("oscillate");
            if (device is { SupportsPresetMode: true, PresetModes.Count: > 0 })
                features.Add($"presets: [{string.Join(", ", device.PresetModes)}]");
            if (features.Count > 0)
                sb.Append($", Capabilities: [{string.Join(", ", features)}]");
        }

        return sb.ToString();
    }

    private async Task<IReadOnlyList<FanEntity>> LoadFansAsync(
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("RefreshFanCacheAsync");
        CacheRefreshes.Add(1);

        var cached = await TryLoadFromDeviceCacheAsync(embeddings, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            _logger.LogInformation("Loaded {Count} fans from Redis cache", cached.Count);
            return cached;
        }

        var states = await _homeAssistantClient.GetStatesAsync(cancellationToken).ConfigureAwait(false);

        var fanEntities = states
            .Where(s => s.EntityId.StartsWith("fan.", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (fanEntities.Count == 0)
        {
            _logger.LogInformation("No fan entities found in Home Assistant");
            return [];
        }

        // Some integrations (e.g., LocalTuya) expose fan modes as a separate select entity named
        // select.{fan_suffix}_mode rather than using the fan entity's native preset_modes attribute.
        var modeSelectLookup = BuildModeSelectLookup(states, fanEntities);

        var fans = new List<FanEntity>();
        foreach (var entity in fanEntities)
            fans.Add(await BuildFanAsync(entity, modeSelectLookup, embeddings, cancellationToken).ConfigureAwait(false));

        await StoreInDeviceCacheAsync(fans, cancellationToken).ConfigureAwait(false);
        return fans;
    }

    /// <summary>Device data only. Areas come from <see cref="IEntityLocationService"/>. Names are embedded again in one batch.</summary>
    private async Task<List<FanEntity>?> TryLoadFromDeviceCacheAsync(
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        CancellationToken cancellationToken)
    {
        var cachedFans = await _cacheService.GetCachedFansAsync(cancellationToken).ConfigureAwait(false);
        if (cachedFans is null or { Count: 0 })
            return null;

        var names = cachedFans.Select(f => f.FriendlyName).ToList();
        var generated = await embeddings.GenerateAsync(names, cancellationToken: cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < cachedFans.Count; i++)
        {
            cachedFans[i].NameEmbedding = generated[i];
            if (cachedFans[i].PhoneticKeys.Length == 0)
                cachedFans[i].PhoneticKeys = StringSimilarity.BuildPhoneticKeys(cachedFans[i].FriendlyName);
        }

        return cachedFans;
    }

    private async Task<FanEntity> BuildFanAsync(
        HomeAssistantState entity,
        Dictionary<string, ModeSelectInfo> modeSelectLookup,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        CancellationToken cancellationToken)
    {
        var friendlyName = entity.Attributes.TryGetValue("friendly_name", out var nameObj)
            ? nameObj?.ToString() ?? entity.EntityId
            : entity.EntityId;

        var percentageStep = 1;
        if (entity.Attributes.TryGetValue("percentage_step", out var stepObj) && int.TryParse(stepObj?.ToString(), out var step))
            percentageStep = step;

        var presetModes = AttributeParser.ParseStringList(entity.Attributes, "preset_modes");

        // Check for a companion select entity that controls modes
        string? modeSelectEntityId = null;
        if (presetModes.Count == 0
            && modeSelectLookup.TryGetValue(entity.EntityId, out var selectInfo))
        {
            modeSelectEntityId = selectInfo.EntityId;
            presetModes = selectInfo.Options;
            _logger.LogDebug("Fan {FanId} uses companion select entity {SelectId} for modes: [{Modes}]",
                entity.EntityId, modeSelectEntityId, string.Join(", ", presetModes));
        }

        var supportedFeatures = 0;
        if (entity.Attributes.TryGetValue("supported_features", out var featObj) && int.TryParse(featObj?.ToString(), out var feat))
            supportedFeatures = feat;

        return new FanEntity
        {
            EntityId = entity.EntityId,
            FriendlyName = friendlyName,
            NameEmbedding = await embeddings.GenerateAsync(friendlyName, cancellationToken: cancellationToken).ConfigureAwait(false),
            PhoneticKeys = StringSimilarity.BuildPhoneticKeys(friendlyName),
            Area = _locationService.GetAreaForEntity(entity.EntityId)?.Name,
            PercentageStep = percentageStep,
            PresetModes = presetModes,
            ModeSelectEntityId = modeSelectEntityId,
            SupportedFeatures = supportedFeatures
        };
    }

    /// <summary>A failed write must not discard fans that were just loaded, so it only logs.</summary>
    private async Task StoreInDeviceCacheAsync(List<FanEntity> fans, CancellationToken cancellationToken)
    {
        try
        {
            var cacheTtl = TimeSpan.FromMinutes(_options.CurrentValue.CacheRefreshMinutes);
            await _cacheService.SetCachedFansAsync(fans, cacheTtl, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Refreshed fan cache: {Count} fans", fans.Count);
        }
        catch (Exception ex) when (!ex.IsCancellation())
        {
            _logger.LogError(ex, "Error caching fans");
        }
    }

    /// <summary>
    /// Builds a lookup from fan entity ID to a companion select entity that controls
    /// fan modes. Some integrations (e.g., LocalTuya/Tuya) expose fan modes as a
    /// separate <c>select.{device}_fan_mode</c> entity instead of through the fan
    /// entity's native <c>preset_modes</c> attribute.
    /// <para>
    /// Detection is deterministic: for each fan entity <c>fan.{suffix}</c>, check
    /// whether <c>select.{suffix}_mode</c> exists in the entity states and has
    /// a non-empty <c>options</c> list.
    /// </para>
    /// </summary>
    private Dictionary<string, ModeSelectInfo> BuildModeSelectLookup(
        HomeAssistantState[] states,
        List<HomeAssistantState> fanEntities)
    {
        // Index all select entities by entity_id for O(1) lookup
        var selectStates = states
            .Where(s => s.EntityId.StartsWith("select.", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(s => s.EntityId, StringComparer.OrdinalIgnoreCase);

        var lookup = new Dictionary<string, ModeSelectInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var fan in fanEntities)
        {
            // fan.office_fan_fan → select.office_fan_fan_mode
            var fanSuffix = fan.EntityId["fan.".Length..];
            var candidateSelectId = $"select.{fanSuffix}_mode";

            if (!selectStates.TryGetValue(candidateSelectId, out var selectState))
                continue;

            var options = AttributeParser.ParseStringList(selectState.Attributes, "options");
            if (options.Count == 0)
                continue;

            lookup[fan.EntityId] = new ModeSelectInfo(candidateSelectId, options);
            _logger.LogDebug(
                "Detected companion mode select {SelectId} for fan {FanId} with options: [{Options}]",
                candidateSelectId, fan.EntityId, string.Join(", ", options));
        }

        return lookup;
    }

    private sealed record ModeSelectInfo(string EntityId, List<string> Options);
}
