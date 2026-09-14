using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Tests.TestDoubles;

internal sealed class ScriptedChatClient(
    Func<CancellationToken, Task<ChatResponse>> responseFactory) : IChatClient
{
    public static ScriptedChatClient Returning(string responseText) =>
        new(_ => Task.FromResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText))));

    public static ScriptedChatClient Throwing(Func<CancellationToken, Exception> factory) =>
        new(token => Task.FromException<ChatResponse>(factory(token)));

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        responseFactory(cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
