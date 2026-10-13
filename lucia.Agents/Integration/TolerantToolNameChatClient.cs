using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Integration;

/// <summary>
/// IChatClient decorator that tolerates models returning abbreviated tool names.
/// When a function call's name does not exactly match any registered tool but exactly
/// one registered tool ends with "__{requestedName}", the call is rewritten to the full
/// registered name before the response reaches the caller. Exact matches, unknown names
/// and ambiguous suffixes pass through unchanged.
/// </summary>
public sealed class TolerantToolNameChatClient : DelegatingChatClient
{
    private readonly HashSet<string> _knownToolNames;
    private readonly ILogger<TolerantToolNameChatClient> _logger;

    public TolerantToolNameChatClient(
        IChatClient innerClient,
        IEnumerable<string> knownToolNames,
        ILogger<TolerantToolNameChatClient> logger)
        : base(innerClient)
    {
        _knownToolNames = new HashSet<string>(
            knownToolNames.Where(n => !string.IsNullOrWhiteSpace(n)),
            StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        return Normalize(response);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            yield return Normalize(update);
        }
    }

    private ChatResponse Normalize(ChatResponse response)
    {
        if (response.Messages is not { Count: > 0 })
            return response;

        var (messages, changed) = NormalizeMessages(response.Messages);
        if (!changed)
            return response;

        return new ChatResponse(messages)
        {
            ModelId = response.ModelId,
            Usage = response.Usage
        };
    }

    private ChatResponseUpdate Normalize(ChatResponseUpdate update)
    {
        if (update.Contents is not { Count: > 0 } || !update.Contents.OfType<FunctionCallContent>().Any())
            return update;

        var contents = new List<AIContent>(update.Contents.Count);
        foreach (var content in update.Contents)
        {
            if (content is FunctionCallContent fc && TryResolveFullName(fc.Name, out var fullName))
            {
                _logger.LogWarning(
                    "Model returned abbreviated tool name '{BareName}'; rewriting to registered name '{FullName}'",
                    fc.Name, fullName);
                contents.Add(new FunctionCallContent(fc.CallId, fullName, fc.Arguments));
            }
            else
            {
                contents.Add(content);
            }
        }

        return new ChatResponseUpdate(update.Role, contents)
        {
            ModelId = update.ModelId,
            FinishReason = update.FinishReason,
            MessageId = update.MessageId,
            ResponseId = update.ResponseId,
            ConversationId = update.ConversationId,
            CreatedAt = update.CreatedAt
        };
    }

    private (List<ChatMessage> Messages, bool Changed) NormalizeMessages(IEnumerable<ChatMessage> messages)
    {
        var result = new List<ChatMessage>(messages.Count());
        var changed = false;

        foreach (var message in messages)
        {
            if (message.Contents is not { Count: > 0 } || !message.Contents.OfType<FunctionCallContent>().Any())
            {
                result.Add(message);
                continue;
            }

            var contents = new List<AIContent>(message.Contents.Count);
            foreach (var content in message.Contents)
            {
                if (content is FunctionCallContent fc && TryResolveFullName(fc.Name, out var fullName))
                {
                    _logger.LogWarning(
                        "Model returned abbreviated tool name '{BareName}'; rewriting to registered name '{FullName}'",
                        fc.Name, fullName);
                    contents.Add(new FunctionCallContent(fc.CallId, fullName, fc.Arguments));
                    changed = true;
                }
                else
                {
                    contents.Add(content);
                }
            }

            result.Add(new ChatMessage(message.Role, contents) { MessageId = message.MessageId });
        }

        return (result, changed);
    }

    private bool TryResolveFullName(string requestedName, out string fullName)
    {
        fullName = requestedName;

        if (_knownToolNames.Contains(requestedName))
            return false;

        var suffix = "__" + requestedName;
        string? match = null;
        foreach (var name in _knownToolNames)
        {
            if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (match is not null)
                return false;

            match = name;
        }

        if (match is null)
            return false;

        fullName = match;
        return true;
    }
}
