using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Services;

/// <summary>
/// IChatClient decorator that checks the prompt cache before forwarding to the inner client.
/// Caches routing decisions (not final responses) so agents still execute tools fresh.
/// NOTE: This wrapper is designed for the router LLM call only.
/// </summary>
public sealed class PromptCachingChatClient : DelegatingChatClient
{
    private readonly IPromptCacheService _cacheService;
    private readonly ILogger<PromptCachingChatClient> _logger;

    public PromptCachingChatClient(
        IChatClient innerClient,
        IPromptCacheService cacheService,
        ILogger<PromptCachingChatClient> logger)
        : base(innerClient)
    {
        _cacheService = cacheService;
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // This wrapper is kept for compatibility but routing cache is now
        // handled directly inside RouterExecutor. Just forward to inner client.
        return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
    }
}
