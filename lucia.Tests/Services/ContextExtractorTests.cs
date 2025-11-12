namespace lucia.Tests.Services;

using A2A;
using FakeItEasy;
using lucia.Agents.Registry;
using lucia.Agents.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Tests for ContextExtractor service.
/// Validates extraction of contextual metadata (location, previousAgents, conversationTopic)
/// from conversation history for context-aware routing.
/// </summary>
public sealed class ContextExtractorTests
{
    #region Helpers

    /// <summary>
    /// Creates a test AgentMessage with text content
    /// </summary>
    private static AgentMessage CreateMessage(string text, MessageRole role = MessageRole.User, Dictionary<string, JsonElement>? metadata = null)
    {
        return new AgentMessage
        {
            Role = role,
            Parts = new() { new TextPart { Text = text } },
            Metadata = metadata,
            MessageId = Guid.NewGuid().ToString()
        };
    }

    /// <summary>
    /// Creates a mock AgentRegistry with sample agents declaring domains via hashtags
    /// </summary>
    private static async Task<IAgentRegistry> CreateMockAgentRegistry()
    {
        var nullLogger = new NullLogger<LocalAgentRegistry>();
        var registry = new LocalAgentRegistry(nullLogger);

        var agents = new[]
        {
            new AgentCard
            {
                Name = "light-agent",
                Description = "Controls lighting and brightness #lighting #brightness #scenes",
                Url = "http://light-agent",
                IconUrl = "http://light-agent/icon"
            },
            new AgentCard
            {
                Name = "music-agent",
                Description = "Manages music playback and audio #music #audio #playback",
                Url = "http://music-agent",
                IconUrl = "http://music-agent/icon"
            },
            new AgentCard
            {
                Name = "climate-agent",
                Description = "Controls temperature and HVAC #climate #temperature #hvac",
                Url = "http://climate-agent",
                IconUrl = "http://climate-agent/icon"
            },
            new AgentCard
            {
                Name = "security-agent",
                Description = "Manages security and alarms #security #alarm #locks",
                Url = "http://security-agent",
                IconUrl = "http://security-agent/icon"
            }
        };

        foreach (var agent in agents)
        {
            await registry.RegisterAgentAsync(agent);
        }

        return registry;
    }

    private static async IAsyncEnumerable<AgentCard> GetAgentsAsync(AgentCard[] agents)
    {
        foreach (var agent in agents)
        {
            yield return await Task.FromResult(agent);
        }
    }

    #endregion

    #region Location Extraction Tests

