using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using lucia.Agents.Providers;
using lucia.EvalHarness.Configuration;

namespace lucia.EvalHarness.Providers;

public sealed class BackendModelDiscovery(HttpClient httpClient, TokenCredential? credential = null)
{
    private readonly TokenCredential _credential = credential ?? new AzureCliCredential();

    public async Task<IReadOnlyList<DiscoveredModel>> ListModelsAsync(
        InferenceBackend backend, CancellationToken ct = default)
    {
        if (backend.Type == InferenceBackendType.AzureFoundry)
        {
            return await ListFoundryDeploymentsAsync(backend, ct);
        }

        var url = backend.Type switch
        {
            InferenceBackendType.Ollama => backend.Endpoint.TrimEnd('/') + "/api/tags",
            InferenceBackendType.OpenAICompat => LlamaCppEndpoint.Normalize(backend.Endpoint).AbsoluteUri.TrimEnd('/') + "/models",
            InferenceBackendType.OpenRouter => LlamaCppEndpoint.Normalize(backend.Endpoint).AbsoluteUri.TrimEnd('/') + "/models",
            _ => throw new InvalidOperationException($"Unsupported backend type: {backend.Type}")
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(backend.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", backend.ApiKey);
        }

        var root = await GetJsonAsync(request, ct);
        var property = backend.Type == InferenceBackendType.Ollama ? "models" : "data";
        var models = ReadArray(root, property);
        var result = new List<DiscoveredModel>();
        foreach (var model in models.EnumerateArray())
        {
            if (backend.Type == InferenceBackendType.Ollama)
            {
                var details = model.TryGetProperty("details", out var nested) ? nested : model;
                result.Add(new DiscoveredModel
                {
                    Name = ReadName(model, "name"),
                    Size = model.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
                    ParameterSize = details.TryGetProperty("parameter_size", out var parameters) ? parameters.GetString() : null,
                    QuantizationLevel = details.TryGetProperty("quantization_level", out var quantization) ? quantization.GetString() : null
                });
            }
            else if (!IsEmbeddingModel(model) &&
                     (backend.Type != InferenceBackendType.OpenRouter || HasTextOutput(model)))
            {
                result.Add(new DiscoveredModel
                {
                    Name = ReadName(model, "id"),
                    Pricing = backend.Type == InferenceBackendType.OpenRouter &&
                              model.TryGetProperty("pricing", out var pricing)
                        ? ReadPricing(pricing)
                        : null
                });
            }
        }

        return result;
    }

    public async Task<bool> IsAvailableAsync(InferenceBackend backend, CancellationToken ct = default)
    {
        try
        {
            await ListModelsAsync(backend, ct);
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or AuthenticationFailedException ||
                                          exception is OperationCanceledException && !ct.IsCancellationRequested)
        {
            Console.Error.WriteLine($"Backend '{backend.Name}' discovery failed: {exception.Message}");
            return false;
        }
    }

    private async Task<IReadOnlyList<DiscoveredModel>> ListFoundryDeploymentsAsync(
        InferenceBackend backend, CancellationToken ct)
    {
        var parts = backend.AzureResourceId?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts is not { Length: 8 } ||
            !parts[0].Equals("subscriptions", StringComparison.OrdinalIgnoreCase) ||
            !parts[2].Equals("resourceGroups", StringComparison.OrdinalIgnoreCase) ||
            !parts[4].Equals("providers", StringComparison.OrdinalIgnoreCase) ||
            !parts[5].Equals("Microsoft.CognitiveServices", StringComparison.OrdinalIgnoreCase) ||
            !parts[6].Equals("accounts", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Backend '{backend.Name}' requires an AzureResourceId for a Foundry account.");
        }

        var resourcePath = "/" + string.Join("/", parts.Select(Uri.EscapeDataString));
        Uri? next = new($"https://management.azure.com{resourcePath}/deployments?api-version=2024-10-01");
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(["https://management.azure.com/.default"]), ct);
        var visited = new HashSet<Uri>();
        var result = new List<DiscoveredModel>();

        while (next is not null)
        {
            if (next.Scheme != Uri.UriSchemeHttps || next.Host != "management.azure.com" ||
                !next.IsDefaultPort || !string.IsNullOrEmpty(next.UserInfo) ||
                !next.AbsolutePath.StartsWith(resourcePath + "/deployments", StringComparison.OrdinalIgnoreCase) ||
                !visited.Add(next))
            {
                throw new JsonException("Foundry returned an invalid deployment continuation URL.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, next);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            var root = await GetJsonAsync(request, ct);
            foreach (var deployment in ReadArray(root, "value").EnumerateArray())
            {
                var properties = deployment.GetProperty("properties");
                if (properties.GetProperty("provisioningState").GetString() == "Succeeded" &&
                    properties.TryGetProperty("capabilities", out var capabilities) &&
                    capabilities.TryGetProperty("responses", out var responses) &&
                    responses.ToString().Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new DiscoveredModel { Name = ReadName(deployment, "name") });
                }
            }

            next = root.TryGetProperty("nextLink", out var nextLink) &&
                   nextLink.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(nextLink.GetString())
                ? new Uri(nextLink.GetString()!, UriKind.Absolute)
                : null;
        }

        return result;
    }

    private async Task<JsonElement> GetJsonAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    }

