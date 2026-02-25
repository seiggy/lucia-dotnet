using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using lucia.Agents.Models;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Services;

/// <summary>
/// IChatClient decorator that checks the prompt cache before forwarding to the inner client.
/// Caches LLM responses (text and tool calls) so that repeated identical prompts skip the
/// LLM call. Tool calls are replayed through the function-invoking layer, so tools still
/// execute fresh every time — only the expensive LLM planning call is cached.
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
        var messageList = messages as IList<ChatMessage> ?? messages.ToList();
        var normalizedKey = NormalizeChatKey(messageList, options);

        // Check cache
        try
        {
            var cached = await _cacheService.TryGetCachedChatResponseAsync(normalizedKey, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                _logger.LogInformation("Chat cache hit — returning cached response (key={CacheKey})", cached.CacheKey);
                return ReconstructResponse(cached);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chat cache lookup failed, falling through to LLM");
        }

        // Cache miss — call the inner client
        var response = await base.GetResponseAsync(messageList, options, cancellationToken).ConfigureAwait(false);

        // Cache the response
        try
        {
            var data = ExtractCacheData(response);
            if (data is not null)
            {
                await _cacheService.CacheChatResponseAsync(normalizedKey, data, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache chat response");
        }

        return response;
    }

    /// <summary>
    /// Builds a normalized cache key from the chat messages and options.
    /// Includes system instructions (from options) to differentiate between agents,
    /// and all message content to capture the full conversation context.
    /// </summary>
    internal static string NormalizeChatKey(IList<ChatMessage> messages, ChatOptions? options)
    {
        var sb = new StringBuilder();

        // Include system instructions from options (differentiates agents)
        if (!string.IsNullOrWhiteSpace(options?.Instructions))
        {
            sb.Append("instructions:");
            sb.Append(ComputeSha256(options.Instructions));
            sb.Append('\n');
        }

        // Include all message content
        foreach (var message in messages)
        {
            sb.Append(message.Role.Value);
            sb.Append(':');

            var text = message.Text ?? string.Empty;
            sb.Append(text.ToLowerInvariant().Trim());
            sb.Append('\n');
        }

        // Collapse whitespace
        var raw = sb.ToString();
        sb.Clear();
        sb.EnsureCapacity(raw.Length);
        var previousWasSpace = false;
        foreach (var c in raw)
        {
            if (c != '\n' && char.IsWhiteSpace(c))
            {
                if (!previousWasSpace)
                    sb.Append(' ');
                previousWasSpace = true;
            }
            else
            {
                sb.Append(c);
                previousWasSpace = false;
            }
        }

        return sb.ToString();
    }

    private static ChatResponse ReconstructResponse(CachedChatResponseData cached)
    {
        var contents = new List<AIContent>();

        if (!string.IsNullOrEmpty(cached.ResponseText))
        {
            contents.Add(new TextContent(cached.ResponseText));
        }

        if (cached.FunctionCalls is { Count: > 0 })
        {
            foreach (var fc in cached.FunctionCalls)
            {
                IDictionary<string, object?>? arguments = null;
                if (!string.IsNullOrEmpty(fc.ArgumentsJson))
                {
                    arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(fc.ArgumentsJson);
                }

                contents.Add(new FunctionCallContent(fc.CallId, fc.Name, arguments));
            }
        }

        var assistantMessage = new ChatMessage(ChatRole.Assistant, contents);
        return new ChatResponse([assistantMessage])
        {
            ModelId = cached.ModelId
        };
    }

    private static CachedChatResponseData? ExtractCacheData(ChatResponse response)
    {
        if (response.Messages is not { Count: > 0 })
            return null;

        string? responseText = null;
        List<CachedFunctionCallData>? functionCalls = null;

        foreach (var message in response.Messages)
        {
            if (message.Role != ChatRole.Assistant)
                continue;

            if (message.Contents is { Count: > 0 })
            {
                foreach (var content in message.Contents)
                {
                    switch (content)
                    {
                        case TextContent text when !string.IsNullOrWhiteSpace(text.Text):
                            responseText = text.Text;
                            break;

                        case FunctionCallContent fc:
                            functionCalls ??= [];
                            functionCalls.Add(new CachedFunctionCallData
                            {
                                CallId = fc.CallId ?? string.Empty,
                                Name = fc.Name,
                                ArgumentsJson = fc.Arguments is not null
                                    ? JsonSerializer.Serialize(fc.Arguments)
                                    : null
                            });
                            break;
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(message.Text))
            {
                responseText = message.Text;
            }
        }

        // Only cache if there's meaningful content
        if (responseText is null && functionCalls is null)
            return null;

        return new CachedChatResponseData
        {
            ResponseText = responseText,
            FunctionCalls = functionCalls,
            ModelId = response.ModelId
        };
    }

    private static string ComputeSha256(string input)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hashBytes);
    }
}