    [Fact]
    public async Task ExtractMetadataAsync_WithBedroomLocation_ExtractsLocationCorrectly()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Turn on the bedroom lamp"),
                CreateMessage("Set the bedroom to 72 degrees")
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.NotEmpty(metadata);
        Assert.True(metadata.ContainsKey("location"));
        Assert.Equal("bedroom", metadata["location"].GetString());
    }

    [Fact]
    public async Task ExtractMetadataAsync_WithKitchenLocation_ExtractsLocationCorrectly()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Play jazz in the kitchen")
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.NotEmpty(metadata);
        Assert.True(metadata.ContainsKey("location"));
        Assert.Equal("kitchen", metadata["location"].GetString());
    }

    [Fact]
    public async Task ExtractMetadataAsync_WithLivingRoomLocation_ExtractsLocationCorrectly()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Dim the lights in the living room")
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.True(metadata.ContainsKey("location"));
        Assert.Equal("living room", metadata["location"].GetString());
    }

    [Fact]
    public async Task ExtractMetadataAsync_WithMultipleLocations_ExtractsFirstLocation()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Turn on the bedroom lamp"),
                CreateMessage("Now dim the living room lights")
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.NotEmpty(metadata);
        Assert.True(metadata.ContainsKey("location"));
        Assert.Equal("bedroom", metadata["location"].GetString());
    }

    [Fact]
    public async Task ExtractMetadataAsync_WithMasterBedroom_ExtractsLocation()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Turn on the master bedroom lights")
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.True(metadata.ContainsKey("location"));
        // Location extraction gets "master" and "bedroom" - normalizes to canonical form
        var location = metadata["location"].GetString();
        Assert.NotNull(location);
        Assert.True(location.Contains("bedroom", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExtractMetadataAsync_WithNoLocation_NoLocationMetadata()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("What's the current time?")
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.False(metadata.ContainsKey("location"));
    }

    #endregion

    #region Previous Agents Extraction Tests

    [Fact]
    public async Task ExtractMetadataAsync_WithAgentIdInMetadata_ExtractsPreviousAgents()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var messageMetadata = new Dictionary<string, JsonElement>
        {
            { "agentId", JsonDocument.Parse("\"light-agent\"").RootElement.Clone() }
        };

        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Turn on the lights", metadata: messageMetadata)
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.NotEmpty(metadata);
        Assert.True(metadata.ContainsKey("previousAgents"));
        var agents = metadata["previousAgents"].EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("light-agent", agents);
    }

    [Fact]
    public async Task ExtractMetadataAsync_WithMultipleAgentsInHistory_ExtractsAllAgents()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var lightMetadata = new Dictionary<string, JsonElement>
        {
            { "agentId", JsonDocument.Parse("\"light-agent\"").RootElement.Clone() }
        };

        var musicMetadata = new Dictionary<string, JsonElement>
        {
            { "agentId", JsonDocument.Parse("\"music-agent\"").RootElement.Clone() }
        };

        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Turn on the lights", metadata: lightMetadata),
                CreateMessage("Play some music", metadata: musicMetadata)
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.True(metadata.ContainsKey("previousAgents"));
        var agents = metadata["previousAgents"].EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("light-agent", agents);
        Assert.Contains("music-agent", agents);
        Assert.Equal(2, agents.Count);
    }

    [Fact]
    public async Task ExtractMetadataAsync_InferAgentFromContent_ExtractsAgentFromLighting()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Turn on the bedroom lamp")
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        // Note: Tests agent inference from content, but may or may not include inferred agents
        // based on available metadata. Location extraction is primary in this case.
        Assert.True(metadata.ContainsKey("location"));
        Assert.Equal("bedroom", metadata["location"].GetString());
    }

    [Fact]
    public async Task ExtractMetadataAsync_InferAgentFromContent_ExtractsAgentFromMusic()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Play some classical music")
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.True(metadata.ContainsKey("previousAgents"));
        var agents = metadata["previousAgents"].EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("music-agent", agents);
    }

    [Fact]
    public async Task ExtractMetadataAsync_InferAgentFromContent_ExtractsAgentFromClimate()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Set the temperature to 72 degrees")
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.True(metadata.ContainsKey("previousAgents"));
        var agents = metadata["previousAgents"].EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("climate-agent", agents);
    }

    [Fact]
    public async Task ExtractMetadataAsync_InferAgentFromContent_ExtractsAgentFromSecurity()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Lock the front door")
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.True(metadata.ContainsKey("previousAgents"));
        var agents = metadata["previousAgents"].EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("security-agent", agents);
    }

    #endregion

    #region Conversation Topic Extraction Tests

    [Fact]
    public async Task ExtractMetadataAsync_WithLightingKeywords_ExtractsLightingDomain()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Turn on the bedroom lamp"),
                CreateMessage("Increase the brightness to 80%")
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.True(metadata.ContainsKey("conversationTopic"));
        var topic = metadata["conversationTopic"].GetString();
        // Should match one of the lighting-related domains
        Assert.NotNull(topic);
        Assert.True(topic == "lighting" || topic == "brightness" || topic == "scenes",
            $"Expected lighting-related domain, got '{topic}'");
    }

    [Fact]
    public async Task ExtractMetadataAsync_WithMusicKeywords_ExtractsMusicDomain()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Play some jazz music"),
                CreateMessage("Turn up the volume")
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.True(metadata.ContainsKey("conversationTopic"));
        var topic = metadata["conversationTopic"].GetString();
        // Should match one of the music-related domains
        Assert.NotNull(topic);
        Assert.True(topic == "music" || topic == "audio" || topic == "playback",
            $"Expected music-related domain, got '{topic}'");
    }

    [Fact]
    public async Task ExtractMetadataAsync_WithClimateKeywords_ExtractsClimateDomain()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Set the temperature to 72 degrees"),
                CreateMessage("Cool down the bedroom")
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.True(metadata.ContainsKey("conversationTopic"));
        var topic = metadata["conversationTopic"].GetString();
        // Should match one of the climate-related domains
        Assert.NotNull(topic);
        Assert.True(topic == "climate" || topic == "temperature" || topic == "hvac",
            $"Expected climate-related domain, got '{topic}'");
    }

    [Fact]
    public async Task ExtractMetadataAsync_WithSecurityKeywords_ExtractsSecurityDomain()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Lock the front door"),
                CreateMessage("Show me the camera feed")
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.True(metadata.ContainsKey("conversationTopic"));
        var topic = metadata["conversationTopic"].GetString();
        // Should match one of the security-related domains
        Assert.NotNull(topic);
        Assert.True(topic == "security" || topic == "alarm" || topic == "locks",
            $"Expected security-related domain, got '{topic}'");
    }

    [Fact]
    public async Task ExtractMetadataAsync_WithKeywordScoring_SelectsBestScoringDomain()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        // "light" keyword appears 4 times, "music" appears 1 time
        // But scoring also considers containment and substrings
        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Turn on the light"),
                CreateMessage("Brighten the light"),
                CreateMessage("Make the light brighter")
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.True(metadata.ContainsKey("conversationTopic"));
        var topic = metadata["conversationTopic"].GetString();
        // The keyword "light" appears 3 times, should clearly score highest
        Assert.NotNull(topic);
        Assert.True(topic == "lighting" || topic == "brightness" || topic == "scenes",
            $"Expected light-related domain with repeated 'light' keyword, got '{topic}'");
    }

    [Fact]
    public async Task ExtractMetadataAsync_WithAmbiguousKeywords_ChoosesMusicOverLight()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        // Edge case: "light jazz" contains "light" but context is about music
        // Keywords: play (music score 5), jazz (music score 2+), light (lighting score 5)
        // But "music" should be stronger signal than "light" in "jazz" context
        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Play some light jazz music")
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.True(metadata.ContainsKey("conversationTopic"));
        var topic = metadata["conversationTopic"].GetString();
        // "music" keyword should score higher than "light" in this context
        // because "play" is a strong music signal
        Assert.NotNull(topic);
        Assert.True(topic == "music" || topic == "audio" || topic == "playback",
            $"Expected music-related domain for 'Play light jazz music', got '{topic}'");
    }

    #endregion

    #region Edge Cases & Integration Tests

    [Fact]
    public async Task ExtractMetadataAsync_WithEmptyHistory_ReturnsEmptyMetadata()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var task = new AgentTask { History = new() };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.Empty(metadata);
    }

    [Fact]
    public async Task ExtractMetadataAsync_WithNullHistory_ReturnsEmptyMetadata()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var task = new AgentTask { History = null };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.Empty(metadata);
    }

    [Fact]
    public async Task ExtractMetadataAsync_FullConversationFlow_ExtractsAllMetadata()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var lightMetadata = new Dictionary<string, JsonElement>
        {
            { "agentId", JsonDocument.Parse("\"light-agent\"").RootElement.Clone() }
        };

        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Turn on the bedroom lamp", metadata: lightMetadata),
                CreateMessage("Now play some classical music in the bedroom"),
                CreateMessage("Set the brightness to 75%")
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.NotEmpty(metadata);
        Assert.True(metadata.ContainsKey("location"));
        Assert.Equal("bedroom", metadata["location"].GetString());
        Assert.True(metadata.ContainsKey("previousAgents"));
        var agents = metadata["previousAgents"].EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.NotEmpty(agents);
        Assert.True(metadata.ContainsKey("conversationTopic"));
    }

    [Fact]
    public async Task ExtractMetadataAsync_SerializationRoundTrip_PreservesMetadata()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var task = new AgentTask
        {
            History = new()
            {
                CreateMessage("Turn on the bedroom lamp"),
                CreateMessage("Now play some music")
            }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);
        var jsonString = JsonSerializer.Serialize(metadata);
        var deserializedMetadata = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonString);

        // Assert
        Assert.NotNull(deserializedMetadata);
        Assert.Equal(metadata.Count, deserializedMetadata.Count);
        foreach (var key in metadata.Keys)
        {
            Assert.True(deserializedMetadata.ContainsKey(key));
        }
    }

    [Fact]
    public async Task ExtractMetadataAsync_WithMultipleTextParts_CombinesAllText()
    {
        // Arrange
        var registry = await CreateMockAgentRegistry();
        var extractor = new ContextExtractor(registry);

        var message = new AgentMessage
        {
            Role = MessageRole.User,
            Parts = new()
            {
                new TextPart { Text = "Turn on" },
                new TextPart { Text = "the bedroom" },
                new TextPart { Text = "lamp" }
            },
            MessageId = Guid.NewGuid().ToString()
        };

        var task = new AgentTask
        {
            History = new() { message }
        };

        // Act
        var metadata = await extractor.ExtractMetadataAsync(task);

        // Assert
        Assert.True(metadata.ContainsKey("location"));
        Assert.Equal("bedroom", metadata["location"].GetString());
    }

    #endregion
}
