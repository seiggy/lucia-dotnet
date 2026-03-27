using A2A;

namespace lucia.Agents.Hosting;

/// <summary>
/// Implements <see cref="IAgentHandler"/> by delegating to a function.
/// Bridges the old event-based <c>OnMessageReceived</c> pattern to the
/// new A2A 1.0 handler model used by <see cref="A2AServer"/>.
/// </summary>
public sealed class DelegatingAgentHandler : IAgentHandler
{
    private readonly Func<RequestContext, AgentEventQueue, CancellationToken, Task> _executeAsync;

    public DelegatingAgentHandler(
        Func<RequestContext, AgentEventQueue, CancellationToken, Task> executeAsync)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
    }

    public Task ExecuteAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        => _executeAsync(context, eventQueue, cancellationToken);

    public Task CancelAsync(RequestContext context, AgentEventQueue eventQueue, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
