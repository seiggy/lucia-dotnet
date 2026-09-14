using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using System.Net;

namespace lucia.Tests.Integration;

/// <summary>
/// HTTP-level regression for the health-flood fix (the named "health-checks" output-cache policy in
/// <c>Extensions.AddServiceDefaults</c> / <c>MapDefaultEndpoints</c>).
///
/// Exercises real endpoint behaviour over Kestrel rather than inspecting DI registrations:
///   (a) two rapid GET /health requests execute an expensive health check only ONCE inside the
///       10-second cache window, proving healthy (200) /health is cached;
///   (b) an unrelated GET endpoint is NOT cached (the policy is named, not a base policy), so its
///       handler runs on every request; and
///   (c) two rapid GET /health requests against a degraded instance both return 503 and execute the
///       expensive check only ONCE, proving the 503 unhealthy response is cached too (the built-in
///       default policy caches only 200, so this guards the HealthCheckOutputCachePolicy override).
/// </summary>
public sealed class HealthCheckCachingTests
{
    private int _healthCheckExecutions;
    private int _unrelatedEndpointExecutions;
    private int _unhealthyCheckExecutions;
    private int _cookie503Executions;

    [Fact]
    public async Task Health_IsCached_ButUnrelatedEndpointIsNot()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddServiceDefaults();

        // Expensive check surfaced through /health; counts real executions.
        builder.Services.AddHealthChecks().AddCheck(
            "expensive",
            () =>
            {
                Interlocked.Increment(ref _healthCheckExecutions);
                return HealthCheckResult.Healthy();
            });

        await using var app = builder.Build();
        app.MapDefaultEndpoints();
        app.MapGet("/uncached", () =>
        {
            Interlocked.Increment(ref _unrelatedEndpointExecutions);
            return "ok";
        });

        await app.StartAsync();

        var baseAddress = app.Urls.First();
        using var client = new HttpClient { BaseAddress = new Uri(baseAddress) };

        // (a) Two rapid /health requests inside the 10s window → check executes once.
        (await client.GetAsync("/health")).EnsureSuccessStatusCode();
        (await client.GetAsync("/health")).EnsureSuccessStatusCode();
        Assert.Equal(1, Volatile.Read(ref _healthCheckExecutions));

        // (b) An unrelated GET endpoint is not covered by the named policy → runs every time.
        (await client.GetAsync("/uncached")).EnsureSuccessStatusCode();
        (await client.GetAsync("/uncached")).EnsureSuccessStatusCode();
        Assert.Equal(2, Volatile.Read(ref _unrelatedEndpointExecutions));

        await app.StopAsync();
    }

    [Fact]
    public async Task UnhealthyHealth_Returns503_AndIsCached()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddServiceDefaults();

        // Degraded instance: the expensive check reports Unhealthy, so /health returns 503.
        builder.Services.AddHealthChecks().AddCheck(
            "expensive",
            () =>
            {
                Interlocked.Increment(ref _unhealthyCheckExecutions);
                return HealthCheckResult.Unhealthy();
            });

        await using var app = builder.Build();
        app.MapDefaultEndpoints();

        await app.StartAsync();

        var baseAddress = app.Urls.First();
        using var client = new HttpClient { BaseAddress = new Uri(baseAddress) };

        // Two rapid /health requests inside the 10s window against a degraded instance: both must
        // return 503 and the expensive check must execute only once, proving the 503 is cached.
        var first = await client.GetAsync("/health");
        var second = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal(1, Volatile.Read(ref _unhealthyCheckExecutions));

        await app.StopAsync();
    }

    [Fact]
    public async Task Cached503WithSetCookie_IsNotStored()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddServiceDefaults();

        await using var app = builder.Build();
        app.MapDefaultEndpoints();

        // An endpoint under the same named "health-checks" policy that returns 503 AND a Set-Cookie
        // header. HealthCheckOutputCachePolicy widens the DefaultPolicy status safeguard to admit
        // 503, but the DefaultPolicy Set-Cookie safeguard must still block storage so a degraded
        // response carrying a cookie is never replayed from cache to another client. Proof: the
        // handler executes on every request instead of being served once from cache.
        app.MapGet("/cookie-503", (HttpContext ctx) =>
        {
            Interlocked.Increment(ref _cookie503Executions);
            ctx.Response.Headers.SetCookie = "session=secret";
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return Results.Empty;
        }).CacheOutput("health-checks");

        await app.StartAsync();

        var baseAddress = app.Urls.First();
        using var client = new HttpClient { BaseAddress = new Uri(baseAddress) };

        var first = await client.GetAsync("/cookie-503");
        var second = await client.GetAsync("/cookie-503");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal(2, Volatile.Read(ref _cookie503Executions));

        await app.StopAsync();
    }
}
