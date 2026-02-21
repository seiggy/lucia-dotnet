using System.Text.Json;
using lucia.Agents.Training.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Training;

/// <summary>
/// DelegatingChatClient that captures the full IChatClient conversation
/// (system prompts, user messages, tool calls, tool results, assistant responses)
/// and persists each agent invocation as a separate <see cref="ConversationTrace"/>
/// linked by session ID for training data collection.
/// </summary>
public sealed class AgentTracingChatClient : DelegatingChatClient
{
    private readonly string _agentId;
    private readonly ITraceRepository _repository;
    private readonly ILogger<AgentTracingChatClient> _logger;

    /// <summary>
    /// AsyncLocal session ID shared across the orchestrator request.
    /// Set by <see cref="SetSessionId"/> before the workflow executes.
    /// </summary>
    private static readonly AsyncLocal<string?> CurrentSessionId = new();

    public AgentTracingChatClient(
        IChatClient innerClient,
        string agentId,
        ITraceRepository repository,
        ILogger<AgentTracingChatClient> logger)
        : base(innerClient)
    {
        _agentId = agentId;
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Sets the session ID for the current async context so that per-agent
    /// traces can be correlated with the orchestrator trace.
    /// </summary>
    public static void SetSessionId(string sessionId)
    {
        CurrentSessionId.Value = sessionId;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sessionId = CurrentSessionId.Value ?? Guid.NewGuid().ToString("N");
        var requestMessages = messages.ToList();
        var tracedMessages = new List<TracedMessage>();
        var tracedToolCalls = new List<TracedToolCall>();
        var availableTools = new List<string>();

        // Capture request messages (system prompt, user input, prior history)
        foreach (var msg in requestMessages)
        {
            tracedMessages.Add(new TracedMessage
            {
                Role = msg.Role.Value,
                Content = ExtractTextContent(msg)
            });

            // Capture any function call/result content from prior turns
            CaptureToolContentFromMessage(msg, tracedToolCalls);
        }

        // Capture available tools
        if (options?.Tools is { Count: > 0 })
        {
            foreach (var tool in options.Tools)
            {
                if (tool is AIFunction fn)
                    availableTools.Add(fn.Name);
            }
        }

        var startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

        try
        {
            var response = await base.GetResponseAsync(requestMessages, options, cancellationToken);

            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            // Capture response messages (assistant text, tool calls, tool results)
            if (response.Messages is { Count: > 0 })
            {
                foreach (var msg in response.Messages)
                {
                    tracedMessages.Add(new TracedMessage
                    {
                        Role = msg.Role.Value,
                        Content = ExtractTextContent(msg)
                    });

                    CaptureToolContentFromMessage(msg, tracedToolCalls);
                }
            }

            // Only persist on the final response — skip intermediate tool-call
            // round-trips so we get one complete trace per agent invocation.
            var hasPendingToolCalls = response.Messages
                .Any(m => m.Contents?.OfType<FunctionCallContent>().Any() == true);

            if (!hasPendingToolCalls)
            {
                var trace = new ConversationTrace
                {
                    SessionId = sessionId,
                    UserInput = ExtractUserInput(requestMessages),
                    FinalResponse = response.Text,
                    TotalDurationMs = elapsed,
                    Metadata =
                    {
                        ["traceType"] = "agent",
                        ["agentId"] = _agentId,
                        ["toolMode"] = options?.ToolMode?.ToString() ?? "auto",
                        ["availableTools"] = string.Join(", ", availableTools),
                        ["modelId"] = response.ModelId ?? "unknown"
                    },
                    AgentExecutions =
                    [
                        new AgentExecutionRecord
                        {
                            AgentId = _agentId,
                            ModelDeploymentName = response.ModelId,
                            Messages = tracedMessages,
                            ToolCalls = tracedToolCalls,
                            ExecutionDurationMs = elapsed,
                            Success = true,
                            ResponseContent = response.Text
                        }
                    ]
                };

                _ = PersistTraceAsync(trace);
            }

            return response;
        }
        catch (Exception ex)
        {
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            var trace = new ConversationTrace
            {
                SessionId = sessionId,
                UserInput = ExtractUserInput(requestMessages),
                TotalDurationMs = elapsed,
                IsErrored = true,
                ErrorMessage = ex.Message,
                Metadata =
                {
                    ["traceType"] = "agent",
                    ["agentId"] = _agentId,
                    ["availableTools"] = string.Join(", ", availableTools)
                },
                AgentExecutions =
                [
                    new AgentExecutionRecord
                    {
                        AgentId = _agentId,
                        Messages = tracedMessages,
                        ToolCalls = tracedToolCalls,
                        ExecutionDurationMs = elapsed,
                        Success = false,
                        ErrorMessage = ex.Message
                    }
                ]
            };

            _ = PersistTraceAsync(trace);
            throw;
        }
    }

    private async Task PersistTraceAsync(ConversationTrace trace)
    {
        try
        {
            await _repository.InsertTraceAsync(trace);
            _logger.LogDebug("Persisted agent trace for {AgentId} (session {SessionId})", _agentId, trace.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist agent trace for {AgentId}", _agentId);
        }
    }

    private static string ExtractTextContent(ChatMessage message)
    {
        if (message.Contents is { Count: > 0 })
        {
            var textParts = message.Contents.OfType<TextContent>().Select(t => t.Text);
            var text = string.Join(' ', textParts);
            if (!string.IsNullOrEmpty(text))
                return text;
        }

        return message.Text ?? string.Empty;
    }

    private static string ExtractUserInput(IList<ChatMessage> messages)
    {
        var userMessages = messages
            .Where(m => m.Role == ChatRole.User)
            .Select(ExtractTextContent)
            .Where(t => !string.IsNullOrWhiteSpace(t));
        return string.Join("\n", userMessages);
    }

    private static void CaptureToolContentFromMessage(ChatMessage message, List<TracedToolCall> toolCalls)
    {
        if (message.Contents is null) return;

        foreach (var content in message.Contents)
        {
            if (content is FunctionCallContent fc)
            {
                toolCalls.Add(new TracedToolCall
                {
                    ToolName = fc.Name,
                    Arguments = fc.Arguments is not null
                        ? JsonSerializer.Serialize(fc.Arguments)
                        : null
                });
            }
            else if (content is FunctionResultContent fr)
            {
                // Try to attach result to the matching tool call
                var matchingCall = toolCalls.LastOrDefault(tc => tc.ToolName == fr.CallId || tc.Result is null);
                if (matchingCall is not null)
                {
                    matchingCall.Result = fr.Result?.ToString()?[..Math.Min(2000, fr.Result.ToString()?.Length ?? 0)];
                }
                else
                {
                    toolCalls.Add(new TracedToolCall
                    {
                        ToolName = fr.CallId ?? "unknown",
                        Result = fr.Result?.ToString()?[..Math.Min(2000, fr.Result.ToString()?.Length ?? 0)]
                    });
                }
            }
        }
    }
}
