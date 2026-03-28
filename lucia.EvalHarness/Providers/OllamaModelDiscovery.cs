using System.Net.Http.Json;
using System.Text.Json.Serialization;
using lucia.EvalHarness.Configuration;

namespace lucia.EvalHarness.Providers;

/// <summary>
/// Discovers available models from an Ollama instance via the <c>/api/tags</c> endpoint.
/// </summary>
public sealed class OllamaModelDiscovery
{
    private readonly HttpClient _httpClient;
    private readonly OllamaSettings _settings;

    public OllamaModelDiscovery(HttpClient httpClient, OllamaSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    /// <summary>
    /// Lists all models available on the configured Ollama instance.
    /// </summary>
    public async Task<IReadOnlyList<OllamaModelInfo>> ListModelsAsync(CancellationToken ct = default)
    {
        var url = _settings.Endpoint.TrimEnd('/') + "/api/tags";
        var response = await _httpClient.GetFromJsonAsync<OllamaTagsResponse>(url, ct);
        return response?.Models ?? [];
    }

    /// <summary>
    /// Lists currently running models with GPU/memory info via <c>/api/ps</c>.
    /// </summary>
    public async Task<IReadOnlyList<OllamaProcessInfo>> ListRunningModelsAsync(CancellationToken ct = default)
    {
        var url = _settings.Endpoint.TrimEnd('/') + "/api/ps";
        try
        {
            var response = await _httpClient.GetFromJsonAsync<OllamaProcessResponse>(url, ct);
            return response?.Models ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    /// <summary>
    /// Tests connectivity to the Ollama endpoint.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(_settings.Endpoint.TrimEnd('/') + "/api/tags", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaModelInfo> Models { get; set; } = [];
}

public sealed class OllamaModelInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("parameter_size")]
    public string? ParameterSize { get; set; }

    [JsonPropertyName("quantization_level")]
    public string? QuantizationLevel { get; set; }

    [JsonPropertyName("modified_at")]
    public DateTimeOffset ModifiedAt { get; set; }

    /// <summary>
    /// Formatted display string (e.g., "llama3.2:latest (3.2B Q4_0, 2.0 GB)").
    /// </summary>
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

public sealed class OllamaProcessResponse
{
    [JsonPropertyName("models")]
    public List<OllamaProcessInfo> Models { get; set; } = [];
}

public sealed class OllamaProcessInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("size_vram")]
    public long SizeVram { get; set; }
}
