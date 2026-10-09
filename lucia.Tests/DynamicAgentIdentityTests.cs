using FakeItEasy;
using lucia.Agents;
using lucia.Agents.Abstractions;
using lucia.Agents.Agents;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Integration;
using lucia.Agents.Training;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests;

/// <summary>
/// The routing identity of a dynamic agent must be its definition Id (kebab-case slug),
/// not its Name or DisplayName. AgentCard.Name, the A2A interface URL, and AIAgent.Id
/// are all compared against router output elsewhere in the pipeline.
/// </summary>
public sealed class DynamicAgentIdentityTests
{
    [Fact]
    public async Task CardAndAIAgent_UseDefinitionIdAsRoutingIdentity_EvenWhenNameDiffers()
    {
        var definition = new AgentDefinition
        {
            Id = "research-agent",
            Name = "Research Agent",
            DisplayName = "Research Agent",
            Description = "Answers research questions",
            Instructions = "Be thorough",
            Tools = [],
        };

        var agent = CreateDynamicAgent(definition);

        await agent.InitializeAsync();

        var card = agent.GetAgentCard();
        Assert.Equal("research-agent", card.Name);
        var skill = Assert.Single(card.Skills);
        Assert.Equal("research-agent", skill.Id);
        Assert.Contains(
            card.SupportedInterfaces,
            i => string.Equals(i.Url, "/a2a/research-agent", StringComparison.Ordinal));

        var aiAgent = agent.GetAIAgent();
        Assert.NotNull(aiAgent);
        Assert.Equal("research-agent", aiAgent.Id);
        Assert.Equal("Research Agent", aiAgent.Name);
    }

    private static DynamicAgent CreateDynamicAgent(AgentDefinition definition)
    {
        var chatClient = A.Fake<IChatClient>();
        var clientResolver = A.Fake<IChatClientResolver>();
        // Loose fakes auto-generate nested fakes for reference return types — force the
        // non-Copilot path (null AIAgent) so BuildAgent is exercised.
        A.CallTo(() => clientResolver.ResolveAIAgentAsync(null, A<CancellationToken>._))
            .Returns(Task.FromResult<AIAgent?>(null));
        A.CallTo(() => clientResolver.ResolveAsync(null, A<CancellationToken>._)).Returns(chatClient);

        var repository = A.Fake<IAgentDefinitionRepository>();
        A.CallTo(() => repository.GetAgentDefinitionAsync(definition.Id, A<CancellationToken>._))
            .Returns(definition);

        return new DynamicAgent(
            definition.Id,
            definition,
            repository,
            A.Fake<IMcpToolRegistry>(),
            clientResolver,
            A.Fake<IModelProviderResolver>(),
            A.Fake<IModelProviderRepository>(),
            new TracingChatClientFactory(A.Fake<ITraceRepository>(), NullLoggerFactory.Instance),
            new AgentsTelemetrySource(),
            NullLoggerFactory.Instance);
    }
}
