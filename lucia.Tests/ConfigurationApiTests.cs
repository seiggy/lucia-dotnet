using System.Reflection;
using FakeItEasy;
using lucia.AgentHost.Apis;
using lucia.Agents.Abstractions;
using Microsoft.Extensions.Configuration;

namespace lucia.Tests;

public sealed class ConfigurationApiTests
{
    [Fact]
    public async Task UpdateSectionAsync_EvictsRoutingCache_WhenRouterExecutorSectionChanges()
    {
        var configStore = A.Fake<IConfigStoreWriter>();
        var promptCache = A.Fake<IPromptCacheService>();

        await InvokeUpdateSectionAsync("RouterExecutor", new Dictionary<string, string?> { ["SystemPrompt"] = "new prompt" }, configStore, promptCache);

        // Routing cache keys contain no catalog/prompt fingerprint, so cached decisions
        // must be dropped when router configuration changes.
        A.CallTo(() => promptCache.EvictAllAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task UpdateSectionAsync_DoesNotEvictChatCache_WhenRouterExecutorSectionChanges()
    {
        var configStore = A.Fake<IConfigStoreWriter>();
        var promptCache = A.Fake<IPromptCacheService>();

        await InvokeUpdateSectionAsync("RouterExecutor", new Dictionary<string, string?> { ["SemanticSimilarityThreshold"] = "0.95" }, configStore, promptCache);

        // Threshold changes affect lookups, not stored entries — chat cache stays intact.
        A.CallTo(() => promptCache.EvictAllChatEntriesAsync(A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task UpdateSectionAsync_DoesNotEvict_WhenUnrelatedSectionChanges()
    {
        var configStore = A.Fake<IConfigStoreWriter>();
        var promptCache = A.Fake<IPromptCacheService>();

        await InvokeUpdateSectionAsync("ModelProviders", new Dictionary<string, string?> { ["Enabled"] = "true" }, configStore, promptCache);

        A.CallTo(() => promptCache.EvictAllAsync(A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => promptCache.EvictAllChatEntriesAsync(A<CancellationToken>._)).MustNotHaveHappened();
    }

    private static async Task InvokeUpdateSectionAsync(
        string section,
        Dictionary<string, string?> values,
        IConfigStoreWriter configStore,
        IPromptCacheService promptCache)
    {
        var method = typeof(ConfigurationApi).GetMethod("UpdateSectionAsync", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var task = (Task)method.Invoke(null, [section, values, configStore, A.Fake<IConfigurationRoot>(), promptCache])!;
        await task.ConfigureAwait(false);
    }
}
