using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Tests.TestDoubles;

/// <summary>
/// Test <see cref="IChatClient"/> that counts how many times disposal is invoked.
/// Used to verify idempotent disposal and ownership-transfer patterns in
/// <see cref="lucia.EvalHarness.Providers.RealAgentInstance"/>.
/// </summary>
internal sealed class CountingChatClient : IChatClient, IAsyncDisposable
{
    public int DisposeCount { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public object? GetService(Type serviceType, object? key = null) => null;

    public void Dispose() { } // disposal tracked via IAsyncDisposable.DisposeAsync

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }
}
