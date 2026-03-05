using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using lucia.AgentHost.Models;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;

namespace lucia.AgentHost.Services;

/// <summary>
/// Enumerates available models for configured providers.
/// </summary>
public sealed class ProviderModelCatalogService
{
    private const string DefaultOllamaEndpoint = "http://localhost:11434";
    private const string DefaultOpenAiEndpoint = "https://api.openai.com/v1/";
    private const string DefaultOpenRouterEndpoint = "https://openrouter.ai/api/v1";
    private const string DefaultGeminiEndpoint = "https://generativelanguage.googleapis.com/v1beta/openai/";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ProviderModelCatalogService> _logger;

    public ProviderModelCatalogService(
        IHttpClientFactory httpClientFactory,
        ILogger<ProviderModelCatalogService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ProviderModelsResponse> ListModelsAsync(ModelProvider provider, CancellationToken ct = default)
    {
        return await ListModelsAsync(provider.ProviderType, provider.Endpoint, provider.Auth, ct).ConfigureAwait(false);
    }

    public async Task<ProviderModelsResponse> ListModelsAsync(
        ProviderType providerType,
        string? endpoint,
        ModelAuthConfig? auth,
        CancellationToken ct = default)
    {
        return providerType switch
        {
            ProviderType.Ollama => await ListOllamaModelsAsync(endpoint, ct).ConfigureAwait(false),
            ProviderType.OpenAI => await ListOpenAiCompatibleModelsAsync(endpoint, auth?.ApiKey, DefaultOpenAiEndpoint, ct).ConfigureAwait(false),
            ProviderType.OpenRouter => await ListOpenRouterModelsAsync(endpoint, auth?.ApiKey, ct).ConfigureAwait(false),
            ProviderType.GoogleGemini => await ListOpenAiCompatibleModelsAsync(endpoint, auth?.ApiKey, DefaultGeminiEndpoint, ct).ConfigureAwait(false),
            _ => new ProviderModelsResponse
            {
                Error = $"Model enumeration is not supported for provider type '{providerType}'."
            }
        };
    }

    public async Task<ProviderModelsResponse> ListOllamaModelsAsync(string? endpoint, CancellationToken ct = default)
    {
        var result = new ProviderModelsResponse();
        var baseUrl = string.IsNullOrWhiteSpace(endpoint) ? DefaultOllamaEndpoint : endpoint.Trim();

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            result.Error = "Invalid Ollama endpoint URL";
            return result;
        }

        var url = uri.ToString().TrimEnd('/') + "/api/tags";

        try
        {
            var client = _httpClientFactory.CreateClient("OllamaModels");
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var parsed = JsonSerializer.Deserialize<OllamaTagsResponse>(json);
            result.Models = (parsed?.Models ?? [])
                .Select(m => m.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n)
                .ToList();
        }
        catch (HttpRequestException ex)
        {
            result.Error = $"Cannot reach Ollama at {baseUrl}: {ex.Message}";
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            result.Error = $"Timed out while connecting to {baseUrl}: {ex.Message}";
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON response while listing Ollama models from {Endpoint}", baseUrl);
            result.Error = $"Received an invalid response from {baseUrl}.";
        }

        return result;
    }

    private async Task<ProviderModelsResponse> ListOpenRouterModelsAsync(
        string? endpoint,
        string? apiKey,
        CancellationToken ct)
    {
        var result = new ProviderModelsResponse();
        var baseUrl = string.IsNullOrWhiteSpace(endpoint) ? DefaultOpenRouterEndpoint : endpoint.Trim();

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            result.Error = "Invalid OpenRouter endpoint URL";
            return result;
        }

        var modelsUri = BuildOpenRouterModelsEndpoint(uri);

        try
        {
            var client = _httpClientFactory.CreateClient("ProviderModelCatalog");
            using var request = new HttpRequestMessage(HttpMethod.Get, modelsUri);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                result.Error = $"OpenRouter authentication failed for {modelsUri}. Verify the API key.";
                return result;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                result.Error = $"OpenRouter endpoint {modelsUri} is invalid. Verify the endpoint URL.";
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                result.Error = $"OpenRouter model discovery failed at {modelsUri}: {(int)response.StatusCode} {response.ReasonPhrase}.";
                return result;
            }

