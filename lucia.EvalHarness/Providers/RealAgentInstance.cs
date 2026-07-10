using lucia.Agents.Abstractions;
using lucia.EvalHarness.Evaluation;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Providers;

/// <summary>
/// Describes a real lucia agent instance ready for evaluation.
/// Contains the actual agent with its real system prompt, tools, and skills —
/// only the LLM backend is swapped to an inference backend.
/// Implements <see cref="IAsyncDisposable"/> so the owned <see cref="IChatClient"/> can be
/// released as soon as evaluation is complete. <see cref="RealAgentFactory.DisposeAsync"/>
/// provides a safety-net disposal for any instances not disposed by the caller.
/// </summary>
public sealed class RealAgentInstance : IAsyncDisposable
{
    private int _disposed;

    public required string AgentName { get; init; }
    public required ILuciaAgent Agent { get; init; }
    public required string DatasetFile { get; init; }

    /// <summary>
    /// When conversation tracing is enabled, captures the full ordered conversation history
    /// (system prompt, user, assistant, tool calls/results).
    /// Call <see cref="ConversationTracer.Reset"/> between test cases.
    /// </summary>
    public ConversationTracer? Tracer { get; init; }

    /// <summary>
    /// The outermost <see cref="IChatClient"/> in the pipeline for this instance, set by
    /// <see cref="RealAgentFactory"/>. Disposing the instance releases the underlying socket
    /// handles owned by the backend client (Ollama, OpenAI, etc.).
    /// </summary>
    internal IChatClient? OwnedChatClient { get; init; }

    /// <inheritdoc/>
    /// <remarks>
    /// Idempotent: subsequent calls after the first are no-ops, so the factory's
    /// safety-net disposal can call this without risk of double-dispose.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (OwnedChatClient is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (OwnedChatClient is IDisposable syncDisposable)
            syncDisposable.Dispose();
    }
}
