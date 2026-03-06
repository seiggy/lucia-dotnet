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

public sealed class BraveSearchWebSearchSkill : IWebSearchSkill
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.WebSearch.BraveSearch", "1.0.0");
    private static readonly Meter Meter = new("Lucia.Skills.WebSearch.BraveSearch", "1.0.0");
    private static readonly Counter<long> SearchRequests = Meter.CreateCounter<long>("websearch.brave.requests", "{count}", "Number of Brave web search requests.");
    private static readonly Counter<long> SearchFailures = Meter.CreateCounter<long>("websearch.brave.failures", "{count}", "Number of failed Brave web searches.");
    private static readonly Histogram<double> SearchDurationMs = Meter.CreateHistogram<double>("websearch.brave.duration", "ms", "Duration of Brave web search operations.");

    public BraveSearchWebSearchSkill(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public IList<AITool> GetTools() => [AIFunctionFactory.Create(WebSearchAsync, new AIFunctionFactoryOptions { Name = "web_search" })];

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Brave Search WebSearchSkill initialized.");
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
            var searchUrl = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count=8";
            var client = _httpClientFactory.CreateClient("BraveSearch");
            var response = await client.GetAsync(searchUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<BraveSearchResponse>(json);
            var webResults = result?.Web?.Results;

            if (webResults is null || webResults.Count == 0)
            {
                RecordDuration(start);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return "No results found.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {webResults.Count} result(s) for \"{query}\":");
            foreach (var r in webResults.Take(8))
            {
                sb.AppendLine();
                sb.AppendLine($"**{r.Title}**");
                sb.AppendLine(r.Url);
                if (!string.IsNullOrWhiteSpace(r.Description))
                    sb.AppendLine(r.Description);
            }

            var ms = RecordDuration(start);
            activity?.SetStatus(ActivityStatusCode.Ok);
            _logger.LogDebug("Brave search completed in {Ms}ms, {Count} results.", ms, webResults.Count);
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            SearchFailures.Add(1);
            _logger.LogWarning(ex, "Brave search failed for query: {Query}.", query);
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

public sealed class BraveSearchResponse
{
    [JsonPropertyName("web")]
    public BraveWebResults? Web { get; set; }
}

public sealed class BraveWebResults
{
    [JsonPropertyName("results")]
    public List<BraveWebResult> Results { get; set; } = [];
}

public sealed class BraveWebResult
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class BraveSearchPlugin : ILuciaPlugin
{
    public string PluginId => "brave-search";

    public string? ConfigSection => "BraveSearch";
    public string? ConfigDescription => "Brave Search API connection settings";
    public IReadOnlyList<PluginConfigProperty> ConfigProperties =>
    [
        new("ApiKey", "string", "Brave Search API subscription token (from https://brave.com/search/api/)", "", IsSensitive: true),
    ];

    public void ConfigureServices(IHostApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var apiKey = (config["BraveSearch:ApiKey"] ?? config["BRAVE_SEARCH_API_KEY"] ?? "").Trim();

        if (string.IsNullOrEmpty(apiKey))
            return;

        builder.Services.AddHttpClient("BraveSearch", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("X-Subscription-Token", apiKey);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        builder.Services.AddSingleton<IWebSearchSkill>(sp =>
            new BraveSearchWebSearchSkill(
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<BraveSearchWebSearchSkill>()));
    }

    public Task ExecuteAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("BraveSearchPlugin");
        var skill = services.GetService<IWebSearchSkill>();
        if (skill is BraveSearchWebSearchSkill)
            logger.LogInformation("Brave Search plugin active — web search tool registered.");
        else
            logger.LogInformation("Brave Search plugin loaded but API key not configured — no search tool registered.");
        return Task.CompletedTask;
    }
}

new BraveSearchPlugin()
