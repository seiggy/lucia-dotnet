using System.Runtime.CompilerServices;
using System.Text.Json;
using lucia.Agents.Orchestration;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Custom AIAgent implementation that delegates message processing to LuciaOrchestrator.
/// This provides the AIAgent interface required for agent registry integration while
/// leveraging the orchestrator's workflow-based routing and execution.
/// Uses IAgentThreadFactory to support pluggable thread implementations (in-memory, Redis-backed, etc.)
/// </summary>
internal sealed class OrchestratorAIAgent : AIAgent
{
    private readonly LuciaOrchestrator _orchestrator;
    private readonly IAgentThreadFactory _threadFactory;
    private readonly string _name;
    private readonly string _description;

    public OrchestratorAIAgent(
        LuciaOrchestrator orchestrator,
        IAgentThreadFactory threadFactory,
        string name,
        string description)
    {
        _orchestrator = orchestrator;
        _threadFactory = threadFactory;
        _name = name;
        _description = description;
    }

    public override string? Name => _name;
    public override string? Description => _description;

    public override AgentThread GetNewThread() =>
        _threadFactory.CreateThread();

    public override AgentThread DeserializeThread(
        JsonElement serializedThread,
        JsonSerializerOptions? jsonSerializerOptions = null) =>
        _threadFactory.DeserializeThread(serializedThread, jsonSerializerOptions);

    public override async Task<AgentRunResponse> RunAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        thread ??= GetNewThread();

        // Extract the last user message
        var userMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
        if (userMessage == null)
        {
            throw new InvalidOperationException("No user message found in chat messages.");
        }

        // Process through orchestrator
        var responseText = await _orchestrator.ProcessRequestAsync(
            userMessage.Text,
            cancellationToken);

        // Create response message
        var responseMessage = new ChatMessage(ChatRole.Assistant, responseText)
        {
            MessageId = Guid.NewGuid().ToString("N"),
            AuthorName = DisplayName
        };

        // Notify thread of messages
        await NotifyThreadOfNewMessagesAsync(
            thread,
            messages.Append(responseMessage),
            cancellationToken);

        return new AgentRunResponse
        {
            AgentId = Id,
            ResponseId = Guid.NewGuid().ToString("N"),
            Messages = [responseMessage]
        };
    }

    public override async IAsyncEnumerable<AgentRunResponseUpdate> RunStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentThread? thread = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        thread ??= GetNewThread();

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
            cancellationToken);

        // Create response message
        var responseMessage = new ChatMessage(ChatRole.Assistant, responseText)
        {
            MessageId = Guid.NewGuid().ToString("N"),
            AuthorName = DisplayName
        };

        // Notify thread of messages
        await NotifyThreadOfNewMessagesAsync(
            thread,
            messages.Append(responseMessage),
            cancellationToken);

        // Yield single streaming update
        yield return new AgentRunResponseUpdate
        {
            AgentId = Id,
            AuthorName = DisplayName,
            Role = ChatRole.Assistant,
            Contents = responseMessage.Contents,
            ResponseId = Guid.NewGuid().ToString("N"),
            MessageId = Guid.NewGuid().ToString("N")
        };
    }
}
