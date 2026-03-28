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
    }
}

/// <summary>
/// A single turn in the conversation transcript.
/// </summary>
public sealed class ConversationTurn
{
    /// <summary>
    /// The role: "system", "user", "assistant", or "tool".
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// Text content of the message (may be null for pure tool-call messages).
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Tool calls made in this turn (assistant role only).
    /// </summary>
    public List<ToolCallInfo>? ToolCalls { get; init; }

    /// <summary>
    /// Tool call ID this message responds to (tool role only).
    /// </summary>
    public string? ToolCallId { get; init; }

    /// <summary>
    /// Tool name this result is for (tool role only).
    /// </summary>
    public string? ToolName { get; init; }

    public static ConversationTurn FromChatMessage(ChatMessage msg)
    {
        var toolCalls = msg.Contents
            .OfType<FunctionCallContent>()
            .Select(fc => new ToolCallInfo
            {
                CallId = fc.CallId,
                Name = fc.Name,
                Arguments = fc.Arguments?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value?.ToString())
            })
            .ToList();

        var toolResults = msg.Contents.OfType<FunctionResultContent>().ToList();

        // For tool-result messages, extract the actual result data.
        // FunctionResultContent.Result holds the tool's return value;
        // msg.Text is typically null for these messages.
        string? content = msg.Text;
        string? toolCallId = null;
        string? toolName = null;

        if (toolResults.Count > 0)
        {
            var first = toolResults[0];
            toolCallId = first.CallId;
            content = first.Result?.ToString() ?? first.Exception?.Message ?? content;
        }

        return new ConversationTurn
        {
            Role = msg.Role.Value,
            Content = content,
            ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
            ToolCallId = toolCallId,
            ToolName = toolName
        };
    }
}

/// <summary>
/// A tool/function call made by the assistant.
/// </summary>
public sealed class ToolCallInfo
{
    public string? CallId { get; init; }
    public required string Name { get; init; }
    public Dictionary<string, string?>? Arguments { get; init; }
}
