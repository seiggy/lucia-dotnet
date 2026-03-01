using System.Text.Json;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
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
    private readonly LiveActivityChannel? _liveChannel;

    /// <summary>
    /// AsyncLocal session ID shared across the orchestrator request.
    /// Set by <see cref="SetSessionId"/> before the workflow executes.
    /// </summary>
    private static readonly AsyncLocal<string?> CurrentSessionId = new();

    public AgentTracingChatClient(
        IChatClient innerClient,
        string agentId,
        ITraceRepository repository,
        ILogger<AgentTracingChatClient> logger,
        LiveActivityChannel? liveChannel = null)
        : base(innerClient)
    {
        _agentId = agentId;
        _repository = repository;
        _logger = logger;
        _liveChannel = liveChannel;
    }

    /// <summary>
    /// Sets the session ID for the current async context so that per-agent
    /// traces can be correlated with the orchestrator trace.
    /// </summary>
    public static void SetSessionId(string sessionId)
    {
        CurrentSessionId.Value = sessionId;
    }

    // Accumulators that survive across multiple GetResponseAsync rounds
    // within a single agent invocation (planning → tool execution → summary)
    private readonly List<TracedMessage> _accumulatedMessages = [];
    private readonly List<TracedToolCall> _accumulatedToolCalls = [];
    private List<string>? _availableToolsSnapshot;
    private double _totalElapsedMs;
    private int _roundCount;

    private void ResetAccumulators()
    {
        _accumulatedMessages.Clear();
        _accumulatedToolCalls.Clear();
        _availableToolsSnapshot = null;
        _totalElapsedMs = 0;
        _roundCount = 0;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sessionId = CurrentSessionId.Value ?? Guid.NewGuid().ToString("N");
        var requestMessages = messages.ToList();
        var availableTools = new List<string>();

        // Capture available tools (only on first call — they don't change between rounds)
        if (options?.Tools is { Count: > 0 } && _accumulatedToolCalls.Count == 0)
        {
            foreach (var tool in options.Tools)
            {
                if (tool is AIFunction fn)
                    availableTools.Add(fn.Name);
            }
            _availableToolsSnapshot = availableTools;
        }

        // Capture request messages for this round
        foreach (var msg in requestMessages)
        {
            _accumulatedMessages.Add(new TracedMessage
            {
                Role = msg.Role.Value,
                Content = ExtractTextContent(msg)
            });

            // Capture any function call/result content from prior turns
            CaptureToolContentFromMessage(msg, _accumulatedToolCalls);
        }

        var startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

        try
        {
            var response = await base.GetResponseAsync(requestMessages, options, cancellationToken).ConfigureAwait(false);

            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            _totalElapsedMs += elapsed;

            // Capture response messages (assistant text, tool calls, tool results)
            if (response.Messages is { Count: > 0 })
            {
                foreach (var msg in response.Messages)
                {
                    _accumulatedMessages.Add(new TracedMessage
                    {
                        Role = msg.Role.Value,
                        Content = ExtractTextContent(msg)
                    });

                    CaptureToolContentFromMessage(msg, _accumulatedToolCalls);
                    EmitLiveToolEvents(msg);
                }
            }

            _roundCount++;

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
                    TotalDurationMs = _totalElapsedMs,
                    Metadata =
                    {
                        ["traceType"] = "agent",
                        ["agentId"] = _agentId,
                        ["toolMode"] = options?.ToolMode?.ToString() ?? "auto",
                        ["availableTools"] = string.Join(", ", _availableToolsSnapshot ?? availableTools),
                        ["modelId"] = response.ModelId ?? "unknown",
                        ["llmRounds"] = _roundCount.ToString()
                    },
                    AgentExecutions =
                    [
                        new AgentExecutionRecord
                        {
                            AgentId = _agentId,
                            ModelDeploymentName = response.ModelId,
                            Messages = _accumulatedMessages.ToList(),
                            ToolCalls = _accumulatedToolCalls.ToList(),
                            ExecutionDurationMs = _totalElapsedMs,
                            Success = true,
                            ResponseContent = response.Text
                        }
                    ]
                };

                ResetAccumulators();
                _ = PersistTraceAsync(trace);
            }

            return response;
        }
        catch (Exception ex)
        {
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            _totalElapsedMs += elapsed;

            var trace = new ConversationTrace
            {
                SessionId = sessionId,
                UserInput = ExtractUserInput(requestMessages),
                TotalDurationMs = _totalElapsedMs,
                IsErrored = true,
                ErrorMessage = ex.Message,
                Metadata =
                {
                    ["traceType"] = "agent",
                    ["agentId"] = _agentId,
                    ["availableTools"] = string.Join(", ", _availableToolsSnapshot ?? availableTools),
                    ["llmRounds"] = _roundCount.ToString()
                },
                AgentExecutions =
                [
                    new AgentExecutionRecord
                    {
                        AgentId = _agentId,
                        Messages = _accumulatedMessages.ToList(),
                        ToolCalls = _accumulatedToolCalls.ToList(),
                        ExecutionDurationMs = _totalElapsedMs,
                        Success = false,
                        ErrorMessage = ex.Message
                    }
                ]
            };

            ResetAccumulators();
            _ = PersistTraceAsync(trace);
            throw;
        }
    }

    /// <summary>
    /// Emits live tool call/result events for real-time dashboard visualization.
    /// </summary>
    private void EmitLiveToolEvents(ChatMessage message)
    {
        if (_liveChannel is null || message.Contents is null) return;

        foreach (var content in message.Contents)
        {
            if (content is FunctionCallContent fc)
            {
                _liveChannel.Write(new LiveEvent
                {
                    Type = LiveEvent.Types.ToolCall,
                    AgentName = _agentId,
                    ToolName = fc.Name,
                    State = LiveEvent.States.CallingTools,
                });
            }
            else if (content is FunctionResultContent fr)
            {
                _liveChannel.Write(new LiveEvent
                {
                    Type = LiveEvent.Types.ToolResult,
                    AgentName = _agentId,
                    ToolName = fr.CallId ?? "unknown",
                    State = LiveEvent.States.ProcessingPrompt,
                });
            }
        }
    }

    private async Task PersistTraceAsync(ConversationTrace trace)
    {
        try
        {
            await _repository.InsertTraceAsync(trace).ConfigureAwait(false);
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
