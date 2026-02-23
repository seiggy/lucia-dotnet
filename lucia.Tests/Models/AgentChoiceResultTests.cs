using System.Text.Json;
using lucia.Agents.Orchestration.Models;
using lucia.Tests.TestDoubles;

namespace lucia.Tests.Models;

/// <summary>
/// Tests for AgentChoiceResult model serialization and validation.
/// </summary>
public class AgentChoiceResultTests : TestBase
{
    [Fact]
    public void Serialize_ToJson_ProducesCorrectFormat()
    {
        // Arrange
        var result = new AgentChoiceResultBuilder()
            .WithAgentId("light-agent")
            .WithConfidence(0.95)
            .WithReasoning("User request mentions 'lights'")
            .Build();

        // Act
        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<AgentChoiceResult>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("light-agent", deserialized.AgentId);
        Assert.Equal(0.95, deserialized.Confidence);
        Assert.Equal("User request mentions 'lights'", deserialized.Reasoning);
    }

    [Fact]
    public void Deserialize_FromJson_HandlesAllFields()
    {
        // Arrange
        var json = """
        {
            "agentId": "music-agent",
            "confidence": 0.88,
            "reasoning": "Music playback request detected",
            "additionalAgents": ["light-agent"]
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<AgentChoiceResult>(json);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("music-agent", result.AgentId);
        Assert.Equal(0.88, result.Confidence);
        Assert.Equal("Music playback request detected", result.Reasoning);
        Assert.NotNull(result.AdditionalAgents);
        Assert.Single(result.AdditionalAgents);
        Assert.Equal("light-agent", result.AdditionalAgents[0]);
    }

    [Fact]
    public void Confidence_WithinValidRange_Accepts()
    {
        // Arrange & Act
        var minResult = new AgentChoiceResultBuilder().WithConfidence(0.0).Build();
        var maxResult = new AgentChoiceResultBuilder().WithConfidence(1.0).Build();

        // Assert
        Assert.Equal(0.0, minResult.Confidence);
        Assert.Equal(1.0, maxResult.Confidence);
    }

    [Fact]
    public void AdditionalAgents_WhenNull_HandlesCorrectly()
    {
        // Arrange
        var result = new AgentChoiceResultBuilder()
            .WithAgentId("test-agent")
            .WithConfidence(0.9)
            .WithReasoning("Test")
            .Build();

        // Act & Assert
        Assert.Null(result.AdditionalAgents);
    }

    [Fact]
    public void AdditionalAgents_WithMultipleAgents_SerializesCorrectly()
    {
        // Arrange
        var result = new AgentChoiceResultBuilder()
            .WithAdditionalAgents("agent1", "agent2", "agent3")
            .Build();

        // Act
        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<AgentChoiceResult>(json);

        // Assert
        Assert.NotNull(deserialized?.AdditionalAgents);
        Assert.Equal(3, deserialized.AdditionalAgents.Count);
        Assert.Contains("agent1", deserialized.AdditionalAgents);
        Assert.Contains("agent2", deserialized.AdditionalAgents);
        Assert.Contains("agent3", deserialized.AdditionalAgents);
    }

    [Fact]
    public void JsonPropertyNames_UseCamelCase()
    {
        // Arrange
        var result = new AgentChoiceResultBuilder()
            .WithAgentId("test-agent")
            .WithConfidence(0.9)
            .WithReasoning("Test reasoning")
            .Build();

        // Act
        var json = JsonSerializer.Serialize(result);

        // Assert
        Assert.Contains("\"agentId\":", json);
        Assert.Contains("\"confidence\":", json);
        Assert.Contains("\"reasoning\":", json);
        Assert.DoesNotContain("AgentId", json);
        Assert.DoesNotContain("Confidence", json);
    }
}
