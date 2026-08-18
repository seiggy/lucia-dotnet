using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Tests.TestDoubles;

/// <summary>
/// Provider-agnostic <see cref="IChatClient"/> double that records the <see cref="ChatOptions"/>
/// it receives from a <see cref="lucia.EvalHarness.Providers.ParameterInjectingChatClient"/>.
/// Used to assert how determinism knobs are mapped onto <see cref="ChatOptions"/> without any
/// real backend or protocol serialization.
/// </summary>
internal sealed class CapturingChatClient : IChatClient
{
    public ChatOptions? CapturedOptions { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CapturedOptions = options;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        CapturedOptions = options;
        yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? key = null) => null;

    public void Dispose() { }
}
