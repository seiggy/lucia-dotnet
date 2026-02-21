using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A2A;

namespace lucia.Tests.TestDoubles;

internal sealed class StubTaskManager : ITaskManager
{
    private readonly Func<MessageSendParams, CancellationToken, Task<A2AResponse?>> _sendMessage;

    public StubTaskManager(Func<MessageSendParams, CancellationToken, Task<A2AResponse?>>? sendMessage = null)
    {
        _sendMessage = sendMessage ?? ((messageSendParams, _) => Task.FromResult<A2AResponse?>(new AgentTask
        {
            Id = Guid.NewGuid().ToString(),
            ContextId = Guid.NewGuid().ToString(),
            Status = new AgentTaskStatus { State = TaskState.Completed },
        }));
    }

    public List<MessageSendParams> CapturedSendMessageParams { get; private set; } = [];

    public MessageSendParams? LastSendMessageParams => CapturedSendMessageParams.LastOrDefault();

    public CancellationToken LastCancellationToken { get; private set; }

    public Func<MessageSendParams, CancellationToken, Task<A2AResponse>>? OnMessageReceived { get; set; }
        = (_, _) => Task.FromResult<A2AResponse>(new AgentMessage());

    public Func<AgentTask, CancellationToken, Task> OnTaskCreated { get; set; } = (_, _) => Task.CompletedTask;

    public Func<AgentTask, CancellationToken, Task> OnTaskCancelled { get; set; } = (_, _) => Task.CompletedTask;

    public Func<AgentTask, CancellationToken, Task> OnTaskUpdated { get; set; } = (_, _) => Task.CompletedTask;

    public Func<string, CancellationToken, Task<AgentCard>> OnAgentCardQuery { get; set; }
        = (url, _) => Task.FromResult(new AgentCard { Url = url, Name = url });

    public Task<AgentTask> CreateTaskAsync(string? contextId = null, string? taskId = null, CancellationToken cancellationToken = default)
    {
        var task = new AgentTask
        {
            Id = taskId ?? Guid.NewGuid().ToString(),
            ContextId = contextId ?? Guid.NewGuid().ToString(),
            Status = new AgentTaskStatus { State = TaskState.Submitted },
        };

        return Task.FromResult(task);
    }

    public Task<AgentTask?> CancelTaskAsync(TaskIdParams taskIdParams, CancellationToken cancellationToken = default)
        => Task.FromResult<AgentTask?>(null);

    public Task<AgentTask?> GetTaskAsync(TaskQueryParams taskIdParams, CancellationToken cancellationToken = default)
        => Task.FromResult<AgentTask?>(null);

    public Task<A2AResponse?> SendMessageAsync(MessageSendParams messageSendParams, CancellationToken cancellationToken = default)
    {
        CapturedSendMessageParams.Add(messageSendParams);
        LastCancellationToken = cancellationToken;
        return _sendMessage(messageSendParams, cancellationToken);
    }

    public IAsyncEnumerable<A2AEvent> SendMessageStreamingAsync(MessageSendParams messageSendParams, CancellationToken cancellationToken = default)
        => AsyncEnumerable.Empty<A2AEvent>();

    public IAsyncEnumerable<A2AEvent> SubscribeToTaskAsync(TaskIdParams taskIdParams, CancellationToken cancellationToken = default)
        => AsyncEnumerable.Empty<A2AEvent>();

    public Task<TaskPushNotificationConfig?> SetPushNotificationAsync(TaskPushNotificationConfig pushNotificationConfig, CancellationToken cancellationToken = default)
        => Task.FromResult<TaskPushNotificationConfig?>(pushNotificationConfig);

    public Task<TaskPushNotificationConfig?> GetPushNotificationAsync(GetTaskPushNotificationConfigParams? notificationConfigParams, CancellationToken cancellationToken = default)
        => Task.FromResult<TaskPushNotificationConfig?>(null);

    public Task UpdateStatusAsync(string taskId, TaskState status, AgentMessage? message = null, bool final = false, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ReturnArtifactAsync(string taskId, Artifact artifact, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
