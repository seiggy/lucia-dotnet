using AgentMemory.Abstractions.Options;
using AgentMemory.Abstractions.Services;
using AgentMemory.AgentFramework;
using AgentMemory.AgentFramework.Tools;
using AgentMemory.Core;
using AgentMemory.Neo4j.Infrastructure;
using FakeItEasy;
using lucia.AgentHost.Memory;
using lucia.Agents.Abstractions;
using lucia.Agents.Services;
using lucia.Data.InMemory;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Services;

public sealed class AgentMemoryContextProviderTests
{
    private const string Alice = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task Invoking_UnknownSpeaker_NeverTouchesAgentMemory()
    {
        var scopes = A.Fake<IServiceScopeFactory>();

        var tools = await RunAgentAsync(CreateProvider(scopes));

        Assert.Empty(tools);
        Assert.Empty(Fake.GetCalls(scopes));
    }

    [Fact]
    public async Task Invoking_SchemaUnavailable_SkipsMemoryWithoutFailingTheTurn()
    {
        var scopes = A.Fake<IServiceScopeFactory>();
        A.CallTo(() => scopes.CreateScope()).Throws(new InvalidOperationException("neo4j down"));
        using var speaker = UserMemoryScope.Begin(Alice);

        var tools = await RunAgentAsync(CreateProvider(scopes));

        Assert.Empty(tools);
    }

    [Fact]
    public async Task Tool_RunsUnderVerifiedSpeakerOwnerScope()
    {
        var facade = A.Fake<IMemoryQueryFacade>();
        var services = new ServiceCollection()
            .AddSingleton(facade)
            .AddScoped<MemoryToolFactory>()
            .AddAgentMemoryCore(_ => { })
            .BuildServiceProvider();
        var owner = services.GetRequiredService<IWritableMemoryOwnerContext>();
        string? ownerDuringCall = null;
        A.CallTo(() => facade.SearchMemoryAsync(A<string>._, A<CancellationToken>._))
            .Invokes(() => ownerDuringCall = owner.UserId)
            .Throws(new InvalidOperationException("stop"));
        var template = new MemoryToolFactory(facade).CreateAIFunctions().Single(tool => tool.Name == "search_memory");
        var tool = new OwnerScopedMemoryTool(template, services.GetRequiredService<IServiceScopeFactory>(), Alice);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await tool.InvokeAsync(new AIFunctionArguments { ["query"] = "tea" }));

        Assert.Equal(Alice, ownerDuringCall);
        Assert.Null(owner.UserId);
    }

    [Fact]
    public async Task RememberFact_WritesUnderSpeakerOwnerScope()
    {
        var facade = A.Fake<IMemoryQueryFacade>();
        var services = new ServiceCollection()
            .AddSingleton(facade)
            .AddAgentMemoryCore(_ => { })
            .BuildServiceProvider();
        var owner = services.GetRequiredService<IWritableMemoryOwnerContext>();
        string? ownerDuringCall = null;
        A.CallTo(() => facade.RememberFactAsync("user", "preferred_name", "Alice", A<CancellationToken>._))
            .Invokes(() => ownerDuringCall = owner.UserId)
            .Returns(MemoryQueryResult.Ok("stored"));

        await CreateProvider(services.GetRequiredService<IServiceScopeFactory>())
            .RememberFactAsync(Alice, "preferred_name", "Alice", CancellationToken.None);

        Assert.Equal(Alice, ownerDuringCall);
        Assert.Null(owner.UserId);
    }

    [Fact]
    public async Task RememberFact_FailedResult_Throws()
    {
        var facade = A.Fake<IMemoryQueryFacade>();
        A.CallTo(() => facade.RememberFactAsync(A<string>._, A<string>._, A<string>._, A<CancellationToken>._))
            .Returns(MemoryQueryResult.Failed("neo4j down"));
        using var services = new ServiceCollection()
            .AddSingleton(facade)
            .AddAgentMemoryCore(_ => { })
            .BuildServiceProvider();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateProvider(services.GetRequiredService<IServiceScopeFactory>())
            .RememberFactAsync(Alice, "preferred_name", "Alice", CancellationToken.None));
    }

    [Fact]
    public async Task Invoked_PersistsOnlyTheSpeakersUtterance_OncePerTurn()
    {
        var inner = A.Fake<Neo4jMemoryContextProvider>();
        A.CallTo(inner).Where(call => call.Method.Name == "InvokingCoreAsync").WithReturnType<ValueTask<AIContext>>()
            .ReturnsLazily(call => ValueTask.FromResult(call.GetArgument<AIContextProvider.InvokingContext>(0)!.AIContext));
        var stored = new List<AIContextProvider.InvokedContext>();
        A.CallTo(inner).Where(call => call.Method.Name == "InvokedCoreAsync").WithReturnType<ValueTask>()
            .Invokes(call => { lock (stored) { stored.Add(call.GetArgument<AIContextProvider.InvokedContext>(0)!); } })
            .Returns(ValueTask.CompletedTask);
        var services = new ServiceCollection()
            .AddSingleton(inner)
            .AddSingleton(A.Fake<ISchemaBootstrapper>())
            .AddSingleton(A.Fake<IMemoryQueryFacade>())
            .AddScoped<MemoryToolFactory>()
            .BuildServiceProvider();
        var provider = CreateProvider(services.GetRequiredService<IServiceScopeFactory>());
        await provider.StartAsync(CancellationToken.None);
        const string Generated = "[Conversation History]\nAssistant: Your name is Bob.\n\n[Current Request]\nI like tea\n\nRouter: turn on the kettle";

        for (var attempt = 0; attempt < 100 && stored.Count == 0; attempt++)
        {
            using var turn = UserMemoryScope.Begin(Alice, "I like tea");
            await Task.WhenAll(RunAgentAsync(provider, Generated), RunAgentAsync(provider, Generated));
            if (stored.Count == 0)
            {
                await Task.Delay(20);
            }
        }

        var persisted = Assert.Single(stored);
        Assert.Equal("I like tea", Assert.Single(persisted.RequestMessages).Text);
        Assert.Equal(ChatRole.User, persisted.RequestMessages.Single().Role);
    }

    [Theory]
    [InlineData(false, typeof(UserContextProvider))]
    [InlineData(true, typeof(AgentMemoryContextProvider))]
    public void AddAgentMemory_FlagSelectsMemoryProvider(bool enabled, Type expected)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AgentMemory:Enabled"] = enabled.ToString(),
            ["AgentMemory:Password"] = "secret"
        });
        builder.Services.AddSingleton<IMemoryStore, InMemoryMemoryStore>();
        builder.Services.AddSingleton<UserContextProvider>();
        builder.Services.AddSingleton<AIContextProvider>(sp => sp.GetRequiredService<UserContextProvider>());

        builder.AddAgentMemory();

        using var services = builder.Services.BuildServiceProvider();
        Assert.IsType(expected, services.GetRequiredService<AIContextProvider>());
        Assert.Equal(enabled, services.GetService<AgentMemoryContextProvider>() is not null);
        if (enabled)
        {
            Assert.Equal(MemoryIsolationMode.StrictMultiTenant,
                services.GetRequiredService<IOptions<MemoryOptions>>().Value.Isolation.Mode);
        }
    }

    private static AgentMemoryContextProvider CreateProvider(IServiceScopeFactory scopes) =>
        new(scopes, Options.Create(new AgentFrameworkOptions()), NullLogger<AgentMemoryContextProvider>.Instance);

    private static async Task<IList<AITool>> RunAgentAsync(AIContextProvider provider, string request = "request")
    {
        using var client = new StubChatClient([
            _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"))
        ]);
        var agent = new ChatClientAgent(client, new ChatClientAgentOptions { AIContextProviders = [provider] });
        await agent.RunAsync(request, await agent.CreateSessionAsync());
        return client.CapturedOptions[0]?.Tools ?? [];
    }
}
