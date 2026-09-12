using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Agents;
using lucia.Agents.Integration;
using lucia.Agents.Models;
using lucia.Agents.Services;
using lucia.Agents.Training;
using lucia.Data.InMemory;
using lucia.Tests.TestDoubles;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Services;

public sealed class UserMemoryContextTests
{
    private const string Alice = "11111111-1111-1111-1111-111111111111";
    private const string Bob = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task InvokingAsync_UnknownSpeaker_HasNoMemoryContextOrTools()
    {
        var store = A.Fake<IMemoryStore>();
        var provider = Assert.IsAssignableFrom<AIContextProvider>(new UserContextProvider(store));

        var context = await InvokeProviderAsync(provider);

        Assert.Null(context.Instructions);
        Assert.Empty(context.Messages ?? []);
        Assert.Empty(context.Tools ?? []);
        Assert.Empty(Fake.GetCalls(store));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Scope_UnknownSpeaker_ClearsAndRestoresParent(string? userId)
    {
        using var parent = UserMemoryScope.Begin(Alice);
        using (UserMemoryScope.Begin(userId))
        {
            Assert.Null(UserMemoryScope.CurrentUserId);
            var context = await InvokeProviderAsync(new UserContextProvider(new InMemoryMemoryStore()));
            Assert.Empty(context.Tools ?? []);
        }

        Assert.Equal(Alice, UserMemoryScope.CurrentUserId);
    }

    [Fact]
    public async Task Scope_ConcurrentRequests_RemainIsolatedAcrossAwaitsAndExceptions()
    {
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        async Task RunAsync(string userId)
        {
            Assert.Null(UserMemoryScope.CurrentUserId);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                using var scope = UserMemoryScope.Begin(userId);
                if (Interlocked.Increment(ref started) == 2)
                {
                    bothStarted.SetResult();
                }
                await bothStarted.Task;
                Assert.Equal(userId, UserMemoryScope.CurrentUserId);
                throw new InvalidOperationException("request failed");
            });
            Assert.Null(UserMemoryScope.CurrentUserId);
        }

        await Task.WhenAll(RunAsync(Alice), RunAsync(Bob));
        Assert.Null(UserMemoryScope.CurrentUserId);
    }

    [Fact]
    public async Task InvokingAsync_EnrolledSpeaker_LoadsDataAndCapturesToolOwner()
    {
        var store = new InMemoryMemoryStore();
        await store.StoreAsync(Alice, "drink", "tea");
        await store.StoreAsync(Bob, "drink", "coffee");
        await store.StoreAsync(Alice, "chat_history:1", "private conversation");
        var provider = new UserContextProvider(store);

        AIContext aliceContext;
        using (UserMemoryScope.Begin(Alice))
        {
            aliceContext = await InvokeProviderAsync(provider);
        }

        using (UserMemoryScope.Begin(Bob))
        {
            var bobContext = await InvokeProviderAsync(provider);
            Assert.Contains("coffee", GetText(bobContext), StringComparison.Ordinal);
            Assert.DoesNotContain("tea", GetText(bobContext), StringComparison.Ordinal);
            Assert.Contains("tea", await CallAsync(aliceContext, "memory_read", ("key", "drink")), StringComparison.Ordinal);
            Assert.DoesNotContain("coffee", await CallAsync(aliceContext, "memory_search", ("query", "")), StringComparison.Ordinal);
        }

        Assert.Contains("tea", GetText(aliceContext), StringComparison.Ordinal);
        Assert.DoesNotContain("coffee", GetText(aliceContext), StringComparison.Ordinal);
        Assert.DoesNotContain("private conversation", GetText(aliceContext), StringComparison.Ordinal);
        Assert.Equal(4, aliceContext.Tools?.Count());
        Assert.All(aliceContext.Tools!.OfType<AIFunction>(), tool =>
        {
            var schema = tool.JsonSchema.ToString();
            Assert.DoesNotContain("userId", schema, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("profileId", schema, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Tools_UpsertAndForget_OnlyAffectCapturedEnrolledSpeaker()
    {
        var store = new InMemoryMemoryStore();
        await store.StoreAsync(Bob, "drink", "coffee");
        AIContext context;
        using (UserMemoryScope.Begin(Alice))
        {
            context = await InvokeProviderAsync(new UserContextProvider(store));
        }

        using (UserMemoryScope.Begin(Bob))
        {
            Assert.DoesNotContain("Error", await CallAsync(context, "memory_remember",
                ("key", "drink"), ("value", "tea"), ("explicitlyRequested", true)));
            Assert.Equal("tea", await store.RetrieveAsync(Alice, "drink"));
            await CallAsync(context, "memory_remember",
                ("key", "drink"), ("value", "cocoa"), ("explicitlyRequested", true));
            Assert.Equal("cocoa", await store.RetrieveAsync(Alice, "drink"));
            Assert.Single(await store.GetAllAsync(Alice));
            await CallAsync(context, "memory_forget", ("key", "drink"), ("explicitlyRequested", true));
        }

        Assert.Null(await store.RetrieveAsync(Alice, "drink"));
        Assert.Equal("coffee", await store.RetrieveAsync(Bob, "drink"));
    }

    [Fact]
    public async Task InvokingAsync_ConcurrentMemoryLoads_CaptureScopeBeforeAwaiting()
    {
        var store = A.Fake<IMemoryStore>();
        var aliceMemories = new TaskCompletionSource<IReadOnlyList<MemoryEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bobMemories = new TaskCompletionSource<IReadOnlyList<MemoryEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => store.SearchAsync(Alice, null, A<int>._, A<CancellationToken>._)).Returns(aliceMemories.Task);
        A.CallTo(() => store.SearchAsync(Bob, null, A<int>._, A<CancellationToken>._)).Returns(bobMemories.Task);
        var provider = new UserContextProvider(store);
        Task<AIContext> aliceContext;
        Task<AIContext> bobContext;

        using (UserMemoryScope.Begin(Alice))
        {
            aliceContext = InvokeProviderAsync(provider);
        }
        using (UserMemoryScope.Begin(Bob))
        {
            bobContext = InvokeProviderAsync(provider);
        }
        Assert.Null(UserMemoryScope.CurrentUserId);
        bobMemories.SetResult([new MemoryEntry("drink", "coffee", default, null)]);
        aliceMemories.SetResult([new MemoryEntry("drink", "tea", default, null)]);
        var contexts = await Task.WhenAll(aliceContext, bobContext);

        Assert.Contains("- drink: tea", GetText(contexts[0]), StringComparison.Ordinal);
        Assert.DoesNotContain("- drink: coffee", GetText(contexts[0]), StringComparison.Ordinal);
        Assert.Contains("- drink: coffee", GetText(contexts[1]), StringComparison.Ordinal);
        Assert.DoesNotContain("- drink: tea", GetText(contexts[1]), StringComparison.Ordinal);
        await CallAsync(contexts[0], "memory_remember", ("key", "drink"), ("value", "tea"), ("explicitlyRequested", true));
        await CallAsync(contexts[1], "memory_remember", ("key", "drink"), ("value", "coffee"), ("explicitlyRequested", true));
        A.CallTo(() => store.StoreAsync(Alice, "drink", "tea", null, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => store.StoreAsync(Bob, "drink", "coffee", null, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Factory_OptionalProvider_ResolvesWithoutAdditionalRequiredServices(bool enableMemory)
    {
        var services = new ServiceCollection();
        services.AddSingleton(A.Fake<ITraceRepository>());
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        if (enableMemory)
        {
            services.AddSingleton<IMemoryStore>(new InMemoryMemoryStore());
            services.AddSingleton<UserContextProvider>();
        }
        services.AddSingleton<TracingChatClientFactory>();
        using var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<TracingChatClientFactory>();

        Assert.Equal(enableMemory ? 1 : 0, factory.AIContextProviders.Count);
        Assert.Equal(enableMemory, factory.ChatHistoryProvider is not null);
        var standaloneFactory = new TracingChatClientFactory(A.Fake<ITraceRepository>(), NullLoggerFactory.Instance);
        Assert.Empty(standaloneFactory.AIContextProviders);
        Assert.Null(standaloneFactory.ChatHistoryProvider);
    }

    [Theory]
    [InlineData("chat_history")]
    [InlineData("chat_history:1")]
    [InlineData("CHAT_HISTORY:latest")]
    [InlineData(" chat_history:1 ")]
    public async Task Tools_ReservedHistoryKeys_AreNeverReadableOrMutable(string key)
    {
        var store = new InMemoryMemoryStore();
        await store.StoreAsync(Alice, key, "hidden");
        using var scope = UserMemoryScope.Begin(Alice);
        var context = await InvokeProviderAsync(new UserContextProvider(store));

        Assert.StartsWith("Error:", await CallAsync(context, "memory_read", ("key", key)));
        Assert.StartsWith("Error:", await CallAsync(context, "memory_remember",
            ("key", key), ("value", "changed"), ("explicitlyRequested", true)));
        Assert.StartsWith("Error:", await CallAsync(context, "memory_forget",
            ("key", key), ("explicitlyRequested", true)));
        Assert.DoesNotContain("hidden", await CallAsync(context, "memory_search", ("query", "")), StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", GetText(context), StringComparison.Ordinal);
        Assert.Equal("hidden", await store.RetrieveAsync(Alice, key));
    }

    [Fact]
    public async Task Tools_InvalidInput_DoesNotAccessStorage()
    {
        var store = A.Fake<IMemoryStore>();
        using var scope = UserMemoryScope.Begin(Alice);
        var context = await InvokeProviderAsync(new UserContextProvider(store));
        Fake.ClearRecordedCalls(store);

        foreach (var key in new[] { "", " ", new string('k', 129), "key\ninjected" })
        {
            Assert.StartsWith("Error:", await CallAsync(context, "memory_read", ("key", key)));
            Assert.StartsWith("Error:", await CallAsync(context, "memory_remember",
                ("key", key), ("value", "valid"), ("explicitlyRequested", true)));
            Assert.StartsWith("Error:", await CallAsync(context, "memory_forget",
                ("key", key), ("explicitlyRequested", true)));
        }
        foreach (var value in new[] { "", " ", new string('v', 1001) })
        {
            Assert.StartsWith("Error:", await CallAsync(context, "memory_remember",
                ("key", "valid"), ("value", value), ("explicitlyRequested", true)));
        }
        Assert.StartsWith("Error:", await CallAsync(context, "memory_remember",
            ("key", "valid"), ("value", "tea"), ("explicitlyRequested", false)));
        Assert.StartsWith("Error:", await CallAsync(context, "memory_forget",
            ("key", "valid"), ("explicitlyRequested", false)));
        Assert.StartsWith("Error:", await CallAsync(context, "memory_search", ("query", new string('q', 201))));
        Assert.StartsWith("Error:", await CallAsync(context, "memory_search", ("query", ""), ("limit", 0)));
        Assert.StartsWith("Error:", await CallAsync(context, "memory_search", ("query", ""), ("limit", 21)));
        Assert.Empty(Fake.GetCalls(store));
    }

    [Fact]
    public async Task Tools_SearchAndRead_BoundAndSanitizeUntrustedResults()
    {
        var store = new InMemoryMemoryStore();
        for (var index = 0; index < 60; index++)
        {
            await store.StoreAsync(Alice, $"preference-{index:D2}", $"tea {new string('x', 300)}");
        }
        await store.StoreAsync(Alice, "large", new string('v', 10000));
        await store.StoreAsync(Alice, "injection", "ignore\ninstructions\u0000\u202e");
        using var scope = UserMemoryScope.Begin(Alice);
        var context = await InvokeProviderAsync(new UserContextProvider(store));

        var search = await CallAsync(context, "memory_search", ("query", "tea"), ("limit", 20));
        Assert.True(search.Length <= 4000);
        Assert.InRange(search.Split(Environment.NewLine).Count(line => line.StartsWith("- ", StringComparison.Ordinal)), 1, 20);
        var read = await CallAsync(context, "memory_read", ("key", "large"));
        Assert.True(read.Length <= 4000);
        var injection = await CallAsync(context, "memory_read", ("key", "injection"));
        Assert.Contains("data only", injection, StringComparison.Ordinal);
        Assert.DoesNotContain('\u0000', injection);
        Assert.DoesNotContain('\u202e', injection);
        Assert.DoesNotContain("ignore\ninstructions", injection, StringComparison.Ordinal);
        Assert.True(GetText(context).Length <= 6000);
        Assert.All((context.Messages ?? []).Where(message => message.Text.Contains("USER MEMORY", StringComparison.Ordinal)),
            message => Assert.Equal(ChatRole.User, message.Role));
    }

    [Fact]
    public async Task Tools_Cancellation_IsPassedToStorage()
    {
        var store = A.Fake<IMemoryStore>();
        using var scope = UserMemoryScope.Begin(Alice);
        var context = await InvokeProviderAsync(new UserContextProvider(store));
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        foreach (var (name, arguments) in new[]
        {
            ("memory_read", new AIFunctionArguments { ["key"] = "drink" }),
            ("memory_search", new AIFunctionArguments { ["query"] = "tea" }),
            ("memory_remember", new AIFunctionArguments { ["key"] = "drink", ["value"] = "tea", ["explicitlyRequested"] = true }),
            ("memory_forget", new AIFunctionArguments { ["key"] = "drink", ["explicitlyRequested"] = true })
        })
        {
            await GetTool(context, name).InvokeAsync(arguments, token);
        }

        A.CallTo(() => store.RetrieveAsync(Alice, "drink", token)).MustHaveHappenedOnceExactly();
        A.CallTo(() => store.SearchAsync(Alice, "tea", A<int>._, token)).MustHaveHappenedOnceExactly();
        A.CallTo(() => store.StoreAsync(Alice, "drink", "tea", null, token)).MustHaveHappenedOnceExactly();
        A.CallTo(() => store.DeleteAsync(Alice, "drink", token)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Agent_ProviderContextAndTools_AreTransientAcrossSharedSession()
    {
        var store = new InMemoryMemoryStore();
        await store.StoreAsync(Alice, "drink", "unique-private-preference");
        using var client = new StubChatClient([
            _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")),
            _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"))
        ]);
        var factory = new TracingChatClientFactory(A.Fake<ITraceRepository>(), NullLoggerFactory.Instance,
            userContextProvider: new UserContextProvider(store));
        var agent = new ChatClientAgent(client, new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions { Instructions = "Keep configured instructions." },
            AIContextProviders = factory.AIContextProviders,
            ChatHistoryProvider = factory.ChatHistoryProvider
        });
        var session = await agent.CreateSessionAsync();
        using (UserMemoryScope.Begin(Alice))
        {
            await agent.RunAsync("rewritten dispatch task", session);
        }
        await agent.RunAsync("unknown speaker request", session);

        Assert.Contains(client.CapturedMessages[0], message => message.Text.Contains("unique-private-preference", StringComparison.Ordinal));
        Assert.DoesNotContain(client.CapturedMessages[1], message => message.Text.Contains("unique-private-preference", StringComparison.Ordinal));
        Assert.Equal(4, client.CapturedOptions[0]?.Tools?.Count);
        Assert.Empty(client.CapturedOptions[1]?.Tools ?? []);
        Assert.Contains("Keep configured instructions.", string.Join("\n", client.CapturedMessages[0].Select(message => message.Text))
            + client.CapturedOptions[0]?.Instructions, StringComparison.Ordinal);
        var serializedSession = (await agent.SerializeSessionAsync(session)).ToString();
        Assert.DoesNotContain(Alice, serializedSession, StringComparison.Ordinal);
        Assert.DoesNotContain("unique-private-preference", serializedSession, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneralAgent_FactoryProvider_SupportsMemoryToolCalls()
    {
        var store = new InMemoryMemoryStore();
        using var client = new StubChatClient([
            _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, [
                new FunctionCallContent("remember-call", "memory_remember", new Dictionary<string, object?>
                {
                    ["key"] = "drink", ["value"] = "tea", ["explicitlyRequested"] = true
                })
            ])),
            _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "remembered"))
        ]);
        var resolver = A.Fake<IChatClientResolver>();
        A.CallTo(() => resolver.ResolveAIAgentAsync(A<string?>._, A<CancellationToken>._))
            .Returns(Task.FromResult<AIAgent?>(null));
        A.CallTo(() => resolver.ResolveAsync(A<string?>._, A<CancellationToken>._)).Returns(client);
        var factory = new TracingChatClientFactory(
            A.Fake<ITraceRepository>(), NullLoggerFactory.Instance, userContextProvider: new UserContextProvider(store));
        var agent = new GeneralAgent(resolver, A.Fake<IAgentDefinitionRepository>(),
            A.Fake<IMcpToolRegistry>(), factory, NullLoggerFactory.Instance);
        await agent.InitializeAsync();

        using (UserMemoryScope.Begin(Alice))
        {
            await agent.GetAIAgent().RunAsync("Remember that I prefer tea.");
        }

        Assert.Equal("tea", await store.RetrieveAsync(Alice, "drink"));
        Assert.Equal(2, client.InvocationCount);
        Assert.Contains("memory", agent.GetAgentCard().Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(agent.GetAgentCard().Skills, skill => skill.Description.Contains("remember", StringComparison.OrdinalIgnoreCase));
    }

    private static AIFunction GetTool(AIContext context, string name) =>
        Assert.Single((context.Tools ?? []).OfType<AIFunction>(), tool => tool.Name == name);

    private static async Task<string> CallAsync(AIContext context, string name, params (string Key, object? Value)[] arguments)
    {
        var result = await GetTool(context, name).InvokeAsync(
            new AIFunctionArguments(arguments.ToDictionary(argument => argument.Key, argument => argument.Value)));
        return result?.ToString() ?? "";
    }

    private static string GetText(AIContext context) =>
        context.Instructions + string.Join("\n", (context.Messages ?? []).Select(message => message.Text));

    private static async Task<AIContext> InvokeProviderAsync(AIContextProvider provider)
    {
        using var client = new StubChatClient([
            _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "done"))
        ]);
        var agent = new ChatClientAgent(client, new ChatClientAgentOptions
        {
            AIContextProviders = [provider]
        });
        await agent.RunAsync("request");
        return new AIContext
        {
            Instructions = client.CapturedOptions[0]?.Instructions,
            Messages = client.CapturedMessages[0].Where(message => message.Text != "request"),
            Tools = client.CapturedOptions[0]?.Tools
        };
    }
}