    private static JsonElement ReadArray(JsonElement root, string name) =>
        root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array
            : throw new JsonException($"Model discovery response must contain a '{name}' array.");

    private static string ReadName(JsonElement model, string property) =>
        model.TryGetProperty(property, out var name) && name.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(name.GetString())
            ? name.GetString()!
            : throw new JsonException($"Discovered model is missing '{property}'.");

    private static bool IsEmbeddingModel(JsonElement model) =>
        model.TryGetProperty("status", out var status) &&
        status.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array &&
        args.EnumerateArray().Any(arg => arg.GetString() is "--embeddings" or "--embedding" or "--embd");

    private static bool HasTextOutput(JsonElement model) =>
        model.TryGetProperty("architecture", out var architecture) &&
        architecture.TryGetProperty("output_modalities", out var outputs) &&
        outputs.ValueKind == JsonValueKind.Array &&
        outputs.EnumerateArray().Any(output => output.GetString() == "text");

    private static ModelPricing ReadPricing(JsonElement pricing)
    {
        var tiers = new List<ModelPricing>();
        if (pricing.TryGetProperty("overrides", out var overrides))
        {
            foreach (var tier in overrides.EnumerateArray())
            {
                // Time-dependent prices need the provider's actual bill, not a base-rate guess.
                if (tier.TryGetProperty("utc_start", out _) || tier.TryGetProperty("utc_end", out _) ||
                    tier.TryGetProperty("utc_days", out _))
                {
                    return new ModelPricing();
                }

                tiers.Add(ReadRates(tier) with
                {
                    MinimumInputTokens = checked(tier.GetProperty("min_prompt_tokens").GetInt64() + 1)
                });
            }
        }

        return ReadRates(pricing) with { ContextTiers = tiers };
    }

    private static ModelPricing ReadRates(JsonElement pricing) => new()
    {
        InputUsdPerMillionTokens = ReadPrice(pricing, "prompt") * 1_000_000m,
        OutputUsdPerMillionTokens = ReadPrice(pricing, "completion") * 1_000_000m,
        CachedInputUsdPerMillionTokens = ReadPrice(pricing, "input_cache_read") * 1_000_000m,
        RequestUsd = ReadPrice(pricing, "request") ?? 0m
    };

    private static decimal? ReadPrice(JsonElement pricing, string property)
    {
        if (!pricing.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (!decimal.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var price))
        {
            throw new JsonException($"OpenRouter returned an invalid '{property}' price.");
        }

        return price >= 0 ? price : null;
    }
}
