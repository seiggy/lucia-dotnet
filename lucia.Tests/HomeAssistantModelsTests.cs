using lucia.HomeAssistant.Models;
using System.Text.Json;

namespace lucia.Tests;

public class HomeAssistantModelsTests
{
    [Fact]
    public void HomeAssistantState_ShouldDeserializeCorrectly()
    {
        // Arrange
        var json = """
        {
            "entity_id": "light.living_room",
            "state": "on",
            "attributes": {
                "brightness": 255,
                "color_mode": "xy"
            },
            "last_changed": "2024-01-01T12:00:00Z",
            "last_updated": "2024-01-01T12:00:00Z",
            "context": {
                "id": "01234567-89ab-cdef-0123-456789abcdef",
                "parent_id": null,
                "user_id": "user123"
            }
        }
        """;

        // Act
        var state = JsonSerializer.Deserialize<HomeAssistantState>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        // Assert
        Assert.NotNull(state);
        Assert.Equal("light.living_room", state.EntityId);
        Assert.Equal("on", state.State);
        Assert.NotNull(state.Attributes);
        Assert.Contains("brightness", state.Attributes.Keys);
        Assert.Equal(255, ((JsonElement)state.Attributes["brightness"]).GetInt32());
        Assert.NotNull(state.Context);
        Assert.Equal("01234567-89ab-cdef-0123-456789abcdef", state.Context.Id);
        Assert.Equal("user123", state.Context.UserId);
    }

    [Fact]
    public void ServiceCallRequest_ShouldSerializeCorrectly()
    {
        // Arrange
        var request = new ServiceCallRequest
        {
            EntityId = "light.living_room",
            ["brightness"] = 128,
            ["color_name"] = "blue"
        };

        // Act
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        // Assert
        Assert.Contains("entity_id", json);
        Assert.Contains("light.living_room", json);
        Assert.Contains("brightness", json);
        Assert.Contains("color_name", json);
        // Should NOT contain service_data wrapper anymore
        Assert.DoesNotContain("service_data", json);
    }

    [Fact]
    public void ServiceCallResponse_ShouldDeserializeCorrectly()
    {
        // Arrange
        var json = """
        {
            "context": {
                "id": "response-context-id",
                "parent_id": "parent-context-id",
                "user_id": null
            }
        }
        """;

        // Act
        var response = JsonSerializer.Deserialize<ServiceCallResponse>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.Context);
        Assert.Equal("response-context-id", response.Context.Id);
        Assert.Equal("parent-context-id", response.Context.ParentId);
        Assert.Null(response.Context.UserId);
    }

    [Fact]
    public void HomeAssistantContext_ShouldHandleNullValues()
    {
        // Arrange
        var json = """
        {
            "id": "context-id",
            "parent_id": null,
            "user_id": null
        }
        """;

        // Act
        var context = JsonSerializer.Deserialize<HomeAssistantContext>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        // Assert
        Assert.NotNull(context);
        Assert.Equal("context-id", context.Id);
        Assert.Null(context.ParentId);
        Assert.Null(context.UserId);
    }
}