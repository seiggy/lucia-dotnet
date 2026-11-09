using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.Family;
using lucia.Data.Sqlite;
using lucia.Tests.Data.Providers;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace lucia.Tests.Family;

public sealed class MemberModelChatClientTests : IDisposable
{
    private readonly SqliteStoreEnvironment _environment;
    private readonly SqliteLlmUsageStore _usage;
    private readonly ActionBroker _broker;

    public MemberModelChatClientTests()
    {
        _environment = SqliteStoreEnvironment.CreateAsync().GetAwaiter().GetResult();
        _usage = new SqliteLlmUsageStore(_environment.Config);
        _broker = new ActionBroker(A.Fake<IActionRecordStore>(), A.Fake<IFamilyMemberStore>(), TimeProvider.System, NullLogger<ActionBroker>.Instance);
    }

    public void Dispose() => _environment.Dispose();

    private static ModelProvider Provider(string id, decimal? inPrice = null, decimal? outPrice = null) => new()
    {
        Id = id,
        Name = id,
        ModelName = $"{id}-model",
        InputCostPerMillionTokensUsd = inPrice,
        OutputCostPerMillionTokensUsd = outPrice,
    };

    private static StubChatClient Responding(string text, long input = 1000, long output = 500) => new([
        _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output },
        },
    ]);

    private MemberModelChatClient Create(IChatClient inner, ModelProvider provider, Func<string, IChatClient?>? resolve = null) =>
        new(inner, provider, (id, _) => Task.FromResult(resolve?.Invoke(id)), _usage, TimeProvider.System, NullLogger<MemberModelChatClient>.Instance);

    private static FamilyMember Member(MemberLlmControls? controls = null) => new()
    {
        Id = "kid",
        DisplayName = "Sam",
        Role = FamilyRole.Child,
        LlmControls = controls ?? new MemberLlmControls(),
    };

    [Fact]
    public async Task WithoutAMember_PassesThroughAndRecordsNothing()
    {
        using var inner = Responding("hi");
        var client = Create(inner, Provider("cloud", 2m, 8m));

        var response = await client.GetResponseAsync("hello");

        Assert.Equal("hi", response.Text);
        Assert.Empty(await _usage.ListRecentAsync("kid", 10));
    }

    [Fact]
    public async Task WithAMember_RecordsTokensAndCost()
    {
        using var inner = Responding("hi", input: 1000, output: 500);
        var client = Create(inner, Provider("cloud", 2m, 8m));

        using (MemberActionScope.Begin(Member(), _broker))
        {
            await client.GetResponseAsync("hello");
        }

        var record = Assert.Single(await _usage.ListRecentAsync("kid", 10));
        Assert.Equal("cloud", record.ProviderId);
        Assert.Equal("cloud-model", record.Model);
        Assert.Equal(1000, record.InputTokens);
        Assert.Equal(500, record.OutputTokens);
        Assert.Equal(0.006m, record.CostUsd);
    }

    [Fact]
    public async Task PreferredProvider_HandlesTheCallInsteadOfTheAgentsModel()
    {
        using var cloud = Responding("from cloud");
        using var local = Responding("from local");
        var localClient = Create(local, Provider("local"));
        var client = Create(cloud, Provider("cloud", 2m, 8m), id => id == "local" ? localClient : null);

        ChatResponse response;
        using (MemberActionScope.Begin(Member(new MemberLlmControls { PreferredProviderId = "local" }), _broker))
        {
            response = await client.GetResponseAsync("hello");
        }

        Assert.Equal("from local", response.Text);
        Assert.Equal(0, cloud.InvocationCount);
        var record = Assert.Single(await _usage.ListRecentAsync("kid", 10));
        Assert.Equal("local", record.ProviderId);
        Assert.Equal(0m, record.CostUsd);
    }

    [Fact]
    public async Task DisallowedProviderWithoutAnAlternative_FailsInsteadOfFallingBack()
    {
        using var cloud = Responding("from cloud");
        var client = Create(cloud, Provider("cloud"));

        using (MemberActionScope.Begin(Member(new MemberLlmControls { AllowedProviderIds = ["local"] }), _broker))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync("hello"));
        }

        Assert.Equal(0, cloud.InvocationCount);
    }

    [Fact]
    public async Task Streaming_RecordsUsageFromUsageContent()
    {
        var inner = A.Fake<IChatClient>();
        A.CallTo(() => inner.GetStreamingResponseAsync(A<IEnumerable<ChatMessage>>._, A<ChatOptions?>._, A<CancellationToken>._))
            .Returns(Updates());
        var client = Create(inner, Provider("cloud", 2m, 8m));

        using (MemberActionScope.Begin(Member(), _broker))
        {
            await foreach (var _ in client.GetStreamingResponseAsync("hello"))
            {
            }
        }

        var record = Assert.Single(await _usage.ListRecentAsync("kid", 10));
        Assert.Equal(1000, record.InputTokens);
        Assert.Equal(0.006m, record.CostUsd);

        static async IAsyncEnumerable<ChatResponseUpdate> Updates()
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "hi");
            await Task.Yield();
            yield return new ChatResponseUpdate
            {
                Contents = [new UsageContent(new UsageDetails { InputTokenCount = 1000, OutputTokenCount = 500 })],
            };
        }
    }
}
