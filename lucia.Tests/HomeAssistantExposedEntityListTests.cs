using System.Text.Json;
using lucia.HomeAssistant.Configuration;
using lucia.HomeAssistant.Models;

namespace lucia.Tests;

/// <summary>
/// Guards the wire format of <c>homeassistant/expose_entity/list</c>. Home Assistant wraps
/// the entity map in an <c>"exposed_entities"</c> envelope key; binding the result directly
/// to a dictionary (the previous behavior) yields a single <c>"exposed_entities"</c> entry
/// with all-null flags, which filtered every entity out of the location cache (issue #284).
/// </summary>
public class HomeAssistantExposedEntityListTests
{
    [Fact]
    public void ExposedEntityListResponse_DeserializesHaEnvelope()
    {
        var json = """
        {
            "exposed_entities": {
                "light.kitchen": { "conversation": true },
                "media_player.tv": { "cloud.alexa": true, "conversation": true }
            }
        }
        """;

        var response = JsonSerializer.Deserialize<ExposedEntityListResponse>(json, HomeAssistantJsonOptions.Default);

        Assert.NotNull(response);
        Assert.Equal(2, response.ExposedEntities.Count);
        Assert.False(response.ExposedEntities.ContainsKey("exposed_entities"));
        Assert.True(response.ExposedEntities["light.kitchen"].Conversation);
        Assert.Null(response.ExposedEntities["light.kitchen"].CloudAlexa);
        Assert.True(response.ExposedEntities["media_player.tv"].CloudAlexa);
        Assert.True(response.ExposedEntities["media_player.tv"].IsExposedToAny);
    }

    [Fact]
    public void ExposedEntityListResponse_EmptyEnvelope_YieldsEmptyDictionary()
    {
        var response = JsonSerializer.Deserialize<ExposedEntityListResponse>(
            "{\"exposed_entities\": {}}", HomeAssistantJsonOptions.Default);

        Assert.NotNull(response);
        Assert.Empty(response.ExposedEntities);
    }

    [Fact]
    public void ExposedEntityAssistants_MissingFlags_AreNotExposed()
    {
        var json = """
        {
            "exposed_entities": {
                "sensor.temperature": {}
            }
        }
        """;

        var response = JsonSerializer.Deserialize<ExposedEntityListResponse>(json, HomeAssistantJsonOptions.Default);

        Assert.NotNull(response);
        Assert.Single(response.ExposedEntities);
        Assert.False(response.ExposedEntities["sensor.temperature"].IsExposedToAny);
    }
}
