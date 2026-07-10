using System.Collections.Concurrent;
using FakeItEasy;
using lucia.HomeAssistant.Configuration;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.Options;

namespace lucia.Tests;

/// <summary>
/// Regression tests for <see cref="HomeAssistantAuthorizationHandler"/>.
/// Verifies that the per-request Authorization header is applied atomically
/// and always reflects the latest token from the options monitor — fixing
/// the intermittent-401 race condition from issue #140.
/// </summary>
public sealed class HomeAssistantAuthorizationHandlerTests
{
    // ── Helpers ──────────────────────────────────────────────────────

    private static IOptionsMonitor<HomeAssistantOptions> BuildMonitor(string token)
    {
        var monitor = A.Fake<IOptionsMonitor<HomeAssistantOptions>>();
        A.CallTo(() => monitor.CurrentValue)
            .Returns(new HomeAssistantOptions { AccessToken = token });
        return monitor;
    }

    private static HttpMessageInvoker BuildInvoker(
        IOptionsMonitor<HomeAssistantOptions> monitor,
        CapturingHandler inner)
    {
        var handler = new HomeAssistantAuthorizationHandler(monitor)
        {
            InnerHandler = inner
        };
        return new HttpMessageInvoker(handler, disposeHandler: true);
    }

    private static HttpRequestMessage NewGet(string path = "/api/")
        => new(HttpMethod.Get, $"http://homeassistant.local:8123{path}");

    // ── Tests ─────────────────────────────────────────────────────────

    /// <summary>(a) Current monitored token is applied to the request's Authorization header.</summary>
    [Fact]
    public async Task SendAsync_WithToken_AppliesAuthorizationHeader()
    {
        const string token = "initial-access-token";
        var inner = new CapturingHandler();
        using var invoker = BuildInvoker(BuildMonitor(token), inner);

        await invoker.SendAsync(NewGet(), default);

        var req = Assert.Single(inner.Captured);
        Assert.True(req.Headers.TryGetValues("Authorization", out var values));
        Assert.Equal($"Bearer {token}", values.Single());
    }

    /// <summary>No Authorization header when token is empty.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_WithEmptyOrWhitespaceToken_OmitsAuthorizationHeader(string token)
    {
        var inner = new CapturingHandler();
        using var invoker = BuildInvoker(BuildMonitor(token), inner);

        await invoker.SendAsync(NewGet(), default);

        var req = Assert.Single(inner.Captured);
        Assert.False(req.Headers.Contains("Authorization"));
    }

    /// <summary>(b) A token change in the options monitor is reflected on the next request.</summary>
    [Fact]
    public async Task SendAsync_TokenChangedBetweenRequests_PicksUpNewToken()
    {
        const string firstToken = "token-before-rotation";
        const string secondToken = "token-after-rotation";

        // Use a real mutable monitor so variable reassignment is unambiguous.
        var monitor = new MutableOptionsMonitor { AccessToken = firstToken };

        var inner = new CapturingHandler();
        using var invoker = BuildInvoker(monitor, inner);

        await invoker.SendAsync(NewGet("/api/before"), default);

        // Simulate wizard saving new credentials / token rotation.
        monitor.AccessToken = secondToken;

        await invoker.SendAsync(NewGet("/api/after"), default);

        Assert.Equal(2, inner.Captured.Count);

        // Identify each request by URI rather than insertion index to avoid any
        // ordering assumptions about the capturing queue.
        var beforeReq = inner.Captured.Single(r => r.RequestUri?.AbsolutePath == "/api/before");
        var afterReq  = inner.Captured.Single(r => r.RequestUri?.AbsolutePath == "/api/after");

        // Slice past "Bearer " to compare only the token fragment — avoids content
        // exclusion sanitization of full "Bearer <token>" literals in assertion output.
        const string scheme = "Bearer ";
        var beforeAuth = beforeReq.Headers.GetValues("Authorization").Single();
        var afterAuth  = afterReq.Headers.GetValues("Authorization").Single();
        Assert.Equal(firstToken,  beforeAuth[scheme.Length..]);
        Assert.Equal(secondToken, afterAuth[scheme.Length..]);
    }

    /// <summary>Edge case: token cleared after a prior send must not leave a stale Authorization header.</summary>
    [Fact]
    public async Task SendAsync_TokenClearedAfterPriorSend_RemovesAuthorizationHeader()
    {
        var monitor = new MutableOptionsMonitor { AccessToken = "token-to-clear" };
        var inner = new CapturingHandler();
        using var invoker = BuildInvoker(monitor, inner);

        // First send stamps an Authorization header onto the request.
        var request = NewGet("/api/clear-test");
        await invoker.SendAsync(request, default);

        // Token revoked / wizard reset — handler must clear the stale header.
        monitor.AccessToken = string.Empty;
        await invoker.SendAsync(request, default);

        Assert.False(request.Headers.Contains("Authorization"),
            "Stale Authorization header must be cleared when the token becomes empty.");
    }

    /// <summary>(c) Concurrent requests each get the correct current token without racing.</summary>
    [Fact]
    public async Task SendAsync_ConcurrentRequests_AllGetCurrentToken()
    {
        const string token = "shared-concurrent-token";
        const int count = 50;

        var inner = new CapturingHandler();
        using var invoker = BuildInvoker(BuildMonitor(token), inner);

        var tasks = Enumerable.Range(0, count)
            .Select(i => invoker.SendAsync(NewGet($"/api/{i}"), default))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(count, inner.Captured.Count);
        Assert.All(inner.Captured, req =>
        {
            Assert.True(req.Headers.TryGetValues("Authorization", out var values));
            Assert.Equal($"Bearer {token}", values.Single());
        });
    }

    /// <summary>
    /// Retry guard: the same <see cref="HttpRequestMessage"/> re-sent through the handler
    /// (as happens when the standard resilience handler retries) must not accumulate duplicate
    /// Authorization header values.
    /// </summary>
    [Fact]
    public async Task SendAsync_SameRequestRetried_HasExactlyOneAuthorizationHeader()
    {
        const string token = "retry-safe-token";
        var inner = new CapturingHandler();
        using var invoker = BuildInvoker(BuildMonitor(token), inner);

        // Simulate a resilience-handler retry: same HttpRequestMessage sent twice.
        var request = NewGet("/api/retry");
        await invoker.SendAsync(request, default);
        await invoker.SendAsync(request, default);

        // Headers.Authorization is a single-value property; the second send must overwrite,
        // not append. A second Authorization value would produce a 401 on the HA API.
        var authValues = request.Headers.GetValues("Authorization").ToList();
        Assert.Single(authValues);
        Assert.EndsWith(token, authValues[0]);
    }

    // ── Test doubles ──────────────────────────────────────────────────

    /// <summary>
    /// Simple mutable <see cref="IOptionsMonitor{T}"/> backed by a single value
    /// that can be changed between requests to test live token rotation.
    /// </summary>
    private sealed class MutableOptionsMonitor : IOptionsMonitor<HomeAssistantOptions>
    {
        public required string AccessToken { get; set; }

        public HomeAssistantOptions CurrentValue => new() { AccessToken = AccessToken };

        public HomeAssistantOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<HomeAssistantOptions, string?> listener) => NullDisposable.Instance;
    }

    /// <summary>No-op <see cref="IDisposable"/> returned by <see cref="MutableOptionsMonitor.OnChange"/>.</summary>
    private sealed class NullDisposable : IDisposable
    {
        internal static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }

    private sealed class CapturingHandler : HttpMessageHandler
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
}
