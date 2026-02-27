using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using lucia.HomeAssistant.Services;
using lucia.HomeAssistant.Models;
using lucia.Agents.Models;
using lucia.Agents.Services;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Skills;

/// <summary>
/// Semantic Kernel plugin for Home Assistant light control with caching and similarity search
/// </summary>
public class LightControlSkill : IAgentSkill
{
    private readonly IHomeAssistantClient _homeAssistantClient;
    private readonly IEmbeddingProviderResolver _embeddingResolver;
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingService;
    private readonly ILogger<LightControlSkill> _logger;
    private readonly IDeviceCacheService _deviceCache;
    private readonly IEntityLocationService _locationService;
    private readonly IEmbeddingSimilarityService _similarity;
    private ImmutableArray<LightEntity> _cachedLights = [];
    private long _lastCacheUpdateTicks = DateTime.MinValue.Ticks;
    private readonly TimeSpan _cacheRefreshInterval = TimeSpan.FromMinutes(30);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.LightControl", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Skills.LightControl", "1.0.0");
    private static readonly Counter<long> LightSearchRequests = Meter.CreateCounter<long>("light.search.requests", "{count}", "Number of light search requests.");
    private static readonly Counter<long> LightSearchSuccess = Meter.CreateCounter<long>("light.search.success", "{count}", "Number of successful light searches.");
    private static readonly Counter<long> LightSearchFailures = Meter.CreateCounter<long>("light.search.failures", "{count}", "Number of failed light searches.");
    private static readonly Histogram<double> LightSearchDurationMs = Meter.CreateHistogram<double>("light.search.duration", "ms", "Duration of light search operations.");
    private static readonly Counter<long> LightStateQueries = Meter.CreateCounter<long>("light.state.queries", "{count}", "Number of light state queries.");
    private static readonly Counter<long> LightStateFailures = Meter.CreateCounter<long>("light.state.failures", "{count}", "Number of failed light state queries.");
    private static readonly Histogram<double> LightStateDurationMs = Meter.CreateHistogram<double>("light.state.duration", "ms", "Duration of light state queries.");
    private static readonly Counter<long> LightControlRequests = Meter.CreateCounter<long>("light.control.requests", "{count}", "Number of light control requests.");
    private static readonly Counter<long> LightControlFailures = Meter.CreateCounter<long>("light.control.failures", "{count}", "Number of failed light control requests.");
    private static readonly Histogram<double> LightControlDurationMs = Meter.CreateHistogram<double>("light.control.duration", "ms", "Duration of light control operations.");
    private static readonly Histogram<double> CacheRefreshDurationMs = Meter.CreateHistogram<double>("light.cache.refresh.duration", "ms", "Duration of light cache refresh operations.");

    public LightControlSkill(
        IHomeAssistantClient homeAssistantClient,
        IEmbeddingProviderResolver embeddingResolver,
        ILogger<LightControlSkill> logger,
        IDeviceCacheService deviceCache,
        IEntityLocationService locationService,
        IEmbeddingSimilarityService similarity)
    {
        _homeAssistantClient = homeAssistantClient;
        _embeddingResolver = embeddingResolver;
        _logger = logger;
        _deviceCache = deviceCache;
        _locationService = locationService;
        _similarity = similarity;
    }

    public IList<AITool> GetTools()
    {
        return [
            AIFunctionFactory.Create(FindLightAsync),
            AIFunctionFactory.Create(FindLightsByAreaAsync),
            AIFunctionFactory.Create(GetLightStateAsync),
            AIFunctionFactory.Create(SetLightStateAsync)
            ];
    }

    /// <summary>
    /// Initialize the plugin and cache light entities
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing LightControlPlugin and caching light entities...");

        // Resolve the embedding generator from the provider system
        _embeddingService = await _embeddingResolver.ResolveAsync(ct: cancellationToken).ConfigureAwait(false);
        if (_embeddingService is null)
        {
            _logger.LogWarning("No embedding provider configured — light semantic search will not be available. " +
                "Configure an Embedding provider in Model Providers to enable this feature.");
            return;
        }

        await RefreshLightCacheAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("LightControlPlugin initialized with {LightCount} light entities", _cachedLights.Length);
    }

    /// <summary>
    /// Re-resolves the embedding generator using the specified provider name.
    /// Called by the owning agent when the embedding configuration changes.
    /// </summary>
    public async Task UpdateEmbeddingProviderAsync(string? providerName, CancellationToken cancellationToken = default)
    {
        _embeddingService = await _embeddingResolver.ResolveAsync(providerName, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("LightControlSkill: embedding provider updated to '{Provider}'", providerName ?? "system-default");
    }

    [Description("Find light(s) in an area by name of the Area")]
    public async Task<string> FindLightsByAreaAsync(
        [Description("The Area name (e.g 'kitchen', 'office', 'main bedroom')")] string area)
    {
        await EnsureCacheIsCurrentAsync().ConfigureAwait(false);

        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.area", area);
        var start = Stopwatch.GetTimestamp();
        LightSearchRequests.Add(1);

        try
        {
            // Delegate area resolution to the shared entity location service
            var locationMatches = await _locationService.FindEntitiesByLocationAsync(
                area, (IReadOnlyList<string>)["light", "switch"]).ConfigureAwait(false);

            if (locationMatches.Count == 0)
            {
                LightSearchFailures.Add(1, [
                    new KeyValuePair<string, object?>("reason", "no-area-match")
                ]);
                return $"No areas found matching '{area}'.";
            }

            // When the light device cache is populated, intersect with it for capability info.
            // When it's empty (e.g. no embedding provider yet), fall back to location service
            // results directly so area-based searches still work.
            var matchedEntityIds = locationMatches.Select(e => e.EntityId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var areaLights = _cachedLights.Where(l => matchedEntityIds.Contains(l.EntityId)).ToList();

            if (areaLights.Count == 0 && _cachedLights.IsEmpty)
            {
                // Light device cache is empty — fall back to location service entities directly
                activity?.SetTag("match.fallback", true);
                return await BuildAreaResponseFromLocationServiceAsync(area, locationMatches, activity)
                    .ConfigureAwait(false);
            }

            if (areaLights.Count == 0)
            {
                LightSearchFailures.Add(1, [
                    new KeyValuePair<string, object?>("reason", "no-lights-in-area")
                ]);
                return $"Found location matching '{area}' but it contains no lights.";
            }

            activity?.SetTag("match.type", "area");
            activity?.SetTag("match.light_count", areaLights.Count);

            var stringBuilder = new StringBuilder();

            // Group by area for nice output
            var lightsByArea = areaLights.GroupBy(l => l.Area ?? "Unknown");
            foreach (var group in lightsByArea)
            {
                stringBuilder.AppendLine($"Found {group.Count()} light(s) in area '{group.Key}':");
                foreach (var light in group)
                {
                    var state = await _homeAssistantClient.GetEntityStateAsync(light.EntityId)
                        .ConfigureAwait(false);
                    if (state is null) continue;

                    var capabilities = GetCapabilityDescription(light);
                    stringBuilder.Append($"- {light.FriendlyName} (Entity ID: {light.EntityId}){capabilities}, State: {state.State}");

                    if (state.Attributes.TryGetValue("brightness", out var brightnessObj))
                    {
                        if (int.TryParse(brightnessObj?.ToString(), out var brightness))
                        {
                            var brightnessPercent = (int)Math.Round(brightness / 255.0 * 100);
                            stringBuilder.Append($" at {brightnessPercent}% brightness");
                        }
                    }

                    stringBuilder.AppendLine();
                }
            }

            LightSearchSuccess.Add(1);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return stringBuilder.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding lights in area: {Area}", area);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LightSearchFailures.Add(1, [
                new KeyValuePair<string, object?>("reason", "exception")
            ]);
            return $"Error searching for light: {ex.Message}";
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            LightSearchDurationMs.Record(elapsedMs);
            activity?.SetTag("search.elapsed_ms", elapsedMs);
        }
    }

    [Description("Find a light entity by name or description using natural language")]
    public async Task<string> FindLightAsync(
        [Description("Name or description of the light (e.g., 'living room light', 'kitchen ceiling', 'bedroom lamp')")] string searchTerm)
    {
        await EnsureCacheIsCurrentAsync().ConfigureAwait(false);

        if (_cachedLights.IsEmpty)
        {
            LightSearchFailures.Add(1, [
                new KeyValuePair<string, object?>("reason", "empty-cache")
            ]);
            return "No lights available in the system.";
        }

        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.term", searchTerm);
        var start = Stopwatch.GetTimestamp();
        LightSearchRequests.Add(1);

        try
        {
            if (_embeddingService is null)
            {
                LightSearchFailures.Add(1, [
                    new KeyValuePair<string, object?>("reason", "no-embedding-provider")
                ]);
                return "Embedding provider not available for light search.";
            }

            // Generate embedding for the search term
            var searchEmbedding = await _embeddingService.GenerateAsync(searchTerm).ConfigureAwait(false);
            activity?.SetTag("search.embedding.dimension", searchEmbedding.Vector.Length);

            // Search light names by embedding similarity
            const double similarityThreshold = 0.6;
            var matchingLights = _cachedLights
                .Select(light => new { Light = light, Similarity = _similarity.ComputeSimilarity(searchEmbedding, light.NameEmbedding) })
                .Where(x => x.Similarity >= similarityThreshold)
                .OrderByDescending(x => x.Similarity)
                .ToList();

            if (matchingLights.Count > 0)
            {
                activity?.SetTag("match.type", "light");
                activity?.SetTag("match.count", matchingLights.Count);
                activity?.SetTag("match.top_similarity", matchingLights[0].Similarity);

                var stringBuilder = new StringBuilder();
                if (matchingLights.Count == 1)
                    stringBuilder.Append("Found light: ");
                else
                    stringBuilder.AppendLine($"Found {matchingLights.Count} matching light(s):");

                foreach (var match in matchingLights)
                {
                    var light = match.Light;
                    var capabilities = GetCapabilityDescription(light);
                    var state = await _homeAssistantClient.GetEntityStateAsync(light.EntityId)
                        .ConfigureAwait(false);
                    if (state is null) continue;

                    var entry = matchingLights.Count == 1
                        ? $"{light.FriendlyName} (Entity ID: {light.EntityId}){capabilities}, State: {state.State}"
                        : $"- {light.FriendlyName} (Entity ID: {light.EntityId}){capabilities}, State: {state.State}";
                    stringBuilder.Append(entry);

                    if (state.Attributes.TryGetValue("brightness", out var brightnessObj) &&
                        int.TryParse(brightnessObj?.ToString(), out var brightness))
                    {
                        stringBuilder.Append($" at {(int)Math.Round(brightness / 255.0 * 100)}% brightness");
                    }

                    if (state.Attributes.TryGetValue("color_temp", out var colorTempObj))
                        stringBuilder.Append($" with color temperature {colorTempObj}");

                    if (matchingLights.Count > 1) stringBuilder.AppendLine();
                }

                LightSearchSuccess.Add(1);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return stringBuilder.ToString().TrimEnd();
            }

            // Fallback: try area-based search via the location service
            var locationMatches = await _locationService.FindEntitiesByLocationAsync(
                searchTerm, (IReadOnlyList<string>)["light", "switch"]).ConfigureAwait(false);

            if (locationMatches.Count > 0)
            {
                var matchedEntityIds = locationMatches.Select(e => e.EntityId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var areaLights = _cachedLights.Where(l => matchedEntityIds.Contains(l.EntityId)).ToList();

                if (areaLights.Count > 0)
                {
                    activity?.SetTag("match.type", "area");
                    activity?.SetTag("match.light_count", areaLights.Count);

                    var stringBuilder = new StringBuilder();
                    stringBuilder.AppendLine($"Found {areaLights.Count} light(s) matching location '{searchTerm}':");
                    foreach (var light in areaLights)
                    {
                        var state = await _homeAssistantClient.GetEntityStateAsync(light.EntityId)
                            .ConfigureAwait(false);
                        if (state is null) continue;
                        var capabilities = GetCapabilityDescription(light);
                        stringBuilder.Append($"- {light.FriendlyName} (Entity ID: {light.EntityId}){capabilities}, State: {state.State}");
                        if (state.Attributes.TryGetValue("brightness", out var brightnessObj) &&
                            int.TryParse(brightnessObj?.ToString(), out var brightness))
                        {
                            stringBuilder.Append($" at {(int)Math.Round(brightness / 255.0 * 100)}% brightness");
                        }
                        stringBuilder.AppendLine();
                    }

                    LightSearchSuccess.Add(1);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return stringBuilder.ToString().TrimEnd();
                }
            }

            _logger.LogWarning("No light or area met similarity threshold for term {SearchTerm}", searchTerm);
            activity?.SetTag("match.failure_reason", "below-threshold");
            LightSearchFailures.Add(1, [
                new KeyValuePair<string, object?>("reason", "below-threshold")
            ]);

            return $"No light or area found matching '{searchTerm}'. Available lights: {string.Join(", ", _cachedLights.Take(5).Select(l => l.FriendlyName))}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding light for search term: {SearchTerm}", searchTerm);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LightSearchFailures.Add(1, [
                new KeyValuePair<string, object?>("reason", "exception")
            ]);
            return $"Error searching for light: {ex.Message}";
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            LightSearchDurationMs.Record(elapsedMs);
            activity?.SetTag("search.elapsed_ms", elapsedMs);
        }
    }

    [Description("Get the current state of a specific light entity")]
    public async Task<string> GetLightStateAsync(
        [Description("The entity ID of the light (e.g., 'light.living_room' or 'switch.kitchen_light')")] string entityId)
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity.id", entityId);
        LightStateQueries.Add(1);
        var start = Stopwatch.GetTimestamp();

        // Resolve friendly name for user-facing responses
        var displayName = _cachedLights.FirstOrDefault(l => l.EntityId == entityId)?.FriendlyName ?? entityId;

        try
        {
            _logger.LogDebug("Getting state for light: {EntityId}", entityId);

            var state = await _homeAssistantClient.GetEntityStateAsync(entityId)
                .ConfigureAwait(false);

            if (state == null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "state-not-found");
                LightStateFailures.Add(1, [
                    new KeyValuePair<string, object?>("reason", "state-not-found")
                ]);
                return $"Light '{displayName}' not found or unavailable.";
            }

            var isOn = state.State == "on";
            var result = $"Light '{displayName}' is {state.State}";

            if (isOn)
            {
                // Check for brightness
                if (state.Attributes.TryGetValue("brightness", out var brightnessObj))
                {
                    if (int.TryParse(brightnessObj?.ToString(), out var brightness))
                    {
                        var brightnessPercent = (int)Math.Round(brightness / 255.0 * 100);
                        result += $" at {brightnessPercent}% brightness";
                    }
                }

                // Check for color temperature
                if (state.Attributes.TryGetValue("color_temp", out var colorTempObj))
                {
                    result += $" with color temperature {colorTempObj}";
                }
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting light state for {EntityId}", entityId);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().Name);
            activity?.SetTag("exception.message", ex.Message);
            LightStateFailures.Add(1, [
                new KeyValuePair<string, object?>("reason", "exception")
            ]);
            return $"Failed to get state for light '{displayName}': {ex.Message}";
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            activity?.SetTag("elapsed_ms", elapsedMs);
            LightStateDurationMs.Record(elapsedMs);
        }
    }

    [Description("Control a light - turn on/off, set brightness, or change color")]
    public async Task<string> SetLightStateAsync(
        [Description("The entity ID of the light (e.g., 'light.living_room')")] string entityId,
        [Description("Desired state: 'on' or 'off'")] string state,
        [Description("Brightness 0-100 for 'on' state; use -1 to omit")] int brightness = -1,
        [Description("Color name like 'red', 'blue', 'warm_white'; use empty string to omit")] string color = "")
    {
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("entity.id", entityId);
        activity?.SetTag("desired.state", state);
        if (brightness >= 0)
        {
            activity?.SetTag("desired.brightness", brightness);
        }
        if (!string.IsNullOrEmpty(color))
        {
            activity?.SetTag("desired.color", color);
        }

        LightControlRequests.Add(1);
        var start = Stopwatch.GetTimestamp();

        try
        {
            _logger.LogDebug("Setting light {EntityId} to {State}, brightness: {Brightness}, color: {Color}",
                entityId, state, brightness, color);

            // Resolve friendly name for user-facing responses
            var displayName = _cachedLights.FirstOrDefault(l => l.EntityId == entityId)?.FriendlyName ?? entityId;

            var request = new ServiceCallRequest
            {
                ["entity_id"] = entityId
            };

            var isSwitch = entityId.StartsWith("switch.");
            var domain = isSwitch ? "switch" : "light";

            if (state.ToLower() == "off")
            {
                await _homeAssistantClient.CallServiceAsync(domain, "turn_off", parameters: null, request).ConfigureAwait(false);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return $"Light '{displayName}' turned off successfully.";
            }
            else if (state.ToLower() == "on")
            {
                if (brightness >= 0 && !isSwitch)
                {
                    var haBrightness = Math.Max(1, Math.Min(255, (int)Math.Round(brightness / 100.0 * 255)));
                    request["brightness"] = haBrightness;
                }

                if (!string.IsNullOrEmpty(color) && !isSwitch)
                {
                    request["color_name"] = color;
                }

                await _homeAssistantClient.CallServiceAsync(domain, "turn_on", parameters: null, request).ConfigureAwait(false);

                var result = $"Light '{displayName}' turned on successfully";
                if (brightness >= 0 && !isSwitch)
                {
                    result += $" at {brightness}% brightness";
                }
                if (!string.IsNullOrEmpty(color) && !isSwitch)
                {
                    result += $" with {color} color";
                }
                activity?.SetStatus(ActivityStatusCode.Ok);
                return result + ".";
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, "invalid-state");
                LightControlFailures.Add(1, [
                    new KeyValuePair<string, object?>("reason", "invalid-state")
                ]);
                return $"Invalid state '{state}'. Use 'on' or 'off'.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting light state for {EntityId}", entityId);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().Name);
            activity?.SetTag("exception.message", ex.Message);
            LightControlFailures.Add(1, [
                new KeyValuePair<string, object?>("reason", "exception")
            ]);
            return $"Failed to control light '{entityId}': {ex.Message}";
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            LightControlDurationMs.Record(elapsedMs);
            activity?.SetTag("elapsed_ms", elapsedMs);
        }
    }

    /// <summary>
    /// Fallback path when the light device cache is empty (e.g. no embedding provider at startup).
    /// Uses entity location service results directly + live HA state to produce a response,
    /// without requiring the embedding-dependent device cache.
    /// </summary>
    private async Task<string> BuildAreaResponseFromLocationServiceAsync(
        string area,
        IReadOnlyList<EntityLocationInfo> locationMatches,
        Activity? activity)
    {
        _logger.LogInformation(
            "Light device cache empty — using location service fallback for area '{Area}' ({Count} entities)",
            area, locationMatches.Count);

        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine($"Found {locationMatches.Count} light(s) in area '{area}':");

        foreach (var entity in locationMatches)
        {
            var state = await _homeAssistantClient.GetEntityStateAsync(entity.EntityId)
                .ConfigureAwait(false);
            if (state is null) continue;

            stringBuilder.Append($"- {entity.FriendlyName} (Entity ID: {entity.EntityId}), State: {state.State}");

            if (state.Attributes.TryGetValue("brightness", out var brightnessObj))
            {
                if (int.TryParse(brightnessObj?.ToString(), out var brightness))
                {
                    var brightnessPercent = (int)Math.Round(brightness / 255.0 * 100);
                    stringBuilder.Append($" at {brightnessPercent}% brightness");
                }
            }

            stringBuilder.AppendLine();
        }

        activity?.SetTag("match.type", "area_fallback");
        activity?.SetTag("match.light_count", locationMatches.Count);
        LightSearchSuccess.Add(1);
        activity?.SetStatus(ActivityStatusCode.Ok);

        return stringBuilder.ToString().TrimEnd();
    }

    private async Task RefreshLightCacheAsync(CancellationToken cancellationToken = default)
    {
        if (_embeddingService is null)
        {
            _logger.LogWarning("Skipping light cache refresh — no embedding provider available.");
            return;
        }

        using var activity = ActivitySource.StartActivity();
        var start = Stopwatch.GetTimestamp();
        try
        {
            // Try Redis cache first (light device data only — areas come from IEntityLocationService)
            var cachedLights = await _deviceCache.GetCachedLightsAsync(cancellationToken).ConfigureAwait(false);
            if (cachedLights is not null)
            {
                _logger.LogInformation("Loaded {LightCount} lights from Redis cache", cachedLights.Count);
                var newLightsFromCache = new List<LightEntity>();
                
                var allEmbeddingsFound = true;
                foreach (var light in cachedLights)
                {
                    var embedding = await _deviceCache.GetEmbeddingAsync($"light:{light.EntityId}", cancellationToken).ConfigureAwait(false);
                    if (embedding is not null)
                    {
                        light.NameEmbedding = embedding;
                        newLightsFromCache.Add(light);
                    }
                    else
                    {
                        _logger.LogWarning("Missing embedding for light {EntityId} in Redis, will re-fetch from HA", light.EntityId);
                        allEmbeddingsFound = false;
                        break;
                    }
                }
                
                if (allEmbeddingsFound && newLightsFromCache.Count == cachedLights.Count)
                {
                    _cachedLights = [.. newLightsFromCache];
                    Volatile.Write(ref _lastCacheUpdateTicks, DateTime.UtcNow.Ticks);
                    return;
                }
            }

            _logger.LogDebug("Refreshing light cache...");

            var allStates = await _homeAssistantClient.GetAllEntityStatesAsync(cancellationToken)
                .ConfigureAwait(false);

            var statesList = allStates.ToList();
            _logger.LogInformation("Received {EntityCount} entities from Home Assistant state API", statesList.Count);

            // Filter for lights and switches with "light" in friendly name
            var lightEntities = statesList
                .Where(s => s.EntityId.StartsWith("light.") ||
                           (s.EntityId.StartsWith("switch.") &&
                            s.Attributes.TryGetValue("friendly_name", out var nameObj) &&
                            nameObj.ToString()?.ToLower().Contains("light", StringComparison.CurrentCultureIgnoreCase) == true))
                .ToList();

            var newLights = new List<LightEntity>();

            foreach (var entity in lightEntities)
            {
                var friendlyName = entity.Attributes.TryGetValue("friendly_name", out var nameObj)
                    ? nameObj.ToString() ?? entity.EntityId
                    : entity.EntityId;

                // Parse supported color modes
                var colorModes = SupportedColorModes.None;
                if (entity.Attributes.TryGetValue("supported_color_modes", out var modesObj))
                {
                    var modesArray = JsonSerializer.Deserialize<string[]>(modesObj.ToString() ?? "[]");
                    if (modesArray != null)
                    {
                        colorModes = modesArray.Aggregate(colorModes, (current, mode) => current | mode switch
                        {
                            "brightness" => SupportedColorModes.Brightness,
                            "color_temp" => SupportedColorModes.ColorTemp,
                            "hs" => SupportedColorModes.Hs,
                            _ => SupportedColorModes.None
                        });
                    }
                }

                // Resolve area from the shared entity location service
                var areaInfo = _locationService.GetAreaForEntity(entity.EntityId);

                // Generate embedding for the friendly name
                var embedding = await _embeddingService.GenerateAsync(friendlyName, cancellationToken: cancellationToken).ConfigureAwait(false);

                var lightEntity = new LightEntity
                {
                    EntityId = entity.EntityId,
                    FriendlyName = friendlyName,
                    SupportedColorModes = colorModes,
                    NameEmbedding = embedding,
                    Area = areaInfo?.Name
                };

                newLights.Add(lightEntity);
            }

            // Atomically swap
            _cachedLights = [.. newLights];
            Volatile.Write(ref _lastCacheUpdateTicks, DateTime.UtcNow.Ticks);

            // Save light device data to Redis
            var deviceCacheTtl = TimeSpan.FromMinutes(30);
            var embeddingCacheTtl = TimeSpan.FromHours(24);
            await _deviceCache.SetCachedLightsAsync(newLights.ToList(), deviceCacheTtl, cancellationToken).ConfigureAwait(false);
            foreach (var light in newLights.Where(l => l.NameEmbedding is not null))
            {
                await _deviceCache.SetEmbeddingAsync($"light:{light.EntityId}", light.NameEmbedding!, embeddingCacheTtl, cancellationToken).ConfigureAwait(false);
            }
            _logger.LogInformation("Saved {LightCount} lights to Redis cache", newLights.Count);

            activity?.SetTag("cache.size", newLights.Count);
            _logger.LogDebug("Light cache refreshed with {Count} entities", newLights.Count);

            if (newLights.Count == 0)
            {
                _logger.LogWarning("No light entities were cached. Inspect trace logs for Home Assistant state payload details.");
            }
            else
            {
                _logger.LogInformation("Cached {LightCount} lights: {Lights}", 
                    newLights.Count,
                    JsonSerializer.Serialize(newLights.Select(l => new
                    {
                        l.EntityId,
                        l.FriendlyName,
                        l.Area,
                        Capabilities = l.SupportedColorModes.ToString()
                    })));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing light cache");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().Name);
            activity?.SetTag("exception.message", ex.Message);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            CacheRefreshDurationMs.Record(elapsedMs);
            activity?.SetTag("elapsed_ms", elapsedMs);
            activity?.AddEvent(new ActivityEvent("cache.refresh.completed", tags: new ActivityTagsCollection
            {
                { "elapsed_ms", elapsedMs },
                { "cache.size", _cachedLights.Length }
            }));
        }
    }

    private async Task EnsureCacheIsCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (DateTime.UtcNow - new DateTime(Volatile.Read(ref _lastCacheUpdateTicks), DateTimeKind.Utc) > _cacheRefreshInterval)
        {
            if (!await _refreshLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                return; // Another refresh is already in progress
            try
            {
                // Double-check after acquiring the lock
                if (DateTime.UtcNow - new DateTime(Volatile.Read(ref _lastCacheUpdateTicks), DateTimeKind.Utc) > _cacheRefreshInterval)
                {
                    await RefreshLightCacheAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _refreshLock.Release();
            }
        }
    }

    private static string GetCapabilityDescription(LightEntity light)
    {
        if (light.IsSwitch)
            return " (switch - on/off only)";

        var capabilities = new List<string>();
        if (light.SupportedColorModes.HasFlag(SupportedColorModes.Brightness))
            capabilities.Add("brightness");
        if (light.SupportedColorModes.HasFlag(SupportedColorModes.ColorTemp))
            capabilities.Add("color temperature");
        if (light.SupportedColorModes.HasFlag(SupportedColorModes.Hs))
            capabilities.Add("color");

        return capabilities.Any() ? $" (supports: {string.Join(", ", capabilities)})" : "";
    }

}
