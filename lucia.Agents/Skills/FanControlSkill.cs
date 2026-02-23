using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Numerics.Tensors;
using System.Text;
using System.Text.Json;
using lucia.Agents.Models;
using lucia.Agents.Services;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Skills;

/// <summary>
/// Skill for controlling fans in Home Assistant.
/// Provides tools for discovering fans, checking state, and controlling speed/direction.
/// </summary>
public sealed class FanControlSkill : IAgentSkill
{
    private const double SimilarityThreshold = 0.6;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);
    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.FanControl", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Skills.FanControl", "1.0.0");

    private static readonly Counter<long> FanControlRequests = Meter.CreateCounter<long>("fan.control.requests");
    private static readonly Counter<long> FanControlFailures = Meter.CreateCounter<long>("fan.control.failures");
    private static readonly Histogram<double> FanControlDurationMs = Meter.CreateHistogram<double>("fan.control.duration", "ms");
    private static readonly Counter<long> CacheRefreshes = Meter.CreateCounter<long>("fan.cache.refreshes");

    private readonly IHomeAssistantClient _homeAssistantClient;
    private readonly IEmbeddingProviderResolver _embeddingResolver;
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private readonly IDeviceCacheService _cacheService;
    private readonly ILogger<FanControlSkill> _logger;

    private volatile List<FanEntity>? _fans;
    private volatile Dictionary<string, Embedding<float>>? _areaEmbeddings;
    private DateTime _lastCacheRefresh = DateTime.MinValue;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public FanControlSkill(
        IHomeAssistantClient homeAssistantClient,
        IEmbeddingProviderResolver embeddingResolver,
        IDeviceCacheService cacheService,
        ILogger<FanControlSkill> logger)
    {
        _homeAssistantClient = homeAssistantClient;
        _embeddingResolver = embeddingResolver;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing FanControlSkill...");

        _embeddingGenerator = await _embeddingResolver.ResolveAsync(ct: cancellationToken).ConfigureAwait(false);
        if (_embeddingGenerator is null)
        {
            _logger.LogWarning("No embedding provider configured — fan semantic search will not be available.");
            return;
        }

        await RefreshFanCacheAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("FanControlSkill initialized with {Count} fans", _fans?.Count ?? 0);
    }

    /// <summary>
    /// Re-resolves the embedding generator using the specified provider name.
    /// Called by the owning agent when the embedding configuration changes.
    /// </summary>
    public async Task UpdateEmbeddingProviderAsync(string? providerName, CancellationToken cancellationToken = default)
    {
        _embeddingGenerator = await _embeddingResolver.ResolveAsync(providerName, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("FanControlSkill: embedding provider updated to '{Provider}'", providerName ?? "system-default");
    }

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

    [Description("Find fans matching a search term using natural language. Returns all fans above the similarity threshold.")]
    public async Task<string> FindFanAsync(
        [Description("The search term to find fans (e.g., 'bedroom fan', 'ceiling fan', 'office')")] string searchTerm)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search_term", searchTerm);
        var start = Stopwatch.GetTimestamp();
        FanControlRequests.Add(1);

        try
        {
            await EnsureCacheIsCurrentAsync().ConfigureAwait(false);

            var fans = _fans;
            if (fans is null || fans.Count == 0)
                return "No fans found. The fan cache may be empty.";

            var searchEmbedding = await _embeddingGenerator!.GenerateAsync(searchTerm).ConfigureAwait(false);

            var matches = fans
                .Select(f => new { Fan = f, Similarity = CosineSimilarity(searchEmbedding.Vector.Span, f.NameEmbedding.Vector.Span) })
                .Where(m => m.Similarity >= SimilarityThreshold)
                .OrderByDescending(m => m.Similarity)
                .ToList();

            if (matches.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Found {matches.Count} fan(s) matching '{searchTerm}':");
                foreach (var match in matches)
                {
                    sb.AppendLine($"  - {match.Fan.FriendlyName} ({match.Fan.EntityId}) in {match.Fan.Area ?? "unknown area"} [similarity: {match.Similarity:F2}]");
                }
                activity?.SetStatus(ActivityStatusCode.Ok);
                return sb.ToString();
            }

            // Fall back to area-based search
            var areaEmbeddings = _areaEmbeddings;
            if (areaEmbeddings is not null)
            {
                var areaMatch = areaEmbeddings
                    .Select(kvp => new { Area = kvp.Key, Similarity = CosineSimilarity(searchEmbedding.Vector.Span, kvp.Value.Vector.Span) })
                    .Where(m => m.Similarity >= SimilarityThreshold)
                    .OrderByDescending(m => m.Similarity)
                    .FirstOrDefault();

                if (areaMatch is not null)
                {
                    var areaFans = fans.Where(f => string.Equals(f.Area, areaMatch.Area, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (areaFans.Count > 0)
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine($"Found {areaFans.Count} fan(s) in area '{areaMatch.Area}' matching '{searchTerm}':");
                        foreach (var fan in areaFans)
                        {
                            sb.AppendLine($"  - {fan.FriendlyName} ({fan.EntityId})");
                        }
                        activity?.SetStatus(ActivityStatusCode.Ok);
                        return sb.ToString();
                    }
                }
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"No fans found matching '{searchTerm}'.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding fan for '{SearchTerm}'", searchTerm);
            FanControlFailures.Add(1);
            return $"Error searching for fan: {ex.Message}";
        }
        finally
        {
            FanControlDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    [Description("Find all fans in a specific area/room.")]
    public async Task<string> FindFansByAreaAsync(
        [Description("The area/room name to search in (e.g., 'bedroom', 'living room')")] string areaName)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("area_name", areaName);
        var start = Stopwatch.GetTimestamp();
        FanControlRequests.Add(1);

        try
        {
            await EnsureCacheIsCurrentAsync().ConfigureAwait(false);

            var fans = _fans;
            if (fans is null || fans.Count == 0)
                return "No fans found. The fan cache may be empty.";

            var searchEmbedding = await _embeddingGenerator!.GenerateAsync(areaName).ConfigureAwait(false);
            var areaEmbeddings = _areaEmbeddings;

            string? bestArea = null;
            if (areaEmbeddings is not null)
            {
                var areaMatch = areaEmbeddings
                    .Select(kvp => new { Area = kvp.Key, Similarity = CosineSimilarity(searchEmbedding.Vector.Span, kvp.Value.Vector.Span) })
                    .OrderByDescending(m => m.Similarity)
                    .FirstOrDefault();

                if (areaMatch is not null && areaMatch.Similarity >= SimilarityThreshold)
                    bestArea = areaMatch.Area;
            }

            if (bestArea is null)
                return $"No area found matching '{areaName}'.";

            var areaFans = fans.Where(f => string.Equals(f.Area, bestArea, StringComparison.OrdinalIgnoreCase)).ToList();

            if (areaFans.Count == 0)
                return $"No fans found in area '{bestArea}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Fans in '{bestArea}':");
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding fans in area '{AreaName}'", areaName);
            FanControlFailures.Add(1);
            return $"Error searching fans by area: {ex.Message}";
        }
        finally
        {
            FanControlDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    [Description("Get the current state of a fan including on/off status, speed percentage, and direction.")]
    public async Task<string> GetFanStateAsync(
        [Description("The entity ID of the fan (e.g., 'fan.office_fan_fan')")] string entityId)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);
        var start = Stopwatch.GetTimestamp();
        FanControlRequests.Add(1);

        try
        {
            var state = await _homeAssistantClient.GetEntityStateAsync(entityId).ConfigureAwait(false);
            if (state is null)
                return $"Fan '{entityId}' not found.";

            var fans = _fans;
            var device = fans?.FirstOrDefault(f => f.EntityId == entityId);

            activity?.SetStatus(ActivityStatusCode.Ok);
            return FormatFanState(state, device);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting state for fan {EntityId}", entityId);
            FanControlFailures.Add(1);
            return $"Error getting fan state: {ex.Message}";
        }
        finally
        {
            FanControlDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    [Description("Turn a fan on or off.")]
    public async Task<string> SetFanStateAsync(
        [Description("The entity ID of the fan")] string entityId,
        [Description("The desired state: 'on' or 'off'")] string state)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);
        activity?.SetTag("state", state);
        var start = Stopwatch.GetTimestamp();
        FanControlRequests.Add(1);

        try
        {
            var service = state.Equals("on", StringComparison.OrdinalIgnoreCase) ? "turn_on" : "turn_off";
            var request = new ServiceCallRequest { EntityId = entityId };

            await _homeAssistantClient.CallServiceAsync("fan", service, request: request).ConfigureAwait(false);

            _logger.LogInformation("Set fan {EntityId} to {State}", entityId, state);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"Turned {state} fan '{entityId}'.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting fan {EntityId} to {State}", entityId, state);
            FanControlFailures.Add(1);
            return $"Failed to set fan state: {ex.Message}";
        }
        finally
        {
            FanControlDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    [Description("Set the speed of a fan as a percentage (0-100). Setting to 0 turns the fan off.")]
    public async Task<string> SetFanSpeedAsync(
        [Description("The entity ID of the fan")] string entityId,
        [Description("The speed percentage (0-100)")] int percentage)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);
        activity?.SetTag("percentage", percentage);
        var start = Stopwatch.GetTimestamp();
        FanControlRequests.Add(1);

        try
        {
            var request = new ServiceCallRequest
            {
                EntityId = entityId,
                ["percentage"] = percentage
            };

            await _homeAssistantClient.CallServiceAsync("fan", "set_percentage", request: request).ConfigureAwait(false);

            _logger.LogInformation("Set fan {EntityId} speed to {Percentage}%", entityId, percentage);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"Set fan '{entityId}' speed to {percentage}%.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting fan {EntityId} speed", entityId);
            FanControlFailures.Add(1);
            return $"Failed to set fan speed: {ex.Message}";
        }
        finally
        {
            FanControlDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    [Description("Set the direction of a fan (forward or reverse). Reverse is commonly used in winter to circulate warm air.")]
    public async Task<string> SetFanDirectionAsync(
        [Description("The entity ID of the fan")] string entityId,
        [Description("The direction: 'forward' or 'reverse'")] string direction)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);
        activity?.SetTag("direction", direction);
        var start = Stopwatch.GetTimestamp();
        FanControlRequests.Add(1);

        try
        {
            var request = new ServiceCallRequest
            {
                EntityId = entityId,
                ["direction"] = direction
            };

            await _homeAssistantClient.CallServiceAsync("fan", "set_direction", request: request).ConfigureAwait(false);

            _logger.LogInformation("Set fan {EntityId} direction to {Direction}", entityId, direction);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"Set fan '{entityId}' direction to '{direction}'.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting fan {EntityId} direction", entityId);
            FanControlFailures.Add(1);
            return $"Failed to set fan direction: {ex.Message}";
        }
        finally
        {
            FanControlDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    [Description("Toggle oscillation on a fan that supports it.")]
    public async Task<string> SetFanOscillationAsync(
        [Description("The entity ID of the fan")] string entityId,
        [Description("Whether to enable oscillation (true/false)")] bool oscillating)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);
        activity?.SetTag("oscillating", oscillating);
        var start = Stopwatch.GetTimestamp();
        FanControlRequests.Add(1);

        try
        {
            var request = new ServiceCallRequest
            {
                EntityId = entityId,
                ["oscillating"] = oscillating
            };

            await _homeAssistantClient.CallServiceAsync("fan", "oscillate", request: request).ConfigureAwait(false);

            _logger.LogInformation("Set fan {EntityId} oscillation to {Oscillating}", entityId, oscillating);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return $"Set fan '{entityId}' oscillation to {(oscillating ? "on" : "off")}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting fan {EntityId} oscillation", entityId);
            FanControlFailures.Add(1);
            return $"Failed to set fan oscillation: {ex.Message}";
        }
        finally
        {
            FanControlDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    [Description("Set a fan's preset mode (e.g., auto, nature, sleep). Use GetFanState to check available presets first.")]
    public async Task<string> SetFanPresetModeAsync(
        [Description("The entity ID of the fan")] string entityId,
        [Description("The preset mode to set (e.g., 'auto', 'nature', 'sleep')")] string presetMode)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity_id", entityId);
        activity?.SetTag("preset_mode", presetMode);
        var start = Stopwatch.GetTimestamp();
        FanControlRequests.Add(1);

        try
        {
            await EnsureCacheIsCurrentAsync().ConfigureAwait(false);
            var fan = _fans?.FirstOrDefault(f => f.EntityId.Equals(entityId, StringComparison.OrdinalIgnoreCase));

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting fan {EntityId} preset mode", entityId);
            FanControlFailures.Add(1);
            return $"Failed to set fan preset mode: {ex.Message}";
        }
        finally
        {
            FanControlDurationMs.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    private static string FormatFanState(HomeAssistantState state, FanEntity? device)
    {
        var sb = new StringBuilder();
        sb.Append($"State: {state.State}");

        if (state.Attributes.TryGetValue("percentage", out var pctObj))
            sb.Append($", Speed: {pctObj}%");

        if (state.Attributes.TryGetValue("direction", out var dirObj))
            sb.Append($", Direction: {dirObj}");

        if (state.Attributes.TryGetValue("preset_mode", out var presetObj) && presetObj is not null)
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
            if (device.SupportsPresetMode && device.PresetModes.Count > 0)
                features.Add($"presets: [{string.Join(", ", device.PresetModes)}]");
            if (features.Count > 0)
                sb.Append($", Capabilities: [{string.Join(", ", features)}]");
        }

        return sb.ToString();
    }

    private async Task RefreshFanCacheAsync(CancellationToken cancellationToken = default)
    {
        if (_embeddingGenerator is null)
        {
            _logger.LogWarning("Skipping fan cache refresh — no embedding provider available.");
            return;
        }

        using var activity = ActivitySource.StartActivity();
        CacheRefreshes.Add(1);

        try
        {
            // Try Redis first
            var cachedFans = await _cacheService.GetCachedFansAsync(cancellationToken).ConfigureAwait(false);
            var cachedAreaEmbeddings = await _cacheService.GetAreaEmbeddingsAsync(cancellationToken).ConfigureAwait(false);

            if (cachedFans is not null && cachedFans.Count > 0)
            {
                // Re-generate embeddings for cached entities
                var names = cachedFans.Select(f => f.FriendlyName).ToList();
                var embeddings = await _embeddingGenerator.GenerateAsync(names, cancellationToken: cancellationToken).ConfigureAwait(false);
                for (var i = 0; i < cachedFans.Count; i++)
                {
                    cachedFans[i].NameEmbedding = embeddings[i];
                }

                _fans = cachedFans;
                _areaEmbeddings = cachedAreaEmbeddings;
                _lastCacheRefresh = DateTime.UtcNow;
                _logger.LogInformation("Loaded {Count} fans from Redis cache", cachedFans.Count);
                return;
            }

            // Fetch from Home Assistant
            var states = await _homeAssistantClient.GetStatesAsync(cancellationToken).ConfigureAwait(false);

            var fanEntities = states
                .Where(s => s.EntityId.StartsWith("fan.", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (fanEntities.Count == 0)
            {
                _logger.LogInformation("No fan entities found in Home Assistant");
                _fans = [];
                return;
            }

            // Build a lookup of companion select entities that control fan modes.
            // Some integrations (e.g., LocalTuya) expose fan modes as a separate
            // select entity named select.{fan_suffix}_mode rather than using the
            // fan entity's native preset_modes attribute.
            var modeSelectLookup = BuildModeSelectLookup(states, fanEntities);

            var newFans = new List<FanEntity>();
            var areas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entity in fanEntities)
            {
                var friendlyName = entity.Attributes.TryGetValue("friendly_name", out var nameObj)
                    ? nameObj.ToString() ?? entity.EntityId
                    : entity.EntityId;

                var area = entity.Attributes.TryGetValue("area_id", out var areaObj) ? areaObj.ToString() : null;
                if (area is null)
                {
                    // Try to infer area from entity ID
                    var parts = entity.EntityId.Replace("fan.", "").Split('_');
                    if (parts.Length > 1)
                        area = parts[0];
                }
                if (area is not null) areas.Add(area);

                var percentageStep = 1;
                if (entity.Attributes.TryGetValue("percentage_step", out var stepObj) && int.TryParse(stepObj?.ToString(), out var step))
                    percentageStep = step;

                var presetModes = ParseStringListAttribute(entity.Attributes, "preset_modes");

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

                var embedding = await _embeddingGenerator.GenerateAsync(friendlyName, cancellationToken: cancellationToken).ConfigureAwait(false);

                newFans.Add(new FanEntity
                {
                    EntityId = entity.EntityId,
                    FriendlyName = friendlyName,
                    NameEmbedding = embedding,
                    Area = area,
                    PercentageStep = percentageStep,
                    PresetModes = presetModes,
                    ModeSelectEntityId = modeSelectEntityId,
                    SupportedFeatures = supportedFeatures
                });
            }

            // Generate area embeddings if needed
            Dictionary<string, Embedding<float>>? newAreaEmbeddings = null;
            if (areas.Count > 0)
            {
                var areaList = areas.ToList();
                var areaEmbeddingResults = await _embeddingGenerator.GenerateAsync(areaList, cancellationToken: cancellationToken).ConfigureAwait(false);
                newAreaEmbeddings = new Dictionary<string, Embedding<float>>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < areaList.Count; i++)
                {
                    newAreaEmbeddings[areaList[i]] = areaEmbeddingResults[i];
                }
            }

            // Atomic swap
            _fans = newFans;
            _areaEmbeddings = newAreaEmbeddings ?? cachedAreaEmbeddings;
            _lastCacheRefresh = DateTime.UtcNow;

            // Persist to Redis
            await _cacheService.SetCachedFansAsync(newFans, CacheTtl, cancellationToken).ConfigureAwait(false);
            if (newAreaEmbeddings is not null)
                await _cacheService.SetAreaEmbeddingsAsync(newAreaEmbeddings, CacheTtl, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Refreshed fan cache: {Count} fans from {Areas} areas", newFans.Count, areas.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing fan cache");
        }
    }

    private async Task EnsureCacheIsCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (_fans is not null && DateTime.UtcNow - _lastCacheRefresh < CacheTtl)
            return;

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_fans is not null && DateTime.UtcNow - _lastCacheRefresh < CacheTtl)
                return;

            await RefreshFanCacheAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        return TensorPrimitives.CosineSimilarity(a, b);
    }

    /// <summary>
    /// Safely parses a JSON array attribute that may contain strings, numbers, or null.
    /// HA devices can return null for preset_modes when no presets are available.
    /// </summary>
    private static List<string> ParseStringListAttribute(Dictionary<string, object> attributes, string key)
    {
        if (!attributes.TryGetValue(key, out var value) || value is null)
            return [];

        try
        {
            if (value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Null || jsonElement.ValueKind != JsonValueKind.Array)
                    return [];

                var result = new List<string>();
                foreach (var item in jsonElement.EnumerateArray())
                {
                    var str = item.ValueKind switch
                    {
                        JsonValueKind.String => item.GetString(),
                        JsonValueKind.Number => item.GetRawText(),
                        _ => item.GetRawText()
                    };
                    if (str is not null) result.Add(str);
                }
                return result;
            }

            var json = value.ToString() ?? "[]";
            var modes = JsonSerializer.Deserialize<JsonElement>(json);
            if (modes.ValueKind == JsonValueKind.Array)
            {
                return modes.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText())
                    .ToList();
            }
        }
        catch (JsonException)
        {
            // Swallow parse failures
        }

        return [];
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

            var options = ParseStringListAttribute(selectState.Attributes, "options");
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
