using System.Runtime.CompilerServices;
using System.Text.Json;
using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Integration;
using lucia.Agents.Orchestration;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Agents;

/// <summary>
/// A2A agent adapter that exposes the <see cref="LuciaEngine"/> workflow engine
/// through the Microsoft Agent Framework <see cref="AIAgent"/> interface.
/// Handles agent card metadata, session lifecycle, and message extraction.
/// </summary>
public sealed class OrchestratorAgent : AIAgent, ILuciaAgent
{
    private readonly LuciaEngine _engine;
    private readonly IAgentSessionFactory _sessionFactory;
    private readonly ILogger<OrchestratorAgent> _logger;
    private readonly IServer _server;
    private readonly AgentCard _agentCard;

    public override string Name => "Orchestrator";
    public override string Description => "Intelligent orchestrator that routes requests to specialized agents based on intent and capabilities";

    public OrchestratorAgent(
        LuciaEngine engine,
        IAgentSessionFactory sessionFactory,
        IServer server,
        ILoggerFactory loggerFactory)
    {
        _engine = engine;
        _sessionFactory = sessionFactory;
        _logger = loggerFactory.CreateLogger<OrchestratorAgent>();
        _server = server;

        var orchestrationSkill = new AgentSkill
        {
            Id = "id_orchestrator",
            Name = "Orchestration",
            Description = "Intelligent routing and coordination of requests across multiple specialized agents",
            Tags = ["orchestration", "routing", "multi-agent", "coordination"],
            Examples =
            [
                "Turn on the kitchen lights",
                "Play some music in the living room",
                "What's the status of the bedroom light?",
                "Dim the office lights and play relaxing music"
            ]
        };

        var agentUrl = ResolveServerUrl();

        _agentCard = new AgentCard
        {
            Url = agentUrl + "/agent",
            Name = "orchestrator",
            Description = "Intelligent #orchestrator that #routes requests to specialized agents based on intent and capabilities",
            Capabilities = new AgentCapabilities
            {
                PushNotifications = false,
                StateTransitionHistory = true,
                Streaming = true
            },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = [orchestrationSkill],
            Version = "1.0.0"
        };
    }

    // --- ILuciaAgent implementation ---

    public AgentCard GetAgentCard() => _agentCard;

    public AIAgent GetAIAgent() => this;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing OrchestratorAgent...");
        _agentCard.Url = ResolveServerUrl() + "/agent";
        _logger.LogInformation("OrchestratorAgent initialized successfully");
        return Task.CompletedTask;
    }

    // --- AIAgent session lifecycle ---

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

    // --- AIAgent request processing ---

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        session ??= await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        var userMessage = messages.LastOrDefault(m => m.Role == ChatRole.User)
            ?? throw new InvalidOperationException("No user message found in chat messages.");

        var sessionId = ResolveSessionId(userMessage, session);

        var responseText = await _engine.ProcessRequestAsync(
            userMessage.Text,
            taskId: null,
            sessionId: sessionId,
            cancellationToken).ConfigureAwait(false);

        var responseMessage = new ChatMessage(ChatRole.Assistant, responseText.Text)
        {
            MessageId = Guid.NewGuid().ToString("N"),
            AuthorName = Name,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["lucia.needsInput"] = responseText.NeedsInput
            }
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
        session ??= await CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        var userMessage = messages.LastOrDefault(m => m.Role == ChatRole.User)
            ?? throw new InvalidOperationException("No user message found in chat messages.");

        var streamSessionId = ResolveSessionId(userMessage, session);
        var responseText = await _engine.ProcessRequestAsync(
            userMessage.Text,
            taskId: null,
            sessionId: streamSessionId,
            cancellationToken).ConfigureAwait(false);

        var responseMessage = new ChatMessage(ChatRole.Assistant, responseText.Text)
        {
            MessageId = Guid.NewGuid().ToString("N"),
            AuthorName = Name,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["lucia.needsInput"] = responseText.NeedsInput
            }
        };

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

    // --- Private helpers ---

    private string ResolveServerUrl()
    {
        var serverAddressesFeature = _server.Features.Get<IServerAddressesFeature>();
        if (serverAddressesFeature?.Addresses != null && serverAddressesFeature.Addresses.Any())
        {
            return serverAddressesFeature.Addresses.First();
        }

        // Return empty so callers produce a relative path (e.g. "/agent")
        // which the HA plugin resolves against the repository base URL.
        return string.Empty;
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
        if (userMessage.AdditionalProperties?.TryGetValue("a2a.contextId", out var ctxId) == true
            && ctxId?.ToString() is { Length: > 0 } ctx)
        {
            return ctx;
        }

        if (ExtractDeviceId(userMessage.Text) is { Length: > 0 } deviceId)
        {
            return deviceId;
        }

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

        var contextStart = messageText.IndexOf("REQUEST_CONTEXT:", StringComparison.Ordinal);
        if (contextStart < 0)
            return null;

        var jsonStart = messageText.IndexOf('{', contextStart);
        if (jsonStart < 0)
            return null;

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
                && id.GetString() is { Length: > 0 } devId)
            {
                return devId;
            }
        }
        catch (JsonException)
        {
            // Malformed context block — fall through
        }

        return null;
    }
}
