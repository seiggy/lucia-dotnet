#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

using Microsoft.Extensions.AI;

namespace lucia.Tests.Orchestration;

/// <summary>
/// A delegating <see cref="IChatClient"/> that records all response messages
/// and tool definitions from each model call. Placed <em>inside</em> the
/// <see cref="FunctionInvokingChatClient"/> pipeline (i.e. between function
/// invocation middleware and the raw model client), it captures the raw model
/// responses — including <see cref="FunctionCallContent"/> items — that would
/// otherwise be consumed by function invocation middleware.
/// <para>
/// Pipeline arrangement:
/// <c>Agent → FunctionInvokingChatClient → ChatHistoryCapture → AzureOpenAI</c>
/// </para>
/// </summary>
public sealed class ChatHistoryCapture : DelegatingChatClient
{
    /// <summary>
    /// All response messages accumulated across all model invocation rounds.
    /// Includes assistant messages with <see cref="FunctionCallContent"/> (tool calls)
    /// and final text responses.
    /// </summary>
    public List<ChatMessage> ResponseMessages { get; } = [];

    /// <summary>
    /// Tool definitions (from <see cref="ChatOptions.Tools"/>) captured from
    /// the first model call. These represent the tools available to the agent.
    /// </summary>
    public List<AITool> ToolDefinitions { get; } = [];

    public ChatHistoryCapture(IChatClient innerClient) : base(innerClient) { }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Capture tool definitions from the first call
        if (options?.Tools is { Count: > 0 } tools && ToolDefinitions.Count == 0)
        {
            ToolDefinitions.AddRange(tools);
        }

        var response = await base.GetResponseAsync(messages, options, cancellationToken);

        // Record response messages (assistant tool calls, text responses, etc.)
        ResponseMessages.AddRange(response.Messages);

        return response;
    }

    /// <summary>
    /// Resets captured state for reuse across test iterations.
    /// </summary>
    public void Reset()
    {
        ResponseMessages.Clear();
        ToolDefinitions.Clear();
    }
}
