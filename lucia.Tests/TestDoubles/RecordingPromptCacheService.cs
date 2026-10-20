using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Orchestration.Models;
using Microsoft.Extensions.AI;

namespace lucia.Tests.TestDoubles;

internal sealed class RecordingPromptCacheService : IPromptCacheService
{
    public List<(string Key, CachedChatResponseData Data)> CachedChatResponses { get; } = [];

    public Task<CachedPromptResponse?> TryGetCachedRoutingDecisionAsync(
        IList<ChatMessage> messages, CancellationToken cancellationToken = default)
        => Task.FromResult<CachedPromptResponse?>(null);

    public Task CacheRoutingDecisionAsync(
        IList<ChatMessage> messages, AgentChoiceResult decision, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<CachedChatResponseData?> TryGetCachedChatResponseAsync(
        string normalizedPrompt, string? semanticQueryText = null, string? expectedInstructionsHash = null, CancellationToken cancellationToken = default)
        => Task.FromResult<CachedChatResponseData?>(null);

    public Task CacheChatResponseAsync(
        string normalizedPrompt, CachedChatResponseData data, CancellationToken cancellationToken = default)
    {
        CachedChatResponses.Add((normalizedPrompt, data));
        return Task.CompletedTask;
    }

    public Task<List<CachedPromptEntry>> GetAllCachedEntriesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new List<CachedPromptEntry>());

    public Task<List<CachedChatResponseData>> GetAllChatCacheEntriesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CachedChatResponses.Select(r => r.Data).ToList());

    public Task<bool> EvictAsync(string cacheKey, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<bool> EvictChatEntryAsync(string cacheKey, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<long> EvictAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0L);

    public Task<long> EvictAllChatEntriesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0L);

    public Task<PromptCacheStats> GetStatsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new PromptCacheStats());

    public Task<PromptCacheStats> GetChatCacheStatsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new PromptCacheStats());
}
