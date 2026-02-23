using System.Text.Json;
using lucia.Agents.Orchestration.Models;
using lucia.Tests.TestDoubles;

namespace lucia.Tests.Models;

/// <summary>
/// Tests for OrchestratorAgentResponse model serialization and validation.
/// </summary>
public class AgentResponseTests : TestBase
{
    [Fact]
    public void Serialize_SuccessfulResponse_ProducesCorrectFormat()
    {
        // Arrange
        var response = new AgentResponseBuilder()
            .WithAgentId("light-agent")
            .WithContent("Lights turned on successfully")
            .WithSuccess(true)
            .WithExecutionTime(1250)
            .Build();

        // Act
        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<OrchestratorAgentResponse>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("light-agent", deserialized.AgentId);
        Assert.Equal("Lights turned on successfully", deserialized.Content);
        Assert.True(deserialized.Success);
        Assert.Null(deserialized.ErrorMessage);
        Assert.Equal(1250, deserialized.ExecutionTimeMs);
    }

    [Fact]
    public void Serialize_FailedResponse_IncludesErrorMessage()
    {
        // Arrange
        var response = new AgentResponseBuilder()
            .WithAgentId("music-agent")
            .WithError("Music Assistant service unavailable")
            .WithExecutionTime(5000)
            .Build();

        // Act
        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<OrchestratorAgentResponse>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.False(deserialized.Success);
        Assert.Equal("Music Assistant service unavailable", deserialized.ErrorMessage);
    }

    [Fact]
    public void SuccessResponse_HasNoErrorMessage()
    {
        // Arrange & Act
        var response = new AgentResponseBuilder()
            .WithSuccess(true)
            .Build();

        // Assert
        Assert.True(response.Success);
        Assert.Null(response.ErrorMessage);
    }

    [Fact]
    public void FailureResponse_RequiresErrorMessage()
    {
        // Arrange & Act
        var response = new AgentResponseBuilder()
            .WithError("Test error")
            .Build();

        // Assert
        Assert.False(response.Success);
        Assert.NotNull(response.ErrorMessage);
        Assert.Equal("Test error", response.ErrorMessage);
    }

    [Fact]
    public void ExecutionTime_TracksCorrectly()
    {
        // Arrange & Act
        var fastResponse = new AgentResponseBuilder().WithExecutionTime(100).Build();
        var slowResponse = new AgentResponseBuilder().WithExecutionTime(30000).Build();

        // Assert
        Assert.Equal(100, fastResponse.ExecutionTimeMs);
        Assert.Equal(30000, slowResponse.ExecutionTimeMs);
    }

    [Fact]
    public void JsonPropertyNames_UseCamelCase()
    {
        // Arrange
        var response = new AgentResponseBuilder()
            .WithAgentId("test-agent")
            .WithContent("Test content")
            .WithSuccess(true)
            .WithExecutionTime(100)
            .Build();

        // Act
        var json = JsonSerializer.Serialize(response);

        // Assert
        Assert.Contains("\"agentId\":", json);
        Assert.Contains("\"content\":", json);
        Assert.Contains("\"success\":", json);
        Assert.Contains("\"executionTimeMs\":", json);
        Assert.DoesNotContain("AgentId", json);
        Assert.DoesNotContain("Success", json);
    }

    [Fact]
    public void Deserialize_FromJson_HandlesAllFields()
    {
        // Arrange
        var json = """
        {
            "agentId": "climate-agent",
            "content": "Temperature set to 72°F",
            "success": true,
            "errorMessage": null,
            "executionTimeMs": 2500
        }
        """;

        // Act
        var response = JsonSerializer.Deserialize<OrchestratorAgentResponse>(json);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("climate-agent", response.AgentId);
        Assert.Equal("Temperature set to 72°F", response.Content);
        Assert.True(response.Success);
        Assert.Null(response.ErrorMessage);
        Assert.Equal(2500, response.ExecutionTimeMs);
    }
}
