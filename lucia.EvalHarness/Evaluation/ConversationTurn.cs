using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Evaluation;

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
