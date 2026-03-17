using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Models;

public sealed class HuggingFaceClient(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<HuggingFaceOptions> options,
    ILogger<HuggingFaceClient> logger)
{
    private const string BaseUrl = "https://huggingface.co";
    private const string OnnxCommunityAuthor = "onnx-community";

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<List<HuggingFaceModelInfo>> SearchModelsAsync(
        EngineType engineType,
        CancellationToken ct)
    {
        var pipelineTag = MapPipelineTag(engineType);
        if (pipelineTag is null)
        {
            return [];
        }

        try
        {
            using var client = CreateAuthenticatedClient();
            var url = $"{BaseUrl}/api/models?pipeline_tag={pipelineTag}&author={OnnxCommunityAuthor}&sort=downloads&direction=-1&limit=100";

            using var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<List<HuggingFaceModelInfo>>(json, s_jsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to search HuggingFace models for pipeline tag {PipelineTag}", pipelineTag);
            return [];
        }
    }

    public async Task<HuggingFaceModelInfo?> GetModelInfoAsync(
        string repoId,
        CancellationToken ct)
    {
        try
        {
            using var client = CreateAuthenticatedClient();
            var url = $"{BaseUrl}/api/models/{repoId}";

            using var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<HuggingFaceModelInfo>(json, s_jsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get HuggingFace model info for {RepoId}", repoId);
            return null;
        }
    }

    public async Task<bool> IsAuthenticatedAsync(CancellationToken ct)
    {
        try
        {
            using var client = CreateAuthenticatedClient();
            using var response = await client.GetAsync($"{BaseUrl}/api/whoami-v2", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to verify HuggingFace authentication");
            return false;
        }
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = httpClientFactory.CreateClient();
        var token = options.CurrentValue.ApiToken;

        if (!string.IsNullOrWhiteSpace(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static string? MapPipelineTag(EngineType engineType) =>
        engineType switch
        {
            EngineType.Stt => "automatic-speech-recognition",
            EngineType.OfflineStt => "automatic-speech-recognition",
            _ => null
        };
}
