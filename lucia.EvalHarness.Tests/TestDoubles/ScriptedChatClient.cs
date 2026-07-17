using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Tests.TestDoubles;

/// <summary>
/// Configurable test <see cref="IChatClient"/> that either returns a scripted response
/// or throws a supplied exception. Used to exercise deadline / timeout classification
/// and result recording without a live model backend.
/// </summary>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly string? _responseText;
    private readonly Func<CancellationToken, Exception>? _throw;

    private ScriptedChatClient(string? responseText, Func<CancellationToken, Exception>? @throw)
    {
        _responseText = responseText;
        _throw = @throw;
    }

    public static ScriptedChatClient Returning(string responseText) => new(responseText, null);

    public static ScriptedChatClient Throwing(Func<CancellationToken, Exception> factory) => new(null, factory);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_throw is not null)
            throw _throw(cancellationToken);

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _responseText ?? string.Empty)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public object? GetService(Type serviceType, object? key = null) => null;

    public void Dispose() { }
}
