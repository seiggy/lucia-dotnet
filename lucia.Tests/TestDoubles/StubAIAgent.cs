using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace lucia.Tests.TestDoubles;

internal sealed class StubAIAgent : AIAgent
{
    private readonly Func<IEnumerable<ChatMessage>, AgentSession?, CancellationToken, Task<AgentResponse>> _runAsync;
    private readonly Func<JsonElement, JsonSerializerOptions?, AgentSession>? _deserialize;

    public StubAIAgent(
        Func<IEnumerable<ChatMessage>, AgentSession?, CancellationToken, Task<AgentResponse>>? runAsync = null,
        Func<JsonElement, JsonSerializerOptions?, AgentSession>? deserialize = null,
        Func<AgentSession>? newSessionFactory = null,
        string? id = null,
        string? name = null)
    {
        _runAsync = runAsync ?? ((messages, session, token) => Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, "ok"))));
        _deserialize = deserialize;
        CreateSessionFactory = newSessionFactory ?? (() => new TestAgentSession());
        OverrideId = id;
        OverrideName = name;
    }

    public AgentSession? LastSession { get; private set; }

    public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public Func<AgentSession> CreateSessionFactory { get; }

    public string? OverrideId { get; }

    public string? OverrideName { get; }

    public new string Id => OverrideId ?? base.Id;

    public override string? Name => OverrideName ?? base.Name;

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
        new(CreateSessionFactory());

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedSession, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
    {
        if (_deserialize is { } factory)
        {
            return new(factory(serializedSession, jsonSerializerOptions));
        }

        return new(new TestAgentSession());
    }

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default)
        => default;

    protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        LastMessages = messages.ToList();
        LastSession = session;
        LastCancellationToken = cancellationToken;
        return _runAsync(messages, session, cancellationToken);
    }

    protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default)
        => AsyncEnumerable.Empty<AgentResponseUpdate>();

    private sealed class TestAgentSession : AgentSession
    {
    }
}
