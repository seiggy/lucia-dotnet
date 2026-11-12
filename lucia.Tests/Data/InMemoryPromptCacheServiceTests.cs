using FakeItEasy;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Orchestration;
using lucia.Data.InMemory;
using lucia.Tests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Data;

public sealed class InMemoryPromptCacheServiceTests
{
    [Fact]
    public async Task SemanticLookup_Hits_WhenInstructionsHashMatches()
    {
        var service = CreateService();
        await service.CacheChatResponseAsync("agent-a prompt", CreateCacheData(instructionsHash: "hash-a"));

        var result = await service.TryGetCachedChatResponseAsync(
            "different agent-a prompt text",
            semanticQueryText: "similar user text",
            expectedInstructionsHash: "hash-a");

        Assert.NotNull(result);
    }

    [Fact]
    public async Task SemanticLookup_NeverCrossesAgents()
    {
        var service = CreateService();
        await service.CacheChatResponseAsync("agent-a prompt", CreateCacheData(instructionsHash: "hash-a"));

        var result = await service.TryGetCachedChatResponseAsync(
            "different agent-b prompt text",
            semanticQueryText: "similar user text",
            expectedInstructionsHash: "hash-b");

        Assert.Null(result);
    }

    [Fact]
    public async Task SemanticLookup_NeverServesLegacyEntriesWithoutHash()
    {
        var service = CreateService();
        await service.CacheChatResponseAsync("legacy prompt", CreateCacheData(instructionsHash: null));

        var result = await service.TryGetCachedChatResponseAsync(
            "different prompt text",
            semanticQueryText: "similar user text",
            expectedInstructionsHash: "hash-a");

        Assert.Null(result);
    }

    [Fact]
    public async Task ExactLookup_IgnoresInstructionsScope()
    {
        // The exact-match key already embeds the instructions hash (built by PromptCachingChatClient),
        // so lookup-time scoping must not interfere with exact matches.
        var service = CreateService();
        await service.CacheChatResponseAsync("agent-a prompt", CreateCacheData(instructionsHash: "hash-a"));

        var result = await service.TryGetCachedChatResponseAsync(
            "agent-a prompt",
            semanticQueryText: null,
            expectedInstructionsHash: "hash-b");

        Assert.NotNull(result);
    }

    private static CachedChatResponseData CreateCacheData(string? instructionsHash) => new()
    {
        InstructionsHash = instructionsHash,
        FunctionCalls =
        [
            new CachedFunctionCallData
            {
                CallId = "call-1",
                Name = "get_activity_summary",
                ArgumentsJson = "{}",
            },
        ],
    };

    private static InMemoryPromptCacheService CreateService()
    {
        // Real fixed-vector generator: the M.E.AI single-text GenerateAsync overload is an
        // extension method, which FakeItEasy cannot intercept on a fake.
        var generator = new FixedEmbeddingGenerator(new Embedding<float>(new[] { 1f, 0f, 0f }));

        var resolver = new StubEmbeddingProviderResolver(generator);

        // Constant 1.0 similarity: every candidate scores above any threshold, so the
        // instructions-hash scope filter is the only variable under test.
        var similarity = A.Fake<IEmbeddingSimilarityService>();
        A.CallTo(() => similarity.ComputeSimilarity(A<Embedding<float>?>._, A<Embedding<float>?>._))
            .Returns(1.0);

        var optionsMonitor = A.Fake<IOptionsMonitor<RouterExecutorOptions>>();
        A.CallTo(() => optionsMonitor.CurrentValue).Returns(new RouterExecutorOptions());

        return new InMemoryPromptCacheService(
            resolver,
            similarity,
            optionsMonitor,
            A.Fake<ILogger<InMemoryPromptCacheService>>());
    }
}
