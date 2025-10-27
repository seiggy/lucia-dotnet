using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A2A;
using lucia.Agents.Orchestration.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Workflow executor that wraps agent execution with context preservation and telemetry.
/// </summary>
public sealed class AgentExecutorWrapper : ReflectingExecutor<AgentExecutorWrapper>, IMessageHandler<ChatMessage, AgentResponse>
{
    /// <summary>Workflow state scope storing orchestration context.</summary>
    public const string StateScope = "orchestration";

    /// <summary>Workflow state key storing orchestration context.</summary>
    public const string StateKey = "context";

    private readonly string _agentId;
    private readonly IServiceProvider _services;
    private readonly ILogger<AgentExecutorWrapper> _logger;
    private readonly AgentExecutorWrapperOptions _options;
    private readonly AIAgent? _agent;
    private readonly AgentCard? _agentCard;
    private readonly ITaskManager? _taskManager;
    private readonly TimeProvider _timeProvider;

    /// <summary>Trace context keys used to flow task metadata.</summary>
    public static class TraceContextKeys
    {
        public const string ConversationId = "contextId";
        public const string TaskId = "taskId";
    }

    public AgentExecutorWrapper(
        string agentId,
        IServiceProvider serviceProvider,
        ILogger<AgentExecutorWrapper> logger,
        IOptions<AgentExecutorWrapperOptions> options,
        AIAgent? agent = null,
        AgentCard? agentCard = null,
        ITaskManager? taskManager = null,
        TimeProvider? timeProvider = null)
        : base(agentId)
    {
        _agentId = agentId;
        _services = serviceProvider;
        _logger = logger;
        _options = options.Value;
        _agent = agent;
        _agentCard = agentCard;
        _taskManager = taskManager;
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (_agentCard is not null && _taskManager is null)
        {
            throw new ArgumentException("Remote agent execution requires an ITaskManager instance.", nameof(taskManager));
        }
    }

