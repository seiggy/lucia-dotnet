using System.Runtime.CompilerServices;
using System.Text.Json;
using lucia.Agents.Orchestration;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Integration;

/// <summary>
/// Custom AIAgent implementation that delegates message processing to LuciaOrchestrator.
/// This provides the AIAgent interface required for agent registry integration while
/// leveraging the orchestrator's workflow-based routing and execution.
/// Uses IAgentSessionFactory to support pluggable session implementations (in-memory, Redis-backed, etc.)
/// </summary>
internal sealed class OrchestratorAIAgent : AIAgent
{
    private readonly LuciaOrchestrator _orchestrator;
    private readonly IAgentSessionFactory _sessionFactory;
    private readonly string _name;
    private readonly string _description;

    public OrchestratorAIAgent(
        LuciaOrchestrator orchestrator,
        IAgentSessionFactory sessionFactory,
        string name,
        string description)
    {
        _orchestrator = orchestrator;
        _sessionFactory = sessionFactory;
        _name = name;
        _description = description;
    }

    public override string? Name => _name;
    public override string? Description => _description;

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
        new(_sessionFactory.CreateSession());

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedSession,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default) =>
        new(_sessionFactory.DeserializeSession(serializedSession, jsonSerializerOptions));

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (session is OrchestratorInMemorySession inMemorySession)
        {
            return new(inMemorySession.Serialize(jsonSerializerOptions));
        }

        return default;
    }

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        session ??= await CreateSessionAsync(cancellationToken);

        // Extract the last user message
        var userMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
        if (userMessage == null)
        {
            throw new InvalidOperationException("No user message found in chat messages.");
        }

        // Process through orchestrator
        var responseText = await _orchestrator.ProcessRequestAsync(
            userMessage.Text,
            taskId: null,
            sessionId: null,
            cancellationToken);

        // Create response message
        var responseMessage = new ChatMessage(ChatRole.Assistant, responseText)
        {
            MessageId = Guid.NewGuid().ToString("N"),
            AuthorName = Name
        };

        return new AgentResponse
        {
            AgentId = Id,
            ResponseId = Guid.NewGuid().ToString("N"),
            Messages = [responseMessage]
        };
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        session ??= await CreateSessionAsync(cancellationToken);

        // Extract the last user message
        var userMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
        if (userMessage == null)
        {
            throw new InvalidOperationException("No user message found in chat messages.");
        }

        // Process through orchestrator (non-streaming for now)
        // TODO: Phase 4 - Implement true streaming through workflow pipeline
        var responseText = await _orchestrator.ProcessRequestAsync(
            userMessage.Text,
            taskId: null,
            sessionId: null,
            cancellationToken);

        // Create response message
        var responseMessage = new ChatMessage(ChatRole.Assistant, responseText)
        {
            MessageId = Guid.NewGuid().ToString("N"),
            AuthorName = Name
        };

        // Yield single streaming update
        yield return new AgentResponseUpdate
        {
            AgentId = Id,
            AuthorName = Name,
            Role = ChatRole.Assistant,
            Contents = responseMessage.Contents,
            ResponseId = Guid.NewGuid().ToString("N"),
            MessageId = Guid.NewGuid().ToString("N")
        };
    }
}
