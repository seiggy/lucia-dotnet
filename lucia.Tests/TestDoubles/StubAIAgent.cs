using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace lucia.Tests.TestDoubles;

internal sealed class StubAIAgent : AIAgent
{
    private readonly Func<IEnumerable<ChatMessage>, AgentThread?, CancellationToken, Task<AgentRunResponse>> _runAsync;
    private readonly Func<JsonElement, JsonSerializerOptions?, AgentThread>? _deserialize;

    public StubAIAgent(
        Func<IEnumerable<ChatMessage>, AgentThread?, CancellationToken, Task<AgentRunResponse>>? runAsync = null,
        Func<JsonElement, JsonSerializerOptions?, AgentThread>? deserialize = null,
        Func<AgentThread>? newThreadFactory = null,
        string? id = null,
        string? name = null)
    {
        _runAsync = runAsync ?? ((messages, thread, token) => Task.FromResult(new AgentRunResponse(new ChatMessage(ChatRole.Assistant, "ok"))));
        _deserialize = deserialize;
        GetNewThreadFactory = newThreadFactory ?? (() => new TestAgentThread());
        OverrideId = id;
        OverrideName = name;
    }

    public AgentThread? LastThread { get; private set; }

    public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public Func<AgentThread> GetNewThreadFactory { get; }

    public string? OverrideId { get; }

    public string? OverrideName { get; }

    public override string Id => OverrideId ?? base.Id;

    public override string? Name => OverrideName ?? base.Name;

    public override AgentThread GetNewThread() => GetNewThreadFactory();

    public override AgentThread DeserializeThread(JsonElement serializedThread, JsonSerializerOptions? jsonSerializerOptions = null)
    {
        if (_deserialize is { } factory)
        {
            return factory(serializedThread, jsonSerializerOptions);
        }

        return new TestAgentThread();
    }

    public override Task<AgentRunResponse> RunAsync(IEnumerable<ChatMessage> messages, AgentThread? thread = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        LastMessages = messages.ToList();
        LastThread = thread;
        LastCancellationToken = cancellationToken;
        return _runAsync(messages, thread, cancellationToken);
    }

    public override IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(IEnumerable<ChatMessage> messages, AgentThread? thread = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        => AsyncEnumerable.Empty<AgentRunResponseUpdate>();

    private sealed class TestAgentThread : AgentThread
    {
    }
}
