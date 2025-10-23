using System;
using System.Collections.Concurrent;
using System.Net.Http;

namespace lucia.Tests.TestDoubles;

internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly ConcurrentDictionary<string, HttpClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public HttpClient CreateClient(string name)
    {
        return _clients.GetOrAdd(name, static _ => new HttpClient(new HttpClientHandler(), disposeHandler: true));
    }
}
