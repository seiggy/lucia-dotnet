using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A2A;
using lucia.Agents.Orchestration.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Invokes a remote agent via the A2A protocol using <see cref="ITaskManager"/>.
/// </summary>
public sealed class RemoteAgentInvoker : IAgentInvoker
{
    private readonly AgentCard _agentCard;
    private readonly ITaskManager _taskManager;
    private readonly ILogger _logger;
    private readonly AgentInvokerOptions _options;
    private readonly TimeProvider _timeProvider;

    public string AgentId { get; }

    public RemoteAgentInvoker(
        string agentId,
        AgentCard agentCard,
        ITaskManager taskManager,
        ILogger logger,
        IOptions<AgentInvokerOptions> options,
        TimeProvider? timeProvider = null)
    {
        AgentId = agentId;
        _agentCard = agentCard ?? throw new ArgumentNullException(nameof(agentCard));
        _taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<OrchestratorAgentResponse> InvokeAsync(
        ChatMessage message,
        CancellationToken cancellationToken)
    {
        var startTimestamp = _timeProvider.GetTimestamp();
        using var linkedCts = CreateTimeoutCts(cancellationToken);

        try
        {
            var sendParams = new MessageSendParams
            {
                Message = new AgentMessage
                {
                    Role = MessageRole.User,
                    MessageId = Guid.NewGuid().ToString("N"),
                    Parts =
                    [
                        new TextPart { Text = ExtractText(message) }
                    ],
                    Extensions = _agentCard.Url is { Length: > 0 } url ? new List<string> { url } : null
                }
            };

            var response = await _taskManager.SendMessageAsync(sendParams, linkedCts.Token)
                .WaitAsync(linkedCts.Token)
                .ConfigureAwait(false);

            var (success, content, error, needsInput) = response switch
            {
                AgentTask task => MapTaskToResult(task),
                AgentMessage agentMessage => (true, ExtractAgentText(agentMessage) ?? string.Empty, (string?)null, false),
                null => (false, string.Empty, (string?)"Remote agent returned no response.", false),
                _ => (false, string.Empty, (string?)"Unsupported remote response type.", false)
            };

            return new OrchestratorAgentResponse
            {
                AgentId = AgentId,
                Content = content,
                Success = success,
                ErrorMessage = error,
                ExecutionTimeMs = ElapsedMs(startTimestamp),
                NeedsInput = needsInput
            };
        }
        catch (OperationCanceledException oce) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(oce, "Remote agent {AgentId} execution timed out after {Timeout}.", AgentId, _options.Timeout);
            return new OrchestratorAgentResponse
            {
                AgentId = AgentId,
                Content = string.Empty,
                Success = false,
                ErrorMessage = $"Agent execution timed out after {_options.Timeout.TotalMilliseconds:F0}ms.",
                ExecutionTimeMs = ElapsedMs(startTimestamp)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Remote agent {AgentId} execution failed.", AgentId);
            return new OrchestratorAgentResponse
            {
                AgentId = AgentId,
                Content = string.Empty,
                Success = false,
                ErrorMessage = ex.Message,
                ExecutionTimeMs = ElapsedMs(startTimestamp)
            };
        }
    }

    private static (bool success, string content, string? error, bool needsInput) MapTaskToResult(AgentTask task)
    {
        var text = ExtractAgentText(task.History?.LastOrDefault()) ?? string.Empty;
        var state = task.Status.State;
        var needsInput = state == TaskState.InputRequired;
        var success = state == TaskState.Completed || state == TaskState.Working || needsInput;
        string? error = success ? null : $"Agent task ended in state '{state}'.";
        return (success, text, error, needsInput);
    }

    private long ElapsedMs(long startTimestamp)
        => (long)_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;

    private CancellationTokenSource CreateTimeoutCts(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_options.Timeout > TimeSpan.Zero)
        {
            cts.CancelAfter(_options.Timeout);
        }
        return cts;
    }

    private static string ExtractText(ChatMessage message)
    {
        if (message.Contents is { Count: > 0 })
        {
            var pieces = message.Contents.OfType<TextContent>().Select(t => t.Text);
            return string.Join(' ', pieces);
        }

        return message.ToString();
    }

    private static string? ExtractAgentText(AgentMessage? message)
    {
        if (message?.Parts is { Count: > 0 })
        {
            return string.Join(' ', message.Parts.OfType<TextPart>().Select(p => p.Text));
        }

        return null;
    }
}
