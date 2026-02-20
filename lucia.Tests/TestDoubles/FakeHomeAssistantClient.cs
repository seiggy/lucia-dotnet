using System.Text.Json;
using System.Text.Json.Serialization;
using lucia.HomeAssistant.Models;
using lucia.HomeAssistant.Services;

namespace lucia.Tests.TestDoubles;

/// <summary>
/// In-memory implementation of <see cref="IHomeAssistantClient"/> backed by a JSON
/// snapshot exported from a real Home Assistant instance via
/// <c>scripts/Export-HomeAssistantSnapshot.ps1</c>.
/// </summary>
internal sealed class FakeHomeAssistantClient : IHomeAssistantClient
{
    private readonly Dictionary<string, HomeAssistantState> _entities = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AreaSnapshot> _areas = [];

    /// <summary>
    /// Loads entity states and area mappings from the snapshot file.
    /// </summary>
    public static FakeHomeAssistantClient FromSnapshotFile(string path)
    {
        var json = File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        var client = new FakeHomeAssistantClient();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Load lights
        if (doc.RootElement.TryGetProperty("lights", out var lightsEl))
        {
            foreach (var el in lightsEl.EnumerateArray())
            {
                var state = DeserializeEntity(el, options);
                if (state is not null)
                    client._entities[state.EntityId] = state;
            }
        }

        // Load media players
        if (doc.RootElement.TryGetProperty("media_players", out var playersEl))
        {
            foreach (var el in playersEl.EnumerateArray())
            {
                var state = DeserializeEntity(el, options);
                if (state is not null)
                    client._entities[state.EntityId] = state;
            }
        }

        // Load areas
        if (doc.RootElement.TryGetProperty("areas", out var areasEl))
        {
            foreach (var el in areasEl.EnumerateArray())
            {
                var area = JsonSerializer.Deserialize<AreaSnapshot>(el.GetRawText(), options);
                if (area is not null)
                    client._areas.Add(area);
            }
        }

        return client;
    }

    public Task<IEnumerable<HomeAssistantState>> GetAllEntityStatesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<HomeAssistantState>>(_entities.Values.ToList());
    }

    public Task<HomeAssistantState?> GetEntityStateAsync(string entityId, CancellationToken cancellationToken = default)
    {
        _entities.TryGetValue(entityId, out var state);
        return Task.FromResult(state);
    }

    public Task<HomeAssistantState> SetEntityStateAsync(
        string entityId,
        string state,
        Dictionary<string, object>? attributes = null,
        CancellationToken cancellationToken = default)
    {
        if (!_entities.TryGetValue(entityId, out var existing))
        {
            existing = new HomeAssistantState
            {
                EntityId = entityId,
                Attributes = attributes ?? new Dictionary<string, object>()
            };
            _entities[entityId] = existing;
        }

        existing.State = state;
        if (attributes is not null)
        {
            foreach (var kvp in attributes)
                existing.Attributes[kvp.Key] = kvp.Value;
        }

        existing.LastChanged = DateTime.UtcNow;
        existing.LastUpdated = DateTime.UtcNow;
        return Task.FromResult(existing);
    }

    public Task<object[]> CallServiceAsync(
        string domain,
        string service,
        string? parameters = null,
        ServiceCallRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        // Simulate state-side effects for common service calls
        var entityId = request?.EntityId;
        if (entityId is not null && _entities.TryGetValue(entityId, out var entity))
        {
            switch ($"{domain}.{service}")
            {
                case "light.turn_on" or "switch.turn_on":
                    entity.State = "on";
                    if (request!.TryGetValue("brightness", out var b))
                        entity.Attributes["brightness"] = b;
                    if (request.TryGetValue("color_name", out var c))
                        entity.Attributes["color_name"] = c;
                    break;

                case "light.turn_off" or "switch.turn_off":
                    entity.State = "off";
                    break;

                case "media_player.media_stop":
                    entity.State = "idle";
                    break;

                case "media_player.media_play":
                    entity.State = "playing";
                    break;

                case "media_player.shuffle_set":
                    entity.Attributes["shuffle"] = true;
                    break;
            }
        }

        return Task.FromResult(Array.Empty<object>());
    }

    public Task<T> CallServiceAsync<T>(
        string domain,
        string service,
        string? parameters = null,
        ServiceCallRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        // Handle music_assistant.get_library by returning canned track data
        if (domain == "music_assistant" && service == "get_library")
        {
            var json = """
            {
                "service_response": {
                    "items": [
                        { "media_type": "track", "uri": "library://track/1", "name": "Fake Track 1" },
                        { "media_type": "track", "uri": "library://track/2", "name": "Fake Track 2" },
                        { "media_type": "track", "uri": "library://track/3", "name": "Fake Track 3" },
                        { "media_type": "track", "uri": "library://track/4", "name": "Fake Track 4" },
                        { "media_type": "track", "uri": "library://track/5", "name": "Fake Track 5" }
                    ]
                }
            }
            """;
            var result = JsonSerializer.Deserialize<T>(json);
            return Task.FromResult(result!);
        }

        // For music_assistant.play_media and other typed calls, return default
        if (typeof(T) == typeof(object[]))
            return Task.FromResult((T)(object)Array.Empty<object>());

        return Task.FromResult(default(T)!);
    }

    public Task<T> RunTemplateAsync<T>(string jinjaTemplate, CancellationToken cancellationToken = default)
    {
        // The LightControlSkill requests area-entity mappings via a Jinja template.
        // Return the snapshot's area data formatted to match AreaEntityMap[].
        // The template uses area_name(id) as the "area" field: {"area":"<area_name>","entities":[...]}
        var areaMappings = _areas.Select(a => new
        {
            area = a.AreaName,
            entities = a.Entities
        });

        var json = JsonSerializer.Serialize(areaMappings);
        var result = JsonSerializer.Deserialize<T>(json);
        return Task.FromResult(result!);
    }

    private static HomeAssistantState? DeserializeEntity(JsonElement el, JsonSerializerOptions options)
    {
        var entityId = el.GetProperty("entity_id").GetString();
        if (entityId is null) return null;

        var state = new HomeAssistantState
        {
            EntityId = entityId,
            State = el.TryGetProperty("state", out var s) ? s.GetString() ?? "unknown" : "unknown"
        };

        if (el.TryGetProperty("attributes", out var attrsEl))
        {
            // Deserialize attributes as Dictionary<string, object> via JsonElement traversal
            foreach (var prop in attrsEl.EnumerateObject())
            {
                state.Attributes[prop.Name] = DeserializeAttributeValue(prop.Value) ?? string.Empty;
            }
        }

        if (el.TryGetProperty("last_changed", out var lc) && DateTime.TryParse(lc.GetString(), out var lcDt))
            state.LastChanged = lcDt;
        if (el.TryGetProperty("last_updated", out var lu) && DateTime.TryParse(lu.GetString(), out var luDt))
            state.LastUpdated = luDt;

        return state;
    }

    private static object? DeserializeAttributeValue(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString()!,
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => JsonSerializer.Serialize(el),
            JsonValueKind.Object => JsonSerializer.Serialize(el),
            _ => el.ToString()
        };
    }

    /// <summary>
    /// Internal model for deserializing the areas array from the snapshot.
    /// </summary>
    private sealed class AreaSnapshot
    {
        [JsonPropertyName("area_id")]
        public string AreaId { get; set; } = string.Empty;
        [JsonPropertyName("area_name")]
        public string AreaName { get; set; } = string.Empty;
        [JsonPropertyName("entities")]
        public List<string> Entities { get; set; } = [];
    }
}
