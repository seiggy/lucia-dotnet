using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using lucia.HomeAssistant.Services;
using lucia.HomeAssistant.Models;
using lucia.Agents.Models;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Skills;

/// <summary>
/// Semantic Kernel plugin for Home Assistant light control with caching and similarity search
/// </summary>
public class LightControlSkill : IAgentSkill
{
    private const string? ReturnResponseToken = "return_response";
    private readonly IHomeAssistantClient _homeAssistantClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingService;
    private readonly ILogger<LightControlSkill> _logger;
    private readonly List<LightEntity> _cachedLights = new();
    private readonly Dictionary<string, Embedding<float>> _areaEmbeddings = new();
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private readonly TimeSpan _cacheRefreshInterval = TimeSpan.FromMinutes(30);

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
        IEmbeddingGenerator<string, Embedding<float>> embeddingService,
        ILogger<LightControlSkill> logger)
    {
        _homeAssistantClient = homeAssistantClient;
        _embeddingService = embeddingService;
        _logger = logger;
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
        await RefreshLightCacheAsync(cancellationToken);
        _logger.LogInformation("LightControlPlugin initialized with {LightCount} light entities", _cachedLights.Count);
    }

    [Description("Find light(s) in an area by name of the Area")]
    public async Task<string> FindLightsByAreaAsync(
        [Description("The Area name (e.g 'kitchen', 'office', 'main bedroom')")] string area)
    {
        await EnsureCacheIsCurrentAsync();

        if (!_cachedLights.Any())
        {
            LightSearchFailures.Add(1, [
                new KeyValuePair<string, object?>("reason", "empty-cache")
            ]);
            return "No lights available in the system";
        }
        
        using var activity = ActivitySource.StartActivity(nameof(FindLightsByAreaAsync), ActivityKind.Internal);
        activity?.SetTag("search.area", area);
        var start = Stopwatch.GetTimestamp();
        LightSearchRequests.Add(1);

        try
        {
            var searchEmbedding = await _embeddingService.GenerateAsync(area);
            activity?.SetTag("search.embedding.dimension", searchEmbedding.Vector.Length);
            
            var areaMatches = _areaEmbeddings
                .Select(kvp => new
                {
                    AreaName = kvp.Key,
                    Similarity = CosineSimilarity(searchEmbedding, kvp.Value),
                    MatchType = "area"
                })
                .ToList();
            
            var bestAreaMatch = areaMatches.OrderByDescending(x => x.Similarity).FirstOrDefault();
            
            // Return all lights from the matched area
            var areaLights = _cachedLights.Where(l => l.Area == bestAreaMatch.AreaName).ToList();
            
            if (!areaLights.Any())
            {
                _logger.LogWarning("Area {AreaName} matched but contains no lights", bestAreaMatch.AreaName);
                activity?.SetTag("match.failure_reason", "no-lights-in-area");
                LightSearchFailures.Add(1, [
                    new KeyValuePair<string, object?>("reason", "no-lights-in-area")
                ]);
                return $"Found area '{bestAreaMatch.AreaName}' but it contains no lights.";
            }

            _logger.LogDebug("Found area match: {AreaName} with similarity {Similarity:F3}, containing {LightCount} lights",
                bestAreaMatch.AreaName, bestAreaMatch.Similarity, areaLights.Count);
            activity?.SetTag("match.type", "area");
            activity?.SetTag("match.area_name", bestAreaMatch.AreaName);
            activity?.SetTag("match.similarity", bestAreaMatch.Similarity);
            activity?.SetTag("match.light_count", areaLights.Count);

            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine($"Found {areaLights.Count} light(s) in area '{bestAreaMatch.AreaName}':");
            
            foreach (var light in areaLights)
            {
                var state = await _homeAssistantClient.GetEntityStateAsync(light.EntityId)
                    .ConfigureAwait(false);
                if (state is null) continue;

                var capabilities = GetCapabilityDescription(light);
                stringBuilder.Append($"- {light.FriendlyName} (Entity ID: {light.EntityId}){capabilities}, State: {state.State}");

                // Check for brightness
                if (state.Attributes.TryGetValue("brightness", out var brightnessObj))
                {
                    if (JsonSerializer.Deserialize<int?>(brightnessObj?.ToString() ?? "null") is { } brightness)
                    {
                        var brightnessPercent = (int)Math.Round(brightness / 255.0 * 100);
                        stringBuilder.Append($" at {brightnessPercent}% brightness");
                    }
                }

                stringBuilder.AppendLine();
            }

            LightSearchSuccess.Add(1);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return stringBuilder.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding lights in area: {Area}", area);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().Name);
            activity?.SetTag("exception.message", ex.Message);
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
            activity?.AddEvent(new ActivityEvent("search.completed", tags: new ActivityTagsCollection
            {
                { "elapsed_ms", elapsedMs },
                { "cache.size", _cachedLights.Count },
                { "area.count", _areaEmbeddings.Count }
            }));
        }
    }

    [Description("Find a light entity by name or description using natural language")]
    public async Task<string> FindLightAsync(
        [Description("Name or description of the light (e.g., 'living room light', 'kitchen ceiling', 'bedroom lamp')")] string searchTerm)
    {
        await EnsureCacheIsCurrentAsync();

        if (!_cachedLights.Any())
        {
            LightSearchFailures.Add(1, [
                new KeyValuePair<string, object?>("reason", "empty-cache")
            ]);
            return "No lights available in the system.";
        }

        using var activity = ActivitySource.StartActivity("FindLight", ActivityKind.Internal);
        activity?.SetTag("search.term", searchTerm);
        var start = Stopwatch.GetTimestamp();
        LightSearchRequests.Add(1);

        try
        {
            // Generate embedding for the search term
            var searchEmbedding = await _embeddingService.GenerateAsync(searchTerm);
            activity?.SetTag("search.embedding.dimension", searchEmbedding.Vector.Length);

            // Search both light names and area names
            var lightMatches = _cachedLights
                .Select(light => new
                {
                    Light = light,
                    Similarity = CosineSimilarity(searchEmbedding, light.NameEmbedding),
                    MatchType = "light"
                })
                .ToList();

            var areaMatches = _areaEmbeddings
                .Select(kvp => new
                {
                    AreaName = kvp.Key,
                    Similarity = CosineSimilarity(searchEmbedding, kvp.Value),
                    MatchType = "area"
                })
                .ToList();

            // Find the best overall match (light or area)
            var bestLightMatch = lightMatches.OrderByDescending(x => x.Similarity).FirstOrDefault();
            var bestAreaMatch = areaMatches.OrderByDescending(x => x.Similarity).FirstOrDefault();

            const double similarityThreshold = 0.6;

            // Determine which match is better
            var useLightMatch = bestLightMatch != null && 
                                (bestAreaMatch == null || bestLightMatch.Similarity >= bestAreaMatch.Similarity);

            if (useLightMatch && bestLightMatch!.Similarity >= similarityThreshold)
            {
                // Return specific light match
                var light = bestLightMatch.Light;
                _logger.LogDebug("Found light match: {EntityId} ({FriendlyName}) with similarity {Similarity:F3}",
                    light.EntityId, light.FriendlyName, bestLightMatch.Similarity);
                activity?.SetTag("match.type", "light");
                activity?.SetTag("match.entity_id", light.EntityId);
                activity?.SetTag("match.friendly_name", light.FriendlyName);
                activity?.SetTag("match.similarity", bestLightMatch.Similarity);

                var capabilities = GetCapabilityDescription(light);
                var state = await _homeAssistantClient.GetEntityStateAsync(light.EntityId)
                    .ConfigureAwait(false);
                if (state is null)
                {
                    _logger.LogWarning("Light {EntityId} not found when retrieving state after match", light.EntityId);
                    activity?.SetStatus(ActivityStatusCode.Error, "state-missing");
                    LightSearchFailures.Add(1, [
                        new KeyValuePair<string, object?>("reason", "state-missing")
                    ]);
                    return $"Light '{light.EntityId}' was matched but could not be retrieved from Home Assistant.";
                }

                var stringBuilder = new StringBuilder();
                stringBuilder.Append($"Found light: {light.FriendlyName} (Entity ID: {light.EntityId}){capabilities}, State: {state.State}");

                // Check for brightness
                if (state.Attributes.TryGetValue("brightness", out var brightnessObj))
                {
                    if (JsonSerializer.Deserialize<int?>(brightnessObj?.ToString() ?? "null") is { } brightness)
                    {
                        var brightnessPercent = (int)Math.Round(brightness / 255.0 * 100);
                        stringBuilder.Append($" at {brightnessPercent}% brightness");
                    }
                }

                // Check for color temperature
                if (state.Attributes.TryGetValue("color_temp", out var colorTempObj))
                {
                    stringBuilder.Append($" with color temperature {colorTempObj}");
                }

                LightSearchSuccess.Add(1);
                activity?.SetStatus(ActivityStatusCode.Ok);

                return stringBuilder.ToString();
            }
            else if (bestAreaMatch != null && bestAreaMatch.Similarity >= similarityThreshold)
            {
                // Return all lights from the matched area
                var areaLights = _cachedLights.Where(l => l.Area == bestAreaMatch.AreaName).ToList();
                
                if (!areaLights.Any())
                {
                    _logger.LogWarning("Area {AreaName} matched but contains no lights", bestAreaMatch.AreaName);
                    activity?.SetTag("match.failure_reason", "no-lights-in-area");
                    LightSearchFailures.Add(1, [
                        new KeyValuePair<string, object?>("reason", "no-lights-in-area")
                    ]);
                    return $"Found area '{bestAreaMatch.AreaName}' but it contains no lights.";
                }

                _logger.LogDebug("Found area match: {AreaName} with similarity {Similarity:F3}, containing {LightCount} lights",
                    bestAreaMatch.AreaName, bestAreaMatch.Similarity, areaLights.Count);
                activity?.SetTag("match.type", "area");
                activity?.SetTag("match.area_name", bestAreaMatch.AreaName);
                activity?.SetTag("match.similarity", bestAreaMatch.Similarity);
                activity?.SetTag("match.light_count", areaLights.Count);

                var stringBuilder = new StringBuilder();
                stringBuilder.AppendLine($"Found {areaLights.Count} light(s) in area '{bestAreaMatch.AreaName}':");
                
                foreach (var light in areaLights)
                {
                    var state = await _homeAssistantClient.GetEntityStateAsync(light.EntityId)
                        .ConfigureAwait(false);
                    if (state is null) continue;

                    var capabilities = GetCapabilityDescription(light);
                    stringBuilder.Append($"- {light.FriendlyName} (Entity ID: {light.EntityId}){capabilities}, State: {state.State}");

                    // Check for brightness
                    if (state.Attributes.TryGetValue("brightness", out var brightnessObj))
                    {
                        if (JsonSerializer.Deserialize<int?>(brightnessObj?.ToString() ?? "null") is { } brightness)
                        {
                            var brightnessPercent = (int)Math.Round(brightness / 255.0 * 100);
                            stringBuilder.Append($" at {brightnessPercent}% brightness");
                        }
                    }

                    stringBuilder.AppendLine();
                }

                LightSearchSuccess.Add(1);
                activity?.SetStatus(ActivityStatusCode.Ok);

                return stringBuilder.ToString().TrimEnd();
            }

            var topLightCandidates = string.Join(", ", lightMatches
                .OrderByDescending(m => m.Similarity)
                .Take(3)
                .Select(m => $"{m.Light.FriendlyName} ({m.Similarity:F2})"));

            var topAreaCandidates = string.Join(", ", areaMatches
                .OrderByDescending(m => m.Similarity)
                .Take(3)
                .Select(m => $"{m.AreaName} ({m.Similarity:F2})"));

            _logger.LogWarning("No light or area met similarity threshold for term {SearchTerm}. Top light candidates: {LightCandidates}, Top area candidates: {AreaCandidates}",
                searchTerm, topLightCandidates, topAreaCandidates);
            activity?.SetTag("match.failure_reason", "below-threshold");
            activity?.SetTag("match.top_light_candidates", topLightCandidates);
            activity?.SetTag("match.top_area_candidates", topAreaCandidates);
            LightSearchFailures.Add(1, [
                new KeyValuePair<string, object?>("reason", "below-threshold")
            ]);

            return $"No light or area found matching '{searchTerm}'. Available lights: {string.Join(", ", _cachedLights.Take(5).Select(l => l.FriendlyName))}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding light for search term: {SearchTerm}", searchTerm);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().Name);
            activity?.SetTag("exception.message", ex.Message);
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
            activity?.AddEvent(new ActivityEvent("search.completed", tags: new ActivityTagsCollection
            {
                { "elapsed_ms", elapsedMs },
                { "cache.size", _cachedLights.Count },
                { "area.count", _areaEmbeddings.Count }
            }));
        }
    }

    [Description("Get the current state of a specific light entity")]
    public async Task<string> GetLightStateAsync(
        [Description("The entity ID of the light (e.g., 'light.living_room' or 'switch.kitchen_light')")] string entityId)
    {
        using var activity = ActivitySource.StartActivity("GetLightState", ActivityKind.Internal);
        activity?.SetTag("entity.id", entityId);
        LightStateQueries.Add(1);
        var start = Stopwatch.GetTimestamp();

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
                return $"Light '{entityId}' not found or unavailable.";
            }

            var isOn = state.State == "on";
            var result = $"Light '{entityId}' is {state.State}";

            if (isOn)
            {
                // Check for brightness
                if (state.Attributes.TryGetValue("brightness", out var brightnessObj))
                {
                    if (JsonSerializer.Deserialize<int?>(brightnessObj.ToString() ?? "null") is { } brightness)
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

            // Use friendly name if available
            if (state.Attributes.TryGetValue("friendly_name", out var friendlyNameObj))
            {
                var friendlyName = friendlyNameObj.ToString();
                if (!string.IsNullOrEmpty(friendlyName))
                {
                    result = result.Replace($"'{entityId}'", $"'{friendlyName}'");
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
            return $"Failed to get state for light '{entityId}': {ex.Message}";
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
        [Description("Brightness level from 0-100 (optional, only for 'on' state)")] int? brightness = null,
        [Description("Color name like 'red', 'blue', 'warm_white' (optional)")] string? color = null)
    {
        using var activity = ActivitySource.StartActivity("SetLightState", ActivityKind.Internal);
        activity?.SetTag("entity.id", entityId);
        activity?.SetTag("desired.state", state);
        if (brightness.HasValue)
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

            var request = new ServiceCallRequest
            {
                ["entity_id"] = entityId
            };

            var isSwitch = entityId.StartsWith("switch.");
            var domain = isSwitch ? "switch" : "light";

            if (state.ToLower() == "off")
            {
                await _homeAssistantClient.CallServiceAsync(domain, "turn_off", ReturnResponseToken, request);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return $"Light '{entityId}' turned off successfully.";
            }
            else if (state.ToLower() == "on")
            {
                if (brightness.HasValue && !isSwitch)
                {
                    var haBrightness = Math.Max(1, Math.Min(255, (int)Math.Round(brightness.Value / 100.0 * 255)));
                    request["brightness"] = haBrightness;
                }

                if (!string.IsNullOrEmpty(color) && !isSwitch)
                {
                    request["color_name"] = color;
                }

                await _homeAssistantClient.CallServiceAsync(domain, "turn_on", ReturnResponseToken, request);

                var result = $"Light '{entityId}' turned on successfully";
                if (brightness.HasValue && !isSwitch)
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

    private async Task RefreshLightCacheAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("RefreshLightCache", ActivityKind.Internal);
        var start = Stopwatch.GetTimestamp();
        try
        {
            _logger.LogDebug("Refreshing light cache...");

            var allStates = await _homeAssistantClient.GetAllEntityStatesAsync(cancellationToken)
                .ConfigureAwait(false);
            
            _logger.LogInformation("Received {EntityCount} entities from Home Assistant state API", allStates.Count());
            foreach (var state in allStates)
            {
                var friendlyName = state.Attributes.TryGetValue("friendly_name", out var nameObj)
                    ? nameObj?.ToString() ?? string.Empty
                    : string.Empty;

                _logger.LogTrace("State payload: EntityId={EntityId}, Domain={Domain}, FriendlyName={FriendlyName}, Attributes={Attributes}",
                    state.EntityId,
                    state.EntityId.Split('.')[0],
                    friendlyName,
                    JsonSerializer.Serialize(state.Attributes));
            }

            // Filter for lights and switches with "light" in friendly name
            var lightEntities = allStates
                .Where(s => s.EntityId.StartsWith("light.") ||
                           (s.EntityId.StartsWith("switch.") &&
                            s.Attributes.TryGetValue("friendly_name", out var nameObj) &&
                            nameObj.ToString()?.ToLower().Contains("light", StringComparison.CurrentCultureIgnoreCase) == true))
                .ToList();

            _cachedLights.Clear();
            _areaEmbeddings.Clear();

            var allEntityDataByArea = await _homeAssistantClient.RunTemplateAsync<List<AreaEntityMap>>(
                    "[{% for id in areas() %}{% if not loop.first %}, {% endif %}{\"area\":\"{{ id }}\",\"entities\":[{% for e in area_entities(id) %}{% if not loop.first %}, {% endif %}\"{{ e }}\"{% endfor %}]}{% endfor %}]",
                    cancellationToken
                );

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
                        foreach (var mode in modesArray)
                        {
                            colorModes |= mode switch
                            {
                                "brightness" => SupportedColorModes.Brightness,
                                "color_temp" => SupportedColorModes.ColorTemp,
                                "hs" => SupportedColorModes.Hs,
                                _ => SupportedColorModes.None
                            };
                        }
                    }
                }

                // find area for the entity
                string? area = null;
                foreach (var areaMap in allEntityDataByArea)
                {
                    if (areaMap.Entities.Contains(entity.EntityId))
                    {
                        area = areaMap.Area;
                        break;
                    }
                }

                // Generate embedding for the friendly name
                var embedding = await _embeddingService.GenerateAsync(friendlyName, cancellationToken: cancellationToken);

                var lightEntity = new LightEntity
                {
                    EntityId = entity.EntityId,
                    FriendlyName = friendlyName,
                    SupportedColorModes = colorModes,
                    NameEmbedding = embedding,
                    Area = area
                };

                _cachedLights.Add(lightEntity);
            }

            // Generate embeddings for all unique areas
            var uniqueAreas = _cachedLights
                .Where(l => !string.IsNullOrEmpty(l.Area))
                .Select(l => l.Area!)
                .Distinct()
                .ToList();

            foreach (var area in uniqueAreas)
            {
                var areaEmbedding = await _embeddingService.GenerateAsync(area, cancellationToken: cancellationToken);
                _areaEmbeddings[area] = areaEmbedding;
            }

            _lastCacheUpdate = DateTime.UtcNow;
            activity?.SetTag("cache.size", _cachedLights.Count);
            activity?.SetTag("cache.area_count", _areaEmbeddings.Count);
            _logger.LogDebug("Light cache refreshed with {Count} entities and {AreaCount} areas", _cachedLights.Count, _areaEmbeddings.Count);

            if (_cachedLights.Count == 0)
            {
                _logger.LogWarning("No light entities were cached. Inspect trace logs for Home Assistant state payload details.");
            }
            else
            {
                _logger.LogInformation("Cached {LightCount} lights across {AreaCount} areas: {Lights}", 
                    _cachedLights.Count, 
                    _areaEmbeddings.Count,
                    JsonSerializer.Serialize(_cachedLights.Select(l => new
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
                { "cache.size", _cachedLights.Count }
            }));
        }
    }

    private async Task EnsureCacheIsCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (DateTime.UtcNow - _lastCacheUpdate > _cacheRefreshInterval)
        {
            await RefreshLightCacheAsync(cancellationToken);
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

    private static double CosineSimilarity(Embedding<float> vector1, Embedding<float> vector2)
    {
        var span1 = vector1.Vector.Span;
        var span2 = vector2.Vector.Span;

        if (span1.Length != span2.Length)
            return 0.0;

        var dotProduct = 0.0;
        var magnitude1 = 0.0;
        var magnitude2 = 0.0;

        for (var i = 0; i < span1.Length; i++)
        {
            dotProduct += span1[i] * span2[i];
            magnitude1 += span1[i] * span1[i];
            magnitude2 += span2[i] * span2[i];
        }

        if (magnitude1 == 0.0 || magnitude2 == 0.0)
            return 0.0;

        return dotProduct / (Math.Sqrt(magnitude1) * Math.Sqrt(magnitude2));
    }
}
