using System.Collections.Concurrent;

namespace lucia.Tests.TestDoubles;

/// <summary>
/// <see cref="HttpMessageHandler"/> that records every <see cref="HttpRequestMessage"/> it receives.
/// Used to verify outgoing requests carry the expected headers.
/// </summary>
internal sealed class CapturingHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<HttpRequestMessage> _queue = new();

    public IReadOnlyList<HttpRequestMessage> Captured => [.. _queue];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _queue.Enqueue(request);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
