using System.Text.Json;
using System.Text.Json.Serialization;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.GitHubCopilot;
using lucia.Agents.GitHubCopilot.Models;
using lucia.Agents.Mcp;
using lucia.Agents.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// Request body for the Copilot CLI connect endpoint.
/// </summary>
public sealed record CopilotConnectRequest(string? GithubToken);

/// <summary>
/// Request body for listing Ollama models.
/// </summary>
public sealed record OllamaModelsRequest(string? Endpoint);

/// <summary>
/// Response from listing Ollama models. Models are populated on success; Error is set on failure.
/// </summary>
public sealed class OllamaModelsResponse
{
    public List<string> Models { get; set; } = [];
    public string? Error { get; set; }
}

/// <summary>
/// Response from GET /api/tags on an Ollama instance.
/// </summary>
internal sealed class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaModelInfo> Models { get; set; } = [];
}

internal sealed class OllamaModelInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

/// <summary>
/// CRUD endpoints for user-configured model providers.
/// </summary>
public static class ModelProviderApi
{
    public static IEndpointRouteBuilder MapModelProviderApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/model-providers")
            .WithTags("Model Providers")
            .RequireAuthorization();

        group.MapGet("/", ListProvidersAsync);
        group.MapGet("/{id}", GetProviderAsync);
        group.MapPost("/", CreateProviderAsync);
        group.MapPut("/{id}", UpdateProviderAsync);
        group.MapDelete("/{id}", DeleteProviderAsync);
        group.MapPost("/{id}/test", TestProviderAsync);
        group.MapPost("/{id}/test-embedding", TestEmbeddingAsync);
        group.MapPost("/copilot/connect", CopilotConnectAsync);
        group.MapPost("/ollama/models", ListOllamaModelsAsync);

        return endpoints;
    }

    private static async Task<Ok<List<ModelProvider>>> ListProvidersAsync(
        [FromServices] IModelProviderRepository repository,
        [FromQuery] ModelPurpose? purpose,
        CancellationToken ct)
    {
        var providers = await repository.GetAllProvidersAsync(ct);
        if (purpose is not null)
        {
            providers = providers.Where(p => p.Purpose == purpose.Value).ToList();
        }
        return TypedResults.Ok(providers);
    }

    private static async Task<Results<Ok<ModelProvider>, NotFound>> GetProviderAsync(
        string id,
        [FromServices] IModelProviderRepository repository,
        CancellationToken ct)
    {
        var provider = await repository.GetProviderAsync(id, ct);
        return provider is not null
            ? TypedResults.Ok(provider)
            : TypedResults.NotFound();
    }

    private static async Task<Results<Created<ModelProvider>, BadRequest<string>>> CreateProviderAsync(
        [FromBody] ModelProvider provider,
        [FromServices] IModelProviderRepository repository,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ModelProviderApi");

        if (string.IsNullOrWhiteSpace(provider.Id))
            return TypedResults.BadRequest("Provider id is required");

        if (string.IsNullOrWhiteSpace(provider.Name))
            return TypedResults.BadRequest("Provider name is required");

        var existing = await repository.GetProviderAsync(provider.Id, ct);
        if (existing is not null)
            return TypedResults.BadRequest($"Provider with id '{provider.Id}' already exists");

        provider.CreatedAt = DateTime.UtcNow;
        provider.UpdatedAt = DateTime.UtcNow;

        await repository.UpsertProviderAsync(provider, ct);
        logger.LogInformation("Created model provider {ProviderId}", provider.Id);

        return TypedResults.Created($"/api/model-providers/{provider.Id}", provider);
    }

    private static async Task<Results<Ok<ModelProvider>, NotFound>> UpdateProviderAsync(
        string id,
        [FromBody] ModelProvider provider,
        [FromServices] IModelProviderRepository repository,
        CancellationToken ct)
    {
        var existing = await repository.GetProviderAsync(id, ct);
        if (existing is null)
            return TypedResults.NotFound();

        provider.Id = id;
        provider.CreatedAt = existing.CreatedAt;
        provider.UpdatedAt = DateTime.UtcNow;

        await repository.UpsertProviderAsync(provider, ct);
        return TypedResults.Ok(provider);
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteProviderAsync(
        string id,
        [FromServices] IModelProviderRepository repository,
        CancellationToken ct)
    {
        var existing = await repository.GetProviderAsync(id, ct);
        if (existing is null)
            return TypedResults.NotFound();

        if (existing.IsBuiltIn)
            return TypedResults.Problem(
                "Built-in model providers cannot be deleted. You can edit their configuration instead.",
                statusCode: StatusCodes.Status400BadRequest);

        await repository.DeleteProviderAsync(id, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<ModelProviderTestResult>> TestProviderAsync(
        string id,
        [FromServices] IModelProviderRepository repository,
        [FromServices] IModelProviderResolver resolver,
        CancellationToken ct)
    {
        var provider = await repository.GetProviderAsync(id, ct);
        if (provider is null)
            return TypedResults.Ok(new ModelProviderTestResult(false, $"Provider '{id}' not found"));

        var result = await resolver.TestConnectionAsync(provider, ct);
        return TypedResults.Ok(result);
    }

    private static async Task<Ok<ModelProviderTestResult>> TestEmbeddingAsync(
        string id,
        [FromServices] IModelProviderRepository repository,
        [FromServices] IModelProviderResolver resolver,
        CancellationToken ct)
    {
        var provider = await repository.GetProviderAsync(id, ct);
        if (provider is null)
            return TypedResults.Ok(new ModelProviderTestResult(false, $"Provider '{id}' not found"));

        var result = await resolver.TestEmbeddingConnectionAsync(provider, ct);
        return TypedResults.Ok(result);
    }

    private static async Task<Ok<CopilotConnectResult>> CopilotConnectAsync(
        [FromBody] CopilotConnectRequest request,
        [FromServices] CopilotConnectService connectService,
        CancellationToken ct)
    {
        var result = await connectService.ConnectAndListModelsAsync(request.GithubToken, ct);
        return TypedResults.Ok(result);
    }

    private static async Task<Ok<OllamaModelsResponse>> ListOllamaModelsAsync(
        [FromBody] OllamaModelsRequest request,
        [FromServices] IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        var result = new OllamaModelsResponse();
        var baseUrl = (request?.Endpoint ?? "").Trim();
        if (string.IsNullOrEmpty(baseUrl))
            baseUrl = "http://localhost:11434";

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            result.Error = "Invalid Ollama endpoint URL";
            return TypedResults.Ok(result);
        }

        var url = uri.ToString().TrimEnd('/') + "/api/tags";
        try
        {
            var client = httpClientFactory.CreateClient("OllamaModels");
            var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var parsed = JsonSerializer.Deserialize<OllamaTagsResponse>(json);
            result.Models = (parsed?.Models ?? [])
                .Select(m => m.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n)
                .ToList();
        }
        catch (Exception ex)
        {
            result.Error = ex is HttpRequestException
                ? $"Cannot reach Ollama at {baseUrl}: {ex.Message}"
                : $"Failed to list models: {ex.Message}";
        }

        return TypedResults.Ok(result);
    }
}
