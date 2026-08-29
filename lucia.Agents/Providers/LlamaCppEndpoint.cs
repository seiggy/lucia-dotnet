namespace lucia.Agents.Providers;

public static class LlamaCppEndpoint
{
    public static Uri Normalize(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("llama.cpp provider requires an endpoint URL");

        var builder = new UriBuilder(endpoint);
        var path = builder.Path.TrimEnd('/');
        if (!path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            builder.Path = $"{path}/v1";

        return builder.Uri;
    }
}
