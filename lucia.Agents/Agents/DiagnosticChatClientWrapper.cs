using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Agents;

/// <summary>
/// Temporary diagnostic wrapper that logs tool-related information passing through the chat client pipeline.
/// Wrap around an existing IChatClient to see exactly what the LLM receives and returns.
/// </summary>
internal sealed class DiagnosticChatClientWrapper : DelegatingChatClient
{
    private readonly ILogger _logger;
    private readonly string _agentId;

    public DiagnosticChatClientWrapper(IChatClient innerClient, ILogger logger, string agentId)
        : base(innerClient)
    {
        _logger = logger;
        _agentId = agentId;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Log what we're sending
        var toolCount = options?.Tools?.Count ?? 0;
        var toolMode = options?.ToolMode?.ToString() ?? "null";
        var instructionLen = options?.Instructions?.Length ?? 0;
        _logger.LogDebug("[DiagChat] {AgentId} REQUEST: tools={ToolCount}, toolMode={ToolMode}, instructionLen={InstructionLen}",
            _agentId, toolCount, toolMode, instructionLen);

        if (options?.Tools is { Count: > 0 })
        {
            foreach (var tool in options.Tools)
            {
                _logger.LogDebug("[DiagChat] {AgentId}   Tool: {ToolName} ({ToolType})",
                    _agentId, tool.Name, tool.GetType().Name);
            }
        }

        var response = await base.GetResponseAsync(messages, options, cancellationToken);

        // Log what we got back
        _logger.LogDebug("[DiagChat] {AgentId} RESPONSE: messageCount={Count}, text={Text}",
            _agentId, response.Messages?.Count ?? 0,
            response.Text?[..Math.Min(120, response.Text?.Length ?? 0)]);

        if (response.Messages is { Count: > 0 })
        {
            foreach (var msg in response.Messages)
            {
                var calls = msg.Contents?.OfType<FunctionCallContent>().ToList();
                var results = msg.Contents?.OfType<FunctionResultContent>().ToList();
                var texts = msg.Contents?.OfType<TextContent>().ToList();

                if (calls is { Count: > 0 })
                    _logger.LogDebug("[DiagChat] {AgentId}   FunctionCalls: {Calls}",
                        _agentId, string.Join(", ", calls.Select(c => c.Name)));
                if (results is { Count: > 0 })
                    _logger.LogDebug("[DiagChat] {AgentId}   FunctionResults: {Results}",
                        _agentId, string.Join(", ", results.Select(r => r.CallId)));
                if (texts is { Count: > 0 })
                    _logger.LogDebug("[DiagChat] {AgentId}   TextContent: {Text}",
                        _agentId, string.Join(" ", texts.Select(t => t.Text?[..Math.Min(80, t.Text?.Length ?? 0)])));
            }
        }

        return response;
    }
}
