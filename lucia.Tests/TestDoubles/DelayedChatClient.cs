using Microsoft.Extensions.AI;

namespace lucia.Tests.TestDoubles;

internal sealed class DelayedChatClient(TimeSpan delay, ChatResponse? response = null) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        return response ?? new ChatResponse([new ChatMessage(ChatRole.Assistant, "Done")]);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
