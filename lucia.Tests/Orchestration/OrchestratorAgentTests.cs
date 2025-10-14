using FakeItEasy;
using lucia.Agents.Agents;
using lucia.Agents.Orchestration;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.Logging;
using Xunit;

namespace lucia.Tests.Orchestration;

public class OrchestratorAgentTests : TestBase
{
    private readonly LuciaOrchestrator _mockOrchestrator;
    private readonly IAgentThreadFactory _threadFactory;
    private readonly ILoggerFactory _loggerFactory;

    public OrchestratorAgentTests()
    {
        _mockOrchestrator = A.Fake<LuciaOrchestrator>();
        _threadFactory = new InMemoryThreadFactory();
        _loggerFactory = CreateLoggerFactory();
    }

    [Fact]
    public void Constructor_InitializesAgentCard()
    {
        // Act
        var agent = new OrchestratorAgent(_mockOrchestrator, _threadFactory, _loggerFactory);
        var card = agent.GetAgentCard();

        // Assert
        Assert.NotNull(card);
        Assert.Equal("orchestrator", card.Name);
        Assert.Equal("/a2a/orchestrator", card.Url);
        Assert.Contains("routes requests", card.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("1.0.0", card.Version);
    }

    [Fact]
    public void GetAgentCard_ReturnsProperCapabilities()
    {
        // Arrange
        var agent = new OrchestratorAgent(_mockOrchestrator, _threadFactory, _loggerFactory);

        // Act
        var card = agent.GetAgentCard();

        // Assert
        Assert.NotNull(card.Capabilities);
        Assert.True(card.Capabilities.PushNotifications);
        Assert.True(card.Capabilities.StateTransitionHistory);
        Assert.True(card.Capabilities.Streaming);
    }

    [Fact]
    public void GetAgentCard_ReturnsProperInputOutputModes()
    {
        // Arrange
        var agent = new OrchestratorAgent(_mockOrchestrator, _threadFactory, _loggerFactory);

        // Act
        var card = agent.GetAgentCard();

        // Assert
        Assert.NotNull(card.DefaultInputModes);
        Assert.Single(card.DefaultInputModes);
        Assert.Contains("text", card.DefaultInputModes);
        
        Assert.NotNull(card.DefaultOutputModes);
        Assert.Single(card.DefaultOutputModes);
        Assert.Contains("text", card.DefaultOutputModes);
    }

    [Fact]
    public void GetAgentCard_ReturnsOrchestrationSkill()
    {
        // Arrange
        var agent = new OrchestratorAgent(_mockOrchestrator, _threadFactory, _loggerFactory);

        // Act
        var card = agent.GetAgentCard();

        // Assert
        Assert.NotNull(card.Skills);
        Assert.Single(card.Skills);
        
        var skill = card.Skills[0];
        Assert.Equal("id_orchestrator", skill.Id);
        Assert.Equal("Orchestration", skill.Name);
        Assert.Contains("intelligent routing", skill.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orchestration", skill.Tags);
        Assert.Contains("routing", skill.Tags);
        Assert.Contains("multi-agent", skill.Tags);
    }

    [Fact]
    public void GetAgentCard_IncludesExamples()
    {
        // Arrange
        var agent = new OrchestratorAgent(_mockOrchestrator, _threadFactory, _loggerFactory);

        // Act
        var card = agent.GetAgentCard();

        // Assert
        var skill = card.Skills[0];
        Assert.NotNull(skill.Examples);
        Assert.NotEmpty(skill.Examples);
        Assert.Contains(skill.Examples, e => e.Contains("kitchen", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetAIAgent_ReturnsNonNullAgent()
    {
        // Arrange
        var agent = new OrchestratorAgent(_mockOrchestrator, _threadFactory, _loggerFactory);

        // Act
        var aiAgent = agent.GetAIAgent();

        // Assert
        Assert.NotNull(aiAgent);
    }

    [Fact]
    public async Task InitializeAsync_CompletesSuccessfully()
    {
        // Arrange
        var agent = new OrchestratorAgent(_mockOrchestrator, _threadFactory, _loggerFactory);

        // Act & Assert - should not throw
        await agent.InitializeAsync();
    }

    [Fact]
    public async Task InitializeAsync_WithCancellation_CompletesSuccessfully()
    {
        // Arrange
        var agent = new OrchestratorAgent(_mockOrchestrator, _threadFactory, _loggerFactory);
        using var cts = new CancellationTokenSource();

        // Act & Assert - should not throw
        await agent.InitializeAsync(cts.Token);
    }
}
