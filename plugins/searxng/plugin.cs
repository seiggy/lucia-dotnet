using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using lucia.Agents.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class SearXngWebSearchSkill : IWebSearchSkill
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly ILogger _logger;

    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.WebSearch.SearXng", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Skills.WebSearch.SearXng", "1.0.0");
    private static readonly Counter<long> SearchRequests = Meter.CreateCounter<long>("websearch.requests", "{count}", "Number of web search requests.");
    private static readonly Counter<long> SearchFailures = Meter.CreateCounter<long>("websearch.failures", "{count}", "Number of failed web searches.");
    private static readonly Histogram<double> SearchDurationMs = Meter.CreateHistogram<double>("websearch.duration", "ms", "Duration of web search operations.");

    public SearXngWebSearchSkill(IHttpClientFactory httpClientFactory, string baseUrl, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrl = baseUrl.TrimEnd('/');
        _logger = logger;
    }

    public IList<AITool> GetTools() => [AIFunctionFactory.Create(WebSearchAsync, new AIFunctionFactoryOptions { Name = "web_search" })];

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SearXNG WebSearchSkill initialized at {BaseUrl}.", _baseUrl);
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

        try
        {
            var searchUrl = $"{_baseUrl}/search?q={Uri.EscapeDataString(query)}&format=json";
            var client = _httpClientFactory.CreateClient("SearXng");
            var response = await client.GetAsync(searchUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<SearXngResponse>(json);
            if (result?.Results is null || result.Results.Count == 0)
            {
                RecordDuration(start);
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

            var ms = RecordDuration(start);
            activity?.SetStatus(ActivityStatusCode.Ok);
            _logger.LogDebug("SearXNG search completed in {Ms}ms, {Count} results.", ms, result.Results.Count);
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            SearchFailures.Add(1);
            _logger.LogWarning(ex, "SearXNG search failed for query: {Query}.", query);
            return $"Web search failed: {ex.Message}";
        }
    }

    private static double RecordDuration(long start)
    {
        var ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        SearchDurationMs.Record(ms);
        return ms;
    }
}

public sealed class SearXngResponse
{
    [JsonPropertyName("results")]
    public List<SearXngResult> Results { get; set; } = [];
}

public sealed class SearXngResult
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

public class SearXngPlugin : ILuciaPlugin
{
    public string PluginId => "searxng";

    public string? ConfigSection => "SearXng";
    public string? ConfigDescription => "SearXNG web search connection settings";
    public IReadOnlyList<PluginConfigProperty> ConfigProperties =>
    [
        new("BaseUrl", "string", "SearXNG instance base URL (e.g. http://localhost:8888)", ""),
    ];

    public void ConfigureServices(IHostApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var baseUrl = (config["SearXng:BaseUrl"] ?? config["SEARXNG_URL"] ?? "").Trim();

        if (string.IsNullOrEmpty(baseUrl))
            return;

        builder.Services.AddHttpClient("SearXng", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        builder.Services.AddSingleton<IWebSearchSkill>(sp =>
            new SearXngWebSearchSkill(
                sp.GetRequiredService<IHttpClientFactory>(),
                baseUrl,
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<SearXngWebSearchSkill>()));
    }

    public Task ExecuteAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("SearXngPlugin");
        var skill = services.GetService<IWebSearchSkill>();
        if (skill is not null)
            logger.LogInformation("SearXNG plugin active — web search tool registered.");
        else
            logger.LogInformation("SearXNG plugin loaded but SEARXNG_URL not configured — no search tool registered.");
        return Task.CompletedTask;
    }
}

new SearXngPlugin()