            using var payload = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(payload, cancellationToken: ct).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
            {
                result.Error = $"OpenRouter model catalog response from {modelsUri} did not include a 'data' array.";
                return result;
            }

            result.Models = dataElement.EnumerateArray()
                .Where(IsOpenRouterToolCapable)
                .Select(item => item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList()!;
        }
        catch (HttpRequestException ex)
        {
            result.Error = $"Cannot reach OpenRouter endpoint at {modelsUri}: {ex.Message}";
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            result.Error = $"Timed out while connecting to {modelsUri}: {ex.Message}";
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON response while listing OpenRouter models from {Endpoint}", modelsUri);
            result.Error = $"Received an invalid OpenRouter response from {modelsUri}.";
        }

        return result;
    }

    private async Task<ProviderModelsResponse> ListOpenAiCompatibleModelsAsync(
        string? endpoint,
        string? apiKey,
        string defaultEndpoint,
        CancellationToken ct)
    {
        var baseUrl = string.IsNullOrWhiteSpace(endpoint) ? defaultEndpoint : endpoint.Trim();
        var result = new ProviderModelsResponse();

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            result.Error = "Invalid provider endpoint URL";
            return result;
        }

        var modelsUri = BuildModelsEndpoint(uri);

        try
        {
            var client = _httpClientFactory.CreateClient("ProviderModelCatalog");
            using var request = new HttpRequestMessage(HttpMethod.Get, modelsUri);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                result.Error = $"Authentication failed for provider endpoint at {modelsUri}. Verify the API key or credentials.";
                return result;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                result.Error = $"Provider models endpoint not found at {modelsUri}. Verify the endpoint URL.";
                return result;
            }

            if (!response.IsSuccessStatusCode)
            {
                result.Error = $"Model discovery failed at {modelsUri}: {(int)response.StatusCode} {response.ReasonPhrase}.";
                return result;
            }

            using var payload = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(payload, cancellationToken: ct).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
            {
                result.Error = $"Model catalog response from {modelsUri} did not include a 'data' array.";
                return result;
            }

            result.Models = dataElement.EnumerateArray()
                .Select(item => item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList()!;
        }
        catch (HttpRequestException ex)
        {
            result.Error = $"Cannot reach provider endpoint at {modelsUri}: {ex.Message}";
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            result.Error = $"Timed out while connecting to {modelsUri}: {ex.Message}";
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON response while listing provider models from {Endpoint}", modelsUri);
            result.Error = $"Received an invalid response from {modelsUri}.";
        }

        return result;
    }

    private static Uri BuildModelsEndpoint(Uri providerEndpoint)
    {
        var normalizedBase = new Uri(providerEndpoint.ToString().TrimEnd('/') + "/", UriKind.Absolute);
        var trimmedPath = normalizedBase.AbsolutePath.TrimEnd('/');

        if (trimmedPath.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(normalizedBase.ToString().TrimEnd('/'), UriKind.Absolute);
        }

        return string.IsNullOrEmpty(trimmedPath) || trimmedPath == "/"
            ? new Uri(normalizedBase, "v1/models")
            : new Uri(normalizedBase, "models");
    }

    private static Uri BuildOpenRouterModelsEndpoint(Uri providerEndpoint)
    {
        var modelsEndpoint = BuildModelsEndpoint(providerEndpoint);
        var builder = new UriBuilder(modelsEndpoint);
        var query = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrWhiteSpace(query)
            ? "supported_parameters=tools"
            : $"{query}&supported_parameters=tools";
        return builder.Uri;
    }

    private static bool IsOpenRouterToolCapable(JsonElement modelElement)
    {
        if (!modelElement.TryGetProperty("supported_parameters", out var supportedParameters) ||
            supportedParameters.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var parameter in supportedParameters.EnumerateArray())
        {
            if (parameter.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var parameterName = parameter.GetString();
            if (string.Equals(parameterName, "tools", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parameterName, "tool_choice", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
