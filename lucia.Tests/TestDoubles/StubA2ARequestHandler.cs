using A2A;

namespace lucia.Tests.TestDoubles;

/// <summary>
/// Stub implementation of <see cref="IA2ARequestHandler"/> for unit tests.
/// Captures send message requests for assertion and returns configurable responses.
/// </summary>
internal sealed class StubA2ARequestHandler : IA2ARequestHandler
{
    private readonly Func<SendMessageRequest, CancellationToken, Task<SendMessageResponse>> _sendMessage;

    public StubA2ARequestHandler(Func<SendMessageRequest, CancellationToken, Task<SendMessageResponse>>? sendMessage = null)
    {
        _sendMessage = sendMessage ?? ((_, _) => Task.FromResult(new SendMessageResponse
        {
            Task = new AgentTask
            {
                Id = Guid.NewGuid().ToString(),
                ContextId = Guid.NewGuid().ToString(),
                Status = new A2A.TaskStatus { State = TaskState.Completed },
            }
        }));
    }

    public List<SendMessageRequest> CapturedSendMessageParams { get; private set; } = [];

    public SendMessageRequest? LastSendMessageParams => CapturedSendMessageParams.LastOrDefault();

    public CancellationToken LastCancellationToken { get; private set; }

    public Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        CapturedSendMessageParams.Add(request);
        LastCancellationToken = cancellationToken;
        return _sendMessage(request, cancellationToken);
    }

    public IAsyncEnumerable<StreamResponse> SendStreamingMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
        => AsyncEnumerable.Empty<StreamResponse>();

    public Task<AgentTask> GetTaskAsync(GetTaskRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentTask());

    public Task<ListTasksResponse> ListTasksAsync(ListTasksRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new ListTasksResponse());

    public Task<AgentTask> CancelTaskAsync(CancelTaskRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentTask());

    public IAsyncEnumerable<StreamResponse> SubscribeToTaskAsync(SubscribeToTaskRequest request, CancellationToken cancellationToken = default)
        => AsyncEnumerable.Empty<StreamResponse>();

    public Task<TaskPushNotificationConfig> CreateTaskPushNotificationConfigAsync(CreateTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new TaskPushNotificationConfig());

    public Task<TaskPushNotificationConfig> GetTaskPushNotificationConfigAsync(GetTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new TaskPushNotificationConfig());

    public Task<ListTaskPushNotificationConfigResponse> ListTaskPushNotificationConfigAsync(ListTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new ListTaskPushNotificationConfigResponse());

    public Task DeleteTaskPushNotificationConfigAsync(DeleteTaskPushNotificationConfigRequest request, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<AgentCard> GetExtendedAgentCardAsync(GetExtendedAgentCardRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentCard { Name = "stub" });
}
