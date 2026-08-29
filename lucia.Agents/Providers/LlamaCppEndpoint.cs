namespace lucia.Agents.Providers;

public static class LlamaCppEndpoint
{
    public static Uri Normalize(string? endpoint)
    {
        if (!TryNormalize(endpoint, out var normalizedEndpoint)
            || normalizedEndpoint is null)
            throw new InvalidOperationException("llama.cpp provider requires a valid HTTP or HTTPS endpoint URL");

        return normalizedEndpoint;
    }

    public static bool TryNormalize(string? endpoint, out Uri? normalizedEndpoint)
    {
        normalizedEndpoint = null;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var builder = new UriBuilder(uri);
        var path = builder.Path.TrimEnd('/');
        if (!path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            builder.Path = $"{path}/v1";

        normalizedEndpoint = builder.Uri;
        return true;
    }
}
