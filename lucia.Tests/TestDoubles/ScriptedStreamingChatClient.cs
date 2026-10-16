using Microsoft.Extensions.AI;

namespace lucia.Tests.TestDoubles;

internal sealed class ScriptedStreamingChatClient : IChatClient
{
    private readonly IReadOnlyList<ChatResponseUpdate> _updates;

    public ScriptedStreamingChatClient(IEnumerable<ChatResponseUpdate> updates)
    {
        _updates = updates.ToList();
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Use GetStreamingResponseAsync.");

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in _updates)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
