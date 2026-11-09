using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Integration;

/// <summary>
/// IChatClient decorator that tolerates models returning abbreviated tool names.
/// When a function call's name does not exactly match any registered tool but exactly
/// one registered tool ends with "__{requestedName}", the call is rewritten to the full
/// registered name before the response reaches the caller. Exact matches, unknown names
/// and ambiguous suffixes pass through unchanged. Rewrites replace the affected
/// FunctionCallContent entries in place on the original response objects, so every other
/// property (finish reason, ids, usage, additional properties, ...) is preserved without
/// having to be copied explicitly.
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

        if (response.Messages is not null)
        {
            foreach (var message in response.Messages)
            {
                RewriteContents(message.Contents);
            }
        }

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            if (update.Contents is not null)
            {
                RewriteContents(update.Contents);
            }

            yield return update;
        }
    }

    private void RewriteContents(IList<AIContent> contents)
    {
        for (var i = 0; i < contents.Count; i++)
        {
            if (contents[i] is not FunctionCallContent call)
                continue;

            if (!TryResolveFullName(call.Name, out var fullName))
                continue;

            _logger.LogWarning(
                "Model returned abbreviated tool name '{BareName}'; rewriting to registered name '{FullName}'",
                call.Name, fullName);
            contents[i] = WithName(call, fullName);
        }
    }

    private static FunctionCallContent WithName(FunctionCallContent call, string newName)
    {
        var rewritten = new FunctionCallContent(call.CallId, newName, call.Arguments)
        {
            Exception = call.Exception,
            InformationalOnly = call.InformationalOnly,
            RawRepresentation = call.RawRepresentation
        };

        if (call.Annotations is { Count: > 0 })
        {
            rewritten.Annotations = [..call.Annotations];
        }

        if (call.AdditionalProperties is { Count: > 0 })
        {
            var properties = new AdditionalPropertiesDictionary();
            foreach (var property in call.AdditionalProperties)
            {
                properties[property.Key] = property.Value;
            }

            rewritten.AdditionalProperties = properties;
        }

        return rewritten;
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