    public async ValueTask<AgentResponse> HandleAsync(ChatMessage message, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var invokeEvent = new ExecutorInvokedEvent(this.Id, message);
        await context.AddEventAsync(invokeEvent, cancellationToken).ConfigureAwait(false);

        string conversationId = GetConversationId(context) ?? Guid.NewGuid().ToString("N");
        string? taskId = GetTaskId(context);

        var orchestrationContext = await LoadOrchestrationContextAsync(context, conversationId, cancellationToken).ConfigureAwait(false);

        var startTimestamp = _timeProvider.GetTimestamp();
        var linkedCts = CreateCancellationCTS(cancellationToken);

        try
        {
            (bool success, string content, string? error) result;

            if (_agent is null && _agentCard is not null)
            {
                result = await InvokeRemoteAsync(message, conversationId, taskId, linkedCts.Token).ConfigureAwait(false);
            }
            else
            {
                result = await InvokeLocalAsync(message, orchestrationContext, linkedCts.Token).ConfigureAwait(false);
            }

            orchestrationContext.PreviousAgentId = _agentId;
            await PersistContextAsync(context, orchestrationContext, cancellationToken).ConfigureAwait(false);

            var elapsed = (long)_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;
            var response = new AgentResponse
            {
                AgentId = _agentId,
                Content = result.content,
                Success = result.success,
                ErrorMessage = result.error,
                ExecutionTimeMs = elapsed
            };

            if (result.success)
            {
                await context.AddEventAsync(new ExecutorCompletedEvent(this.Id, response), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await context.AddEventAsync(new ExecutorFailedEvent(this.Id, result.error is not null ? new InvalidOperationException(result.error) : null), cancellationToken).ConfigureAwait(false);
            }

            return response;
        }
        catch (OperationCanceledException oce) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(oce, "Agent {AgentId} execution timed out after {Timeout}.", _agentId, _options.Timeout);
            await context.AddEventAsync(new ExecutorFailedEvent(this.Id, oce), cancellationToken).ConfigureAwait(false);

            return new AgentResponse
            {
                AgentId = _agentId,
                Content = string.Empty,
                Success = false,
                ErrorMessage = $"Agent execution timed out after {_options.Timeout.TotalMilliseconds:F0}ms.",
                ExecutionTimeMs = (long)_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent {AgentId} execution failed.", _agentId);
            await context.AddEventAsync(new ExecutorFailedEvent(this.Id, ex), cancellationToken).ConfigureAwait(false);

            return new AgentResponse
            {
                AgentId = _agentId,
                Content = string.Empty,
                Success = false,
                ErrorMessage = ex.Message,
                ExecutionTimeMs = (long)_timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds
            };
        }
        finally
        {
            linkedCts.Dispose();
        }
    }

    public ValueTask<AgentResponse> HandleAsync(ChatMessage message, IWorkflowContext context)
        => HandleAsync(message, context, CancellationToken.None);

    private async Task<(bool success, string content, string? error)> InvokeLocalAsync(ChatMessage message, OrchestrationContext orchestrationContext, CancellationToken cancellationToken)
    {
        var agent = ResolveAgent();
        var thread = EnsureThread(agent, orchestrationContext);

        var runTask = agent.RunAsync(message, thread, options: null, cancellationToken);
        var response = await runTask.WaitAsync(cancellationToken).ConfigureAwait(false);

        orchestrationContext.AgentThreads[_agentId] = thread;
        AppendHistory(orchestrationContext, message, response.Messages);

        return (true, response.Text, null);
    }

    private async Task<(bool success, string content, string? error)> InvokeRemoteAsync(ChatMessage message, string conversationId, string? taskId, CancellationToken cancellationToken)
    {
        if (_taskManager is null || _agentCard is null)
        {
            throw new InvalidOperationException("Remote execution is not configured.");
        }

        var sendParams = new MessageSendParams
        {
            Message = new AgentMessage
            {
                Role = MessageRole.User,
                MessageId = Guid.NewGuid().ToString("N"),
                TaskId = taskId,
                ContextId = conversationId,
                Parts =
                [
                    new TextPart { Text = ExtractText(message) }
                ],
                Extensions = _agentCard.Url is { Length: > 0 } url ? new List<string> { url } : null
            }
        };

        var sendTask = _taskManager.SendMessageAsync(sendParams, cancellationToken);
        var response = await sendTask.WaitAsync(cancellationToken).ConfigureAwait(false);

        return response switch
        {
            AgentTask task => MapTaskToResult(task),
            AgentMessage agentMessage => (true, ExtractText(agentMessage) ?? string.Empty, null),
            null => (false, string.Empty, "Remote agent returned no response."),
            _ => (false, string.Empty, "Unsupported remote response type.")
        };
    }

    private (bool success, string content, string? error) MapTaskToResult(AgentTask task)
    {
        var text = ExtractText(task.History?.LastOrDefault()) ?? string.Empty;
        var state = task.Status.State;
        var success = state == TaskState.Completed || state == TaskState.Working || state == TaskState.InputRequired;
        string? error = success ? null : $"Agent task ended in state '{state}'.";
        return (success, text, error);
    }

    private static void AppendHistory(OrchestrationContext context, ChatMessage userMessage, IList<ChatMessage>? agentMessages)
    {
        context.History.Add(userMessage);

        if (agentMessages is { Count: > 0 })
        {
            foreach (var message in agentMessages)
            {
                context.History.Add(message);
            }
        }
    }

    private async Task<OrchestrationContext> LoadOrchestrationContextAsync(IWorkflowContext workflowContext, string conversationId, CancellationToken cancellationToken)
    {
    var stored = await workflowContext.ReadStateAsync<OrchestrationContext>(StateKey, StateScope).ConfigureAwait(false);

        if (stored is null || !string.Equals(stored.ConversationId, conversationId, StringComparison.Ordinal))
        {
            return new OrchestrationContext
            {
                ConversationId = conversationId
            };
        }

        return stored;
    }

    private static string? GetConversationId(IWorkflowContext context)
        => context.TraceContext is { } trace && trace.TryGetValue(TraceContextKeys.ConversationId, out var value) ? value : null;

    private static string? GetTaskId(IWorkflowContext context)
        => context.TraceContext is { } trace && trace.TryGetValue(TraceContextKeys.TaskId, out var value) ? value : null;

    private static string ExtractText(ChatMessage message)
    {
        if (message.Contents is { Count: > 0 })
        {
            var pieces = message.Contents.OfType<TextContent>().Select(t => t.Text);
            return string.Join(' ', pieces);
        }

        return message.ToString();
    }

    private static string? ExtractText(AgentMessage? message)
    {
        if (message?.Parts is { Count: > 0 })
        {
            return string.Join(' ', message.Parts.OfType<TextPart>().Select(p => p.Text));
        }

        return null;
    }

    private AIAgent ResolveAgent()
        => _agent ?? _services.GetRequiredService<AIAgent>();

    private AgentThread EnsureThread(AIAgent agent, OrchestrationContext context)
    {
        if (context.AgentThreads.TryGetValue(_agentId, out var existing))
        {
            return existing;
        }

        var thread = agent.GetNewThread();
        context.AgentThreads[_agentId] = thread;
        return thread;
    }

    private async Task PersistContextAsync(IWorkflowContext workflowContext, OrchestrationContext orchestrationContext, CancellationToken cancellationToken)
    {
        TrimHistory(orchestrationContext);
    await workflowContext.QueueStateUpdateAsync(StateKey, orchestrationContext, StateScope).ConfigureAwait(false);
    }

    private void TrimHistory(OrchestrationContext context)
    {
        if (_options.HistoryLimit <= 0)
        {
            return;
        }

        if (context.History.Count > _options.HistoryLimit)
        {
            var excess = context.History.Count - _options.HistoryLimit;
            context.History.RemoveRange(0, excess);
        }
    }

    private CancellationTokenSource CreateCancellationCTS(CancellationToken cancellationToken)
    {
        if (_options.Timeout <= TimeSpan.Zero)
        {
            return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.Timeout);
        return cts;
    }
}
