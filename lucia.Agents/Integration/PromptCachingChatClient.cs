using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Integration;

/// <summary>
/// IChatClient decorator that checks the prompt cache before forwarding to the inner client.
/// Only the initial planning round (before any tool results) is cached — this ensures tool
/// call decisions are reusable while live device state (from tool execution) is always fresh.
/// Subsequent rounds that contain function results always bypass the cache entirely.
/// </summary>
public sealed class PromptCachingChatClient : DelegatingChatClient
{
    private static readonly ActivitySource ActivitySource = new("Lucia.ChatCache", "1.0.0");

    private readonly IPromptCacheService _cacheService;
    private readonly ILogger<PromptCachingChatClient> _logger;

    // Strips volatile HA context fields so identical intents produce the same cache key
    private static readonly Regex VolatileHaFieldsPattern = new(
        @"""(?:timestamp|day_of_week|id)"":\s*""[^""]*""",
        RegexOptions.Compiled);

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
        using var activity = ActivitySource.StartActivity("ChatCache.GetResponse");
        var messageList = messages as IList<ChatMessage> ?? messages.ToList();

        // Tag with message count and round type so traces show which step in
        // the multi-call agent loop this is (planning vs tool-result summary)
        activity?.SetTag("cache.message_count", messageList.Count);
        activity?.SetTag("cache.has_function_calls",
            messageList.Any(m => m.Contents?.OfType<FunctionCallContent>().Any() == true));

        // Only cache the initial planning round (no tool results in context).
        // Once tool results are present the LLM response depends on live device
        // state (e.g. "light is already on") and must never be served from cache.
        var hasToolResults = ContainsToolResults(messageList);
        activity?.SetTag("cache.has_tool_results", hasToolResults);
        activity?.SetTag("cache.round", hasToolResults ? "tool_summary" : "planning");

        if (!hasToolResults)
        {
            var normalizedKey = NormalizeChatKey(messageList, options);

            try
            {
                var cached = await _cacheService.TryGetCachedChatResponseAsync(normalizedKey, cancellationToken).ConfigureAwait(false);
                if (cached is not null)
                {
                    _logger.LogInformation("Chat cache hit — returning cached LLM decision (key={CacheKey})", cached.CacheKey);
                    activity?.SetTag("cache.result", "hit");
                    activity?.SetTag("cache.key", cached.CacheKey);
                    activity?.SetTag("cache.function_calls", cached.FunctionCalls?.Count ?? 0);
                    return ReconstructResponse(cached);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Chat cache lookup failed, falling through to LLM");
                activity?.SetTag("cache.result", "error");
            }

            // Cache miss — call the inner client
            activity?.SetTag("cache.result", "miss");
            var response = await base.GetResponseAsync(messageList, options, cancellationToken).ConfigureAwait(false);

            // Cache the initial planning decision (tool selection only)
            try
            {
                var data = ExtractCacheData(response);
                if (data is not null)
                {
                    data.NormalizedPrompt = StripVolatileFields(ExtractLastUserText(messageList));
                    await _cacheService.CacheChatResponseAsync(normalizedKey, data, CancellationToken.None).ConfigureAwait(false);
                    activity?.SetTag("cache.stored", true);
                }
                else
                {
                    activity?.SetTag("cache.stored", false);
                    activity?.SetTag("cache.skip_reason", "no_tool_calls");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cache chat response");
            }

            return response;
        }

        // Tool results present — bypass cache entirely, always call the LLM fresh
        activity?.SetTag("cache.result", "bypass");
        activity?.SetTag("cache.bypass_reason", "tool_results_present");
        _logger.LogDebug("Skipping chat cache — conversation contains tool results");
        return await base.GetResponseAsync(messageList, options, cancellationToken).ConfigureAwait(false);
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

        // Include all message content — text, function calls, and function results.
        // message.Text only returns TextContent; FunctionCallContent and
        // FunctionResultContent must be captured explicitly so that different
        // tool results produce different cache keys.
        foreach (var message in messages)
        {
            sb.Append(message.Role.Value);
            sb.Append(':');

            foreach (var item in message.Contents)
            {
                switch (item)
                {
                    case TextContent tc:
                        sb.Append(tc.Text?.ToLowerInvariant().Trim());
                        break;
                    case FunctionCallContent fc:
                        sb.Append("call:");
                        sb.Append(fc.Name);
                        sb.Append('(');
                        if (fc.Arguments is not null)
                        {
                            foreach (var kvp in fc.Arguments.OrderBy(k => k.Key, StringComparer.Ordinal))
                            {
                                sb.Append(kvp.Key);
                                sb.Append('=');
                                sb.Append(kvp.Value?.ToString()?.ToLowerInvariant());
                                sb.Append(',');
                            }
                        }
                        sb.Append(')');
                        break;
                    case FunctionResultContent fr:
                        sb.Append("result:");
                        sb.Append(fr.Result?.ToString()?.ToLowerInvariant());
                        break;
                }
            }

            sb.Append('\n');
        }

        // Collapse whitespace
        var raw = sb.ToString();

        // Strip volatile HA context fields so identical intents hash the same
        raw = VolatileHaFieldsPattern.Replace(raw, string.Empty);

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

        List<CachedFunctionCallData>? functionCalls = null;

        foreach (var message in response.Messages)
        {
            if (message.Role != ChatRole.Assistant)
                continue;

            if (message.Contents is { Count: > 0 })
            {
                foreach (var content in message.Contents)
                {
                    if (content is FunctionCallContent fc)
                    {
                        functionCalls ??= [];
                        functionCalls.Add(new CachedFunctionCallData
                        {
                            CallId = fc.CallId ?? string.Empty,
                            Name = fc.Name,
                            ArgumentsJson = fc.Arguments is not null
                                ? JsonSerializer.Serialize(fc.Arguments)
                                : null
                        });
                    }
                }
            }
        }

        // Only cache tool-call decisions — never plain text responses.
        // Text responses reflect live device state and must not be served stale.
        if (functionCalls is null)
            return null;

        return new CachedChatResponseData
        {
            FunctionCalls = functionCalls,
            ModelId = response.ModelId
        };
    }

    private static string ComputeSha256(string input)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hashBytes);
    }

    /// <summary>
    /// Returns true when the conversation contains tool/function results,
    /// meaning the LLM response depends on dynamic runtime state and must
    /// not be cached. Only the initial planning call (no tool results yet)
    /// is safe to cache.
    /// </summary>
    private static bool ContainsToolResults(IList<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.Tool)
                return true;

            if (message.Contents is { Count: > 0 })
            {
                foreach (var content in message.Contents)
                {
                    if (content is FunctionResultContent)
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts the last user message text for a human-readable display prompt.
    /// </summary>
    private static string ExtractLastUserText(IList<ChatMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == ChatRole.User && !string.IsNullOrWhiteSpace(messages[i].Text))
                return messages[i].Text.Trim();
        }

        return "(no user message)";
    }

    /// <summary>
    /// Strips volatile HA context fields (timestamp, day_of_week) from text
    /// so the stored display prompt doesn't include per-request noise.
    /// </summary>
    internal static string StripVolatileFields(string text)
    {
        return VolatileHaFieldsPattern.Replace(text, string.Empty);
    }
}
