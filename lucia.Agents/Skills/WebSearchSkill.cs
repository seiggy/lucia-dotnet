using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using lucia.Agents.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Skills;

/// <summary>
/// Skill for web search via SearXNG. Exposes a web_search tool when SEARXNG_URL is configured.
/// Aligns with Open Web UI's SearXNG integration for privacy-focused meta-search.
/// </summary>
public sealed class WebSearchSkill : IAgentSkill
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<SearXngOptions> _options;
    private readonly ILogger<WebSearchSkill> _logger;

    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.WebSearch", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Skills.WebSearch", "1.0.0");
    private static readonly Counter<long> SearchRequests = Meter.CreateCounter<long>("websearch.requests", "{count}", "Number of web search requests.");
    private static readonly Counter<long> SearchFailures = Meter.CreateCounter<long>("websearch.failures", "{count}", "Number of failed web searches.");
    private static readonly Histogram<double> SearchDurationMs = Meter.CreateHistogram<double>("websearch.duration", "ms", "Duration of web search operations.");

    public WebSearchSkill(
        IHttpClientFactory httpClientFactory,
        IOptions<SearXngOptions> options,
        ILogger<WebSearchSkill> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public IList<AITool> GetTools()
    {
        var baseUrl = (_options.Value?.BaseUrl ?? "").Trim();
        if (string.IsNullOrEmpty(baseUrl))
            return [];

        return [AIFunctionFactory.Create(WebSearchAsync)];
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var baseUrl = (_options.Value?.BaseUrl ?? "").Trim();
        if (!string.IsNullOrEmpty(baseUrl))
            _logger.LogInformation("WebSearchSkill initialized with SearXNG at {BaseUrl}", baseUrl);
        else
            _logger.LogDebug("WebSearchSkill: SEARXNG_URL not set — web search tool disabled.");
        return Task.CompletedTask;
    }

    [Description("Search the web for current information. Use when the user asks about recent events, news, or facts that may have changed. Returns title, URL, and snippet for each result.")]
    public async Task<string> WebSearchAsync(
        [Description("The search query (e.g. 'latest news about renewable energy', 'weather in Paris today')")] string query,
        CancellationToken cancellationToken = default)
    {
        SearchRequests.Add(1);
        var start = Stopwatch.GetTimestamp();
        using var activity = ActivitySource.StartActivity();
        activity?.SetTag("search.query", query);

        var baseUrl = (_options.Value?.BaseUrl ?? "").Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
        {
            SearchFailures.Add(1);
            return "Web search is not configured. Set SEARXNG_URL to enable search.";
        }

        try
        {
            var searchUrl = $"{baseUrl}/search?q={Uri.EscapeDataString(query)}&format=json";
            var client = _httpClientFactory.CreateClient("SearXng");
            var response = await client.GetAsync(searchUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<SearXngResponse>(json);
            if (result?.Results is null || result.Results.Count == 0)
            {
                var durationMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                SearchDurationMs.Record(durationMs);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return "No results found.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {result.Results.Count} result(s) for \"{query}\":");
            foreach (var r in result.Results.Take(8))
            {
                sb.AppendLine();
                sb.AppendLine($"**{r.Title}**");
                sb.AppendLine(r.Url);
                if (!string.IsNullOrWhiteSpace(r.Content))
                    sb.AppendLine(r.Content);
            }

            var durMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            SearchDurationMs.Record(durMs);
            activity?.SetStatus(ActivityStatusCode.Ok);
            _logger.LogDebug("Web search completed in {Ms}ms, {Count} results", durMs, result.Results.Count);
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            SearchFailures.Add(1);
            _logger.LogWarning(ex, "Web search failed for query: {Query}", query);
            return $"Web search failed: {ex.Message}";
        }
    }

    private sealed class SearXngResponse
    {
        [JsonPropertyName("results")]
        public List<SearXngResult> Results { get; set; } = [];
    }

    private sealed class SearXngResult
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
