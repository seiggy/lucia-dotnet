using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Workflows;

namespace lucia.Tests.TestDoubles;

internal sealed class NoopWorkflowContext : IWorkflowContext
{
    public ValueTask AddEventAsync(WorkflowEvent workflowEvent)
        => ValueTask.CompletedTask;

    public ValueTask AddEventAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask SendMessageAsync(object message, string? targetId = null)
        => ValueTask.CompletedTask;

    public ValueTask SendMessageAsync(object message, string? targetId = null, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask YieldOutputAsync(object output)
        => ValueTask.CompletedTask;

    public ValueTask YieldOutputAsync(object output, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask RequestHaltAsync()
        => ValueTask.CompletedTask;

    public ValueTask<T?> ReadStateAsync<T>(string key, string? scopeName = null)
        => ValueTask.FromResult<T?>(default);

    public ValueTask<T?> ReadStateAsync<T>(string key, string? scopeName = null, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<T?>(default);

    public ValueTask<HashSet<string>> ReadStateKeysAsync(string? scopeName = null)
        => ValueTask.FromResult(new HashSet<string>());

    public ValueTask<HashSet<string>> ReadStateKeysAsync(string? scopeName = null, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new HashSet<string>());

    public ValueTask QueueStateUpdateAsync<T>(string key, T? value, string? scopeName = null)
        => ValueTask.CompletedTask;

    public ValueTask QueueStateUpdateAsync<T>(string key, T? value, string? scopeName = null, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask QueueClearScopeAsync(string? scopeName = null)
        => ValueTask.CompletedTask;

    public ValueTask QueueClearScopeAsync(string? scopeName = null, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public IReadOnlyDictionary<string, string>? TraceContext => null;
}
