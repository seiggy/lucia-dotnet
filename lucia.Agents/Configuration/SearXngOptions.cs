namespace lucia.Agents.Configuration;

/// <summary>
/// Configuration for SearXNG web search integration.
/// Binds from SearXng:BaseUrl or SEARXNG_URL (env var).
/// </summary>
public sealed class SearXngOptions
{
    public const string SectionName = "SearXng";

    /// <summary>
    /// Base URL of the SearXNG instance (e.g. http://host.docker.internal:8081).
    /// When set, the WebSearchSkill exposes a web_search tool to agents.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
}
