using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Extensions.Hosting;

// The built-in DefaultPolicy that backs every named output-cache policy only stores 200 responses.
// Health endpoints report degraded/unhealthy state as 503, so without this policy degraded-state
// polling bypasses the cache and re-runs the expensive checks on every request. Appended after the
// default policy in the named "health-checks" policy, this re-enables storage for 503 only, so both
// healthy (200) and unhealthy (503) results are cached for the configured window while other status
// codes remain uncached.
//
// DefaultPolicy.ServeResponseAsync applies three storage safeguards (see dotnet/aspnetcore
// DefaultPolicy.cs): it blocks storage when the response carries a Set-Cookie header, when the user
// is authenticated, or when the status code is not 200. This policy only widens the last safeguard
// to also admit 503 — the Set-Cookie and authenticated-response safeguards are re-checked here so a
// degraded 503 that also sets a cookie (or belongs to an authenticated caller) is never stored and
// replayed from cache to other clients.
internal sealed class HealthCheckOutputCachePolicy : IOutputCachePolicy
{
    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;

        if (response.StatusCode == StatusCodes.Status503ServiceUnavailable
            && StringValues.IsNullOrEmpty(response.Headers.SetCookie)
            && context.HttpContext.User?.Identity?.IsAuthenticated != true)
        {
            context.AllowCacheStorage = true;
        }

        return ValueTask.CompletedTask;
    }
}
