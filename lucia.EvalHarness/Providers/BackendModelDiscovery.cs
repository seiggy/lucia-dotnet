using System.Net.Http.Json;
using System.Text.Json.Serialization;
using lucia.EvalHarness.Configuration;

namespace lucia.EvalHarness.Providers;

/// <summary>
/// Discovers available models from any supported inference backend.
/// Ollama backends use <c>/api/tags</c>; OpenAI-compatible backends use <c>/v1/models</c>.
/// </summary>
public sealed class BackendModelDiscovery
{
    private readonly HttpClient _httpClient;

    public BackendModelDiscovery(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Lists models available on the specified backend.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredModel>> ListModelsAsync(
        InferenceBackend backend, CancellationToken ct = default)
    {
        return backend.Type switch
        {
            InferenceBackendType.Ollama => await ListOllamaModelsAsync(backend.Endpoint, ct),
            InferenceBackendType.OpenAICompat => await ListOpenAICompatModelsAsync(backend.Endpoint, ct),
            _ => []
        };
    }

    /// <summary>
    /// Tests connectivity to the specified backend.
    /// </summary>
    public async Task<bool> IsAvailableAsync(InferenceBackend backend, CancellationToken ct = default)
    {
        try
        {
            var url = backend.Type switch
            {
                InferenceBackendType.Ollama => backend.Endpoint.TrimEnd('/') + "/api/tags",
                InferenceBackendType.OpenAICompat => backend.Endpoint.TrimEnd('/') + "/v1/models",
                _ => backend.Endpoint
            };

            var response = await _httpClient.GetAsync(url, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<DiscoveredModel>> ListOllamaModelsAsync(
        string endpoint, CancellationToken ct)
    {
        var url = endpoint.TrimEnd('/') + "/api/tags";
        var response = await _httpClient.GetFromJsonAsync<OllamaTagsResponse>(url, ct);

        return response?.Models?.Select(m => new DiscoveredModel
        {
            Name = m.Name,
            Size = m.Size,
            ParameterSize = m.ParameterSize,
            QuantizationLevel = m.QuantizationLevel
        }).ToList() ?? [];
    }

    private async Task<IReadOnlyList<DiscoveredModel>> ListOpenAICompatModelsAsync(
        string endpoint, CancellationToken ct)
    {
        try
        {
            var url = endpoint.TrimEnd('/') + "/v1/models";
            var response = await _httpClient.GetFromJsonAsync<OpenAIModelsResponse>(url, ct);

            return response?.Data?.Select(m => new DiscoveredModel
            {
                Name = m.Id
            }).ToList() ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }
}

/// <summary>
/// A model discovered from any inference backend, normalized to a common shape.
/// </summary>
public sealed class DiscoveredModel
{
    public string Name { get; init; } = string.Empty;
    public long Size { get; init; }
    public string? ParameterSize { get; init; }
    public string? QuantizationLevel { get; init; }

    public string DisplayName
    {
        get
        {
            var parts = new List<string> { Name };
            var meta = new List<string>();
            if (!string.IsNullOrEmpty(ParameterSize)) meta.Add(ParameterSize);
            if (!string.IsNullOrEmpty(QuantizationLevel)) meta.Add(QuantizationLevel);
            if (Size > 0) meta.Add($"{Size / (1024.0 * 1024 * 1024):F1} GB");
            if (meta.Count > 0) parts.Add($"({string.Join(", ", meta)})");
            return string.Join(" ", parts);
        }
    }
}

/// <summary>
/// Response shape from the OpenAI-compatible <c>/v1/models</c> endpoint.
/// </summary>
internal sealed class OpenAIModelsResponse
{
    [JsonPropertyName("data")]
    public List<OpenAIModelEntry>? Data { get; set; }
}

/// <summary>
/// A single model entry from the OpenAI-compatible models list.
/// </summary>
internal sealed class OpenAIModelEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}
