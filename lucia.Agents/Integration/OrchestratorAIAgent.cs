using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        // Resolve session key for multi-turn conversation continuity via Redis cache.
        // Priority: A2A contextId > device.id from HA context > session object > new GUID
        var sessionId = ResolveSessionId(userMessage, session);

        // Process through orchestrator
        var responseText = await _orchestrator.ProcessRequestAsync(
            userMessage.Text,
            taskId: null,
            sessionId: sessionId,
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
        var streamSessionId = ResolveSessionId(userMessage, session);
        var responseText = await _orchestrator.ProcessRequestAsync(
            userMessage.Text,
            taskId: null,
            sessionId: streamSessionId,
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

    /// <summary>
    /// Resolves the session key for Redis cache lookup.
    /// Priority: A2A contextId → device.id from HA request context → session object → new GUID.
    /// Home Assistant doesn't reliably provide a persistent conversation_id, so we fall back
    /// to the device.id embedded in the request context. Since the service is single-tenant
    /// (runs on the user's own network), device.id is sufficient for session continuity.
    /// </summary>
    private static string ResolveSessionId(ChatMessage userMessage, AgentSession? session)
    {
        // 1. A2A contextId (set by the A2A handler from the client's contextId)
        if (userMessage.AdditionalProperties?.TryGetValue("a2a.contextId", out var ctxId) == true
            && ctxId?.ToString() is { Length: > 0 } ctx)
        {
            return ctx;
        }

        // 2. Extract device.id from the HA REQUEST_CONTEXT block in the prompt text
        if (ExtractDeviceId(userMessage.Text) is { Length: > 0 } deviceId)
        {
            return deviceId;
        }

        // 3. Fall back to session object's ID or generate new
        return (session as OrchestratorInMemorySession)?.SessionId
            ?? Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Extracts the device.id from the Home Assistant REQUEST_CONTEXT JSON block
    /// embedded in the user prompt text. Returns null if not found.
    /// </summary>
    internal static string? ExtractDeviceId(string? messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText))
            return null;

        // Find the REQUEST_CONTEXT JSON block
        var contextStart = messageText.IndexOf("REQUEST_CONTEXT:", StringComparison.Ordinal);
        if (contextStart < 0)
            return null;

        // Find the JSON object boundaries
        var jsonStart = messageText.IndexOf('{', contextStart);
        if (jsonStart < 0)
            return null;

        // Find matching closing brace (handle nested objects)
        var depth = 0;
        var jsonEnd = -1;
        for (var i = jsonStart; i < messageText.Length; i++)
        {
            switch (messageText[i])
            {
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0) { jsonEnd = i; goto found; }
                    break;
            }
        }
        found:

        if (jsonEnd < 0)
            return null;

        try
        {
            var jsonSpan = messageText.AsSpan(jsonStart, jsonEnd - jsonStart + 1);
            using var doc = JsonDocument.Parse(jsonSpan.ToString());

            if (doc.RootElement.TryGetProperty("device", out var device)
                && device.TryGetProperty("id", out var id)
                && id.GetString() is { Length: > 0 } deviceId)
            {
                return deviceId;
            }
        }
        catch (JsonException)
        {
            // Malformed context block — fall through
        }

        return null;
    }
}
