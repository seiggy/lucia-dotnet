using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text.Json;
using lucia.HomeAssistant.Services;
using lucia.HomeAssistant.Models;
using lucia.Agents.Models;
using Microsoft.Extensions.AI;

namespace lucia.Agents.Skills;

/// <summary>
/// Semantic Kernel plugin for Home Assistant light control with caching and similarity search
/// </summary>
public class LightControlSkill
{
    private readonly IHomeAssistantClient _homeAssistantClient;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingService;
    private readonly ILogger<LightControlSkill> _logger;
    private readonly List<LightEntity> _cachedLights = new();
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private readonly TimeSpan _cacheRefreshInterval = TimeSpan.FromMinutes(30);

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

    [Description("Find a light entity by name or description using natural language")]
    public async Task<string> FindLightAsync(
        [Description("Name or description of the light (e.g., 'living room light', 'kitchen ceiling', 'bedroom lamp')")] string searchTerm)
    {
        await EnsureCacheIsCurrentAsync();

        if (!_cachedLights.Any())
        {
            return "No lights available in the system.";
        }

        try
        {
            // Generate embedding for the search term
            var searchEmbedding = await _embeddingService.GenerateAsync(searchTerm);

            // Find the best match using cosine similarity
            var bestMatch = _cachedLights
                .Select(light => new
                {
                    Light = light,
                    Similarity = CosineSimilarity(searchEmbedding, light.NameEmbedding)
                })
                .OrderByDescending(x => x.Similarity)
                .First();

            // Return the best match if similarity is above threshold
            const double similarityThreshold = 0.6;
            if (bestMatch.Similarity >= similarityThreshold)
            {
                _logger.LogDebug("Found light match: {EntityId} ({FriendlyName}) with similarity {Similarity:F3}",
                    bestMatch.Light.EntityId, bestMatch.Light.FriendlyName, bestMatch.Similarity);

                var capabilities = GetCapabilityDescription(bestMatch.Light);
                return $"Found light: {bestMatch.Light.FriendlyName} (Entity ID: {bestMatch.Light.EntityId}){capabilities}";
            }

            return $"No light found matching '{searchTerm}'. Available lights: {string.Join(", ", _cachedLights.Take(5).Select(l => l.FriendlyName))}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding light for search term: {SearchTerm}", searchTerm);
            return $"Error searching for light: {ex.Message}";
        }
    }

    [Description("Get the current state of a specific light entity")]
    public async Task<string> GetLightStateAsync(
        [Description("The entity ID of the light (e.g., 'light.living_room' or 'switch.kitchen_light')")] string entityId)
    {
        try
        {
            _logger.LogDebug("Getting state for light: {EntityId}", entityId);

            var state = await _homeAssistantClient.GetStateAsync(entityId);

            if (state == null)
            {
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

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting light state for {EntityId}", entityId);
            return $"Failed to get state for light '{entityId}': {ex.Message}";
        }
    }

    [Description("Control a light - turn on/off, set brightness, or change color")]
    public async Task<string> SetLightStateAsync(
        [Description("The entity ID of the light (e.g., 'light.living_room')")] string entityId,
        [Description("Desired state: 'on' or 'off'")] string state,
        [Description("Brightness level from 0-100 (optional, only for 'on' state)")] int? brightness = null,
        [Description("Color name like 'red', 'blue', 'warm_white' (optional)")] string? color = null)
    {
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
                await _homeAssistantClient.CallServiceAsync(domain, "turn_off", request);
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

                await _homeAssistantClient.CallServiceAsync(domain, "turn_on", request);

                var result = $"Light '{entityId}' turned on successfully";
                if (brightness.HasValue && !isSwitch)
                {
                    result += $" at {brightness}% brightness";
                }
                if (!string.IsNullOrEmpty(color) && !isSwitch)
                {
                    result += $" with {color} color";
                }
                return result + ".";
            }
            else
            {
                return $"Invalid state '{state}'. Use 'on' or 'off'.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting light state for {EntityId}", entityId);
            return $"Failed to control light '{entityId}': {ex.Message}";
        }
    }

    private async Task RefreshLightCacheAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Refreshing light cache...");

            var allStates = await _homeAssistantClient.GetStatesAsync(cancellationToken);

            // Filter for lights and switches with "light" in friendly name
            var lightEntities = allStates
                .Where(s => s.EntityId.StartsWith("light.") ||
                           (s.EntityId.StartsWith("switch.") &&
                            s.Attributes.TryGetValue("friendly_name", out var nameObj) &&
                            nameObj.ToString()?.ToLower().Contains("light", StringComparison.CurrentCultureIgnoreCase) == true))
                .ToList();

            _cachedLights.Clear();

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

                // Generate embedding for the friendly name
                var embedding = await _embeddingService.GenerateAsync(friendlyName, cancellationToken: cancellationToken);

                var lightEntity = new LightEntity
                {
                    EntityId = entity.EntityId,
                    FriendlyName = friendlyName,
                    SupportedColorModes = colorModes,
                    NameEmbedding = embedding
                };

                _cachedLights.Add(lightEntity);
            }

            _lastCacheUpdate = DateTime.UtcNow;
            _logger.LogDebug("Light cache refreshed with {Count} entities", _cachedLights.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing light cache");
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
