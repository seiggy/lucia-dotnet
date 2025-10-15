using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Agents.AI.Workflows;

namespace lucia.Tests.TestDoubles;

internal sealed class RecordingWorkflowContext : IWorkflowContext
{
    private readonly ConcurrentDictionary<(string Scope, string Key), object?> _state;
    private readonly List<WorkflowEvent> _events = [];
    private readonly IReadOnlyDictionary<string, string>? _traceContext;

    public RecordingWorkflowContext(
        IReadOnlyDictionary<string, string>? traceContext = null,
        IDictionary<(string Scope, string Key), object?>? initialState = null)
    {
        _traceContext = traceContext;
        _state = initialState is null
            ? new ConcurrentDictionary<(string Scope, string Key), object?>()
            : new ConcurrentDictionary<(string Scope, string Key), object?>(initialState);
    }

    public IReadOnlyList<WorkflowEvent> Events => _events;

    public IReadOnlyDictionary<(string Scope, string Key), object?> State => _state;

    public IReadOnlyDictionary<string, string>? TraceContext => _traceContext;

    public ValueTask AddEventAsync(WorkflowEvent workflowEvent)
    {
        _events.Add(workflowEvent);
        return ValueTask.CompletedTask;
    }

    public ValueTask AddEventAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken)
        => AddEventAsync(workflowEvent);

    public ValueTask QueueStateUpdateAsync<T>(string key, T? value, string? scopeName = null)
    {
        _state[(scopeName ?? string.Empty, key)] = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask QueueStateUpdateAsync<T>(string key, T? value, string? scopeName, CancellationToken cancellationToken)
        => QueueStateUpdateAsync(key, value, scopeName);

    public ValueTask QueueClearScopeAsync(string? scopeName = null)
    {
        var scope = scopeName ?? string.Empty;
        foreach (var key in _state.Keys.Where(k => k.Scope == scope).ToArray())
        {
            _state.TryRemove(key, out _);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask QueueClearScopeAsync(string? scopeName, CancellationToken cancellationToken)
        => QueueClearScopeAsync(scopeName);

    public ValueTask<T?> ReadStateAsync<T>(string key, string? scopeName = null)
    {
        if (_state.TryGetValue((scopeName ?? string.Empty, key), out var value))
        {
            return ValueTask.FromResult((T?)value);
        }

        return ValueTask.FromResult<T?>(default);
    }

    public ValueTask<T?> ReadStateAsync<T>(string key, string? scopeName, CancellationToken cancellationToken)
        => ReadStateAsync<T>(key, scopeName);

    public ValueTask<HashSet<string>> ReadStateKeysAsync(string? scopeName = null)
    {
        var scope = scopeName ?? string.Empty;
        var keys = _state.Keys
            .Where(k => k.Scope == scope)
            .Select(k => k.Key)
            .ToHashSet(StringComparer.Ordinal);

        return ValueTask.FromResult(keys);
    }

    public ValueTask<HashSet<string>> ReadStateKeysAsync(string? scopeName, CancellationToken cancellationToken)
        => ReadStateKeysAsync(scopeName);

    public ValueTask SendMessageAsync(object message, string? targetId = null)
        => ValueTask.CompletedTask;

    public ValueTask SendMessageAsync(object message, string? targetId, CancellationToken cancellationToken)
        => SendMessageAsync(message, targetId);

    public ValueTask YieldOutputAsync(object output)
        => ValueTask.CompletedTask;

    public ValueTask YieldOutputAsync(object output, CancellationToken cancellationToken)
        => YieldOutputAsync(output);

    public ValueTask RequestHaltAsync()
        => ValueTask.CompletedTask;

    public ValueTask RequestHaltAsync(CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}
