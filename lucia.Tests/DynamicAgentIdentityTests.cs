using FakeItEasy;
using lucia.Agents;
using lucia.Agents.Abstractions;
using lucia.Agents.Agents;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Integration;
using lucia.Agents.Services;
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
    public async Task LegacyMcpReference_WithPluginPrefix_IsResolvedAsMcp()
    {
        var definition = new AgentDefinition
        {
            Id = "legacy-agent",
            Name = "legacy-agent",
            DisplayName = "Legacy agent",
            Description = "",
            Instructions = "",
            Tools = [new AgentToolReference { ServerId = "plugin:weather", ToolName = "forecast" }],
        };
        var repository = A.Fake<IAgentDefinitionRepository>();
        A.CallTo(() => repository.GetAgentDefinitionAsync(definition.Id, A<CancellationToken>._)).Returns(definition);
        var mcpRegistry = A.Fake<IMcpToolRegistry>();
        var resolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => resolver.ResolveAIAgentAsync(A<string?>._, A<CancellationToken>._))
            .Returns(Task.FromResult<AIAgent?>(A.Fake<AIAgent>()));
        var agent = new DynamicAgent(definition.Id, definition, repository, mcpRegistry, resolver,
            A.Fake<IModelProviderResolver>(), A.Fake<IModelProviderRepository>(),
            new TracingChatClientFactory(A.Fake<ITraceRepository>(), NullLoggerFactory.Instance),
            new AgentsTelemetrySource(), NullLoggerFactory.Instance);

        await agent.InitializeAsync();

        A.CallTo(() => mcpRegistry.ResolveToolsAsync(
            A<IReadOnlyList<AgentToolReference>>.That.Matches(references =>
                references.Count == 1 && references[0].ServerId == "plugin:weather"),
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

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

    [Theory]
    [InlineData("weather")]
    [InlineData("search")]
    public async Task SelectedPluginAndMcpTools_ArePassedToModelUnlessNamesConflict(string pluginToolName)
    {
        var pluginTool = AIFunctionFactory.Create(() => "sunny", pluginToolName);
        var provider = A.Fake<IAgentToolProvider>();
        A.CallTo(() => provider.Name).Returns("Weather");
        A.CallTo(() => provider.InstructionHint).Returns("Use weather for forecasts.");
        A.CallTo(() => provider.GetTools()).Returns([pluginTool, AIFunctionFactory.Create(() => "unused", "unused")]);
        var pluginRegistry = new PluginToolRegistry([provider], NullLogger<PluginToolRegistry>.Instance);
        var mcpReference = new AgentToolReference { ServerId = "mcp-server", ToolName = "search" };
        var mcpTool = AIFunctionFactory.Create(() => "result", "search");
        var mcpRegistry = A.Fake<IMcpToolRegistry>();
        A.CallTo(() => mcpRegistry.ResolveToolsAsync(A<IReadOnlyList<AgentToolReference>>._, A<CancellationToken>._))
            .Returns([mcpTool]);
        var definition = new AgentDefinition
        {
            Id = "weather-agent", Name = "weather-agent", DisplayName = "Weather agent",
            Description = "Forecasts", Instructions = "Answer briefly.",
            Tools = [mcpReference, new AgentToolReference { ServerId = "plugin:Weather", ToolName = pluginToolName, Source = "plugin" }],
        };
        var repository = A.Fake<IAgentDefinitionRepository>();
        A.CallTo(() => repository.GetAgentDefinitionAsync(definition.Id, A<CancellationToken>._)).Returns(definition);
        ChatOptions? observedOptions = null;
        var observedMessages = new List<ChatMessage>();
        var client = A.Fake<IChatClient>();
        A.CallTo(() => client.GetResponseAsync(A<IEnumerable<ChatMessage>>._, A<ChatOptions?>._, A<CancellationToken>._))
            .Invokes(call =>
            {
                observedOptions = call.GetArgument<ChatOptions?>(1);
                observedMessages.AddRange(call.GetArgument<IEnumerable<ChatMessage>>(0)!);
            })
            .Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Done")));
        var resolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => resolver.ResolveAIAgentAsync(null, A<CancellationToken>._)).Returns(Task.FromResult<AIAgent?>(null));
        A.CallTo(() => resolver.ResolveAsync(null, A<CancellationToken>._)).Returns(client);
        var agent = new DynamicAgent(
            definition.Id, definition, repository, mcpRegistry, resolver,
            A.Fake<IModelProviderResolver>(), A.Fake<IModelProviderRepository>(),
            new TracingChatClientFactory(A.Fake<ITraceRepository>(), NullLoggerFactory.Instance),
            new AgentsTelemetrySource(), NullLoggerFactory.Instance, pluginRegistry);

        if (pluginToolName == "search")
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => agent.InitializeAsync());
            Assert.Contains("multiple tools named 'search'", exception.Message);
            return;
        }

        await agent.InitializeAsync();
        await agent.GetAIAgent().RunAsync("What is the forecast?");
        await agent.RebuildAsync();

        Assert.NotNull(observedOptions);
        Assert.Equal(["search", "weather"], observedOptions.Tools!.Select(tool => tool.Name));
        var prompt = observedOptions.Instructions + string.Join("\n", observedMessages.Select(message => message.Text));
        Assert.Contains("Answer briefly.", prompt);
        Assert.Contains("Use weather for forecasts.", prompt);
        A.CallTo(() => mcpRegistry.ResolveToolsAsync(
            A<IReadOnlyList<AgentToolReference>>.That.Matches(references =>
                references.Count == 1 && references[0].ServerId == "mcp-server"),
            A<CancellationToken>._)).MustHaveHappenedTwiceExactly();
        A.CallTo(() => provider.InitializeAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }
}
