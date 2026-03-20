using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
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

    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.WebSearch.BraveSearch", "1.1.0");
    private static readonly Meter Meter = new("Lucia.Skills.WebSearch.BraveSearch", "1.1.0");
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

    [Description("Search the web for current information using the Brave LLM Context API. Returns pre-extracted, relevance-scored web content optimized for LLM consumption including text chunks, tables, and code blocks from source pages.")]
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
            var searchUrl = $"https://api.search.brave.com/res/v1/llm/context?q={Uri.EscapeDataString(query)}&count=8&maximum_number_of_tokens=8192";
            var client = _httpClientFactory.CreateClient("BraveSearch");
            var response = await client.GetAsync(searchUrl, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning(
                    "Brave LLM Context API returned {StatusCode} for query '{Query}'. Response: {Body}",
                    (int)response.StatusCode, query, errorBody);
                return $"Web search failed: HTTP {(int)response.StatusCode}. Check your Brave Search API key and subscription.";
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<BraveLlmContextResponse>(json);
            var groundingItems = result?.Grounding?.Generic;

            if (groundingItems is null || groundingItems.Count == 0)
            {
                RecordDuration(start);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return "No results found.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found context from {groundingItems.Count} source(s) for \"{query}\":");
            foreach (var item in groundingItems.Take(8))
            {
                sb.AppendLine();
                sb.AppendLine($"**{item.Title}**");
                sb.AppendLine(item.Url);
                foreach (var snippet in item.Snippets ?? [])
                {
                    sb.AppendLine(snippet);
                }
            }

            var ms = RecordDuration(start);
            activity?.SetStatus(ActivityStatusCode.Ok);
            _logger.LogDebug("Brave LLM Context search completed in {Ms}ms, {Count} sources.", ms, groundingItems.Count);
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            SearchFailures.Add(1);
            _logger.LogWarning(ex, "Brave LLM Context search failed for query: {Query}.", query);
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

public sealed class BraveLlmContextResponse
{
    [JsonPropertyName("grounding")]
    public BraveLlmGrounding? Grounding { get; set; }

    [JsonPropertyName("sources")]
    public Dictionary<string, BraveLlmSource>? Sources { get; set; }
}

public sealed class BraveLlmGrounding
{
    [JsonPropertyName("generic")]
    public List<BraveLlmGroundingItem> Generic { get; set; } = [];
}

public sealed class BraveLlmGroundingItem
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("snippets")]
    public List<string> Snippets { get; set; } = [];
}

public sealed class BraveLlmSource
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = "";

    [JsonPropertyName("age")]
    public List<string>? Age { get; set; }
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
