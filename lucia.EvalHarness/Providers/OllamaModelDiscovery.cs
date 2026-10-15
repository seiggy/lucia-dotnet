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
