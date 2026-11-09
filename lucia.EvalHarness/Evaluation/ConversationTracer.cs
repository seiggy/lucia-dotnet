using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Evaluation;

/// <summary>
/// A delegating <see cref="IChatClient"/> that records the complete conversation
/// history in order: system prompt, user messages, assistant responses, tool calls,
/// and tool results. Placed in the chat client pipeline, it captures every round
/// of model interaction for trace export.
/// </summary>
public sealed class ConversationTracer : DelegatingChatClient
{
    /// <summary>
    /// The complete ordered conversation transcript across all LLM rounds.
    /// </summary>
    public List<ConversationTurn> Turns { get; } = [];

    /// <summary>Tools offered to the model in this scenario, or null if no inventory was captured.</summary>
    public IReadOnlyCollection<string>? AvailableToolNames => _availableToolNames;

    private HashSet<string>? _availableToolNames;

    /// <summary>
    /// Tracks messages already recorded so we don't duplicate them when
    /// FunctionInvokingChatClient replays the conversation on subsequent rounds.
    /// </summary>
    private readonly HashSet<ChatMessage> _recorded = new(ReferenceEqualityComparer.Instance);

    public ConversationTracer(IChatClient innerClient) : base(innerClient) { }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (options?.Tools is { } tools)
        {
            _availableToolNames ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tool in tools)
            {
                _availableToolNames.Add(tool.Name);
            }
        }

        // Record input messages we haven't seen before.
        // On round N, FunctionInvokingChatClient replays system + user + all
        // prior assistant/tool messages — skip ones we already captured.
        foreach (var msg in messages)
        {
            if (_recorded.Add(msg))
            {
                Turns.Add(ConversationTurn.FromChatMessage(msg));
            }
        }

        var response = await base.GetResponseAsync(messages, options, cancellationToken);

        // Record the new assistant response(s) from this round
        foreach (var msg in response.Messages)
        {
            if (_recorded.Add(msg))
            {
                Turns.Add(ConversationTurn.FromChatMessage(msg));
            }
        }

        return response;
    }

    /// <summary>
    /// Resets the conversation transcript for reuse across test cases.
    /// </summary>
    public void Reset()
    {
        Turns.Clear();
        _recorded.Clear();
        _availableToolNames = null;
    }
}
