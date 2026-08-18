using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Agents;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Integration;
using lucia.Agents.Services;
using lucia.Agents.Skills;
using lucia.Agents.Training;
using lucia.HomeAssistant.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests;

public sealed class ListsAgentTests
{
    [Fact]
    public async Task RefreshConfigAsync_AfterRepeatedAbsence_AppliesLaterDefinition()
    {
        AgentDefinition? currentDefinition = null;
        var repository = A.Fake<IAgentDefinitionRepository>();
        A.CallTo(() => repository.GetAgentDefinitionAsync("lists-agent", A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult(currentDefinition));

        var appliedConnections = new List<string?>();
        var resolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => resolver.ResolveAIAgentAsync(A<string?>._, A<CancellationToken>._))
            .ReturnsLazily(call =>
            {
                appliedConnections.Add(call.GetArgument<string?>(0));
                return Task.FromResult<AIAgent?>(A.Fake<AIAgent>());
            });

        var agent = new ListsAgent(
            resolver,
            repository,
            new ListSkill(A.Fake<IHomeAssistantClient>(), NullLogger<ListSkill>.Instance),
            new TracingChatClientFactory(A.Fake<ITraceRepository>(), NullLoggerFactory.Instance),
            NullLoggerFactory.Instance);

        await agent.InitializeAsync();
        await agent.RefreshConfigAsync();
        currentDefinition = new AgentDefinition
        {
            Id = "lists-agent",
            Name = "lists-agent",
            DisplayName = "Lists Agent",
            Description = "Lists agent",
            Instructions = "Use lists",
            ModelConnectionName = "model-y",
            UpdatedAt = DateTime.UnixEpoch,
        };
        await agent.RefreshConfigAsync();

        Assert.Equal([null, "model-y"], appliedConnections);
    }
}
