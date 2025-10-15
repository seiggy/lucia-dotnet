using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace lucia.Tests.TestDoubles;

internal sealed class StubChatClient : IChatClient
{
    private readonly Queue<Func<InvocationContext, ChatResponse>> _responses;

    public StubChatClient(IEnumerable<Func<InvocationContext, ChatResponse>> responses)
    {
        _responses = new Queue<Func<InvocationContext, ChatResponse>>(responses);
    }

    public List<IReadOnlyList<ChatMessage>> CapturedMessages { get; } = [];

    public List<ChatOptions?> CapturedOptions { get; } = [];

    public int InvocationCount { get; private set; }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var materialized = messages.ToArray();
        CapturedMessages.Add(materialized);
        CapturedOptions.Add(options);
        InvocationCount++;

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No responses configured for StubChatClient.");
        }

        var factory = _responses.Dequeue();
        var response = factory(new InvocationContext(materialized, options, cancellationToken));
        return Task.FromResult(response);
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

    internal readonly record struct InvocationContext(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options, CancellationToken CancellationToken);
}
