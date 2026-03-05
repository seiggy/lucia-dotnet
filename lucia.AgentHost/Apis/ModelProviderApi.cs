using lucia.AgentHost.Models;
using lucia.AgentHost.Services;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Configuration.UserConfiguration;
using lucia.Agents.GitHubCopilot;
using lucia.Agents.GitHubCopilot.Models;
using lucia.Agents.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Apis;

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
        group.MapPost("/{id}/models", ListProviderModelsAsync);
        group.MapPost("/{id}/model", SetProviderModelAsync);
        group.MapPost("/copilot/connect", CopilotConnectAsync);
        group.MapPost("/ollama/models", ListOllamaModelsAsync);
        group.MapPost("/models/discover", DiscoverProviderModelsAsync);

        return endpoints;
    }

    private static async Task<Ok<List<ModelProvider>>> ListProvidersAsync(
        [FromServices] IModelProviderRepository repository,
        [FromQuery] ModelPurpose? purpose,
        CancellationToken ct)
    {
        var providers = await repository.GetAllProvidersAsync(ct).ConfigureAwait(false);
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
        var provider = await repository.GetProviderAsync(id, ct).ConfigureAwait(false);
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

        var existing = await repository.GetProviderAsync(provider.Id, ct).ConfigureAwait(false);
        if (existing is not null)
            return TypedResults.BadRequest($"Provider with id '{provider.Id}' already exists");

        provider.CreatedAt = DateTime.UtcNow;
        provider.UpdatedAt = DateTime.UtcNow;

        await repository.UpsertProviderAsync(provider, ct).ConfigureAwait(false);
        logger.LogInformation("Created model provider {ProviderId}", provider.Id);

        return TypedResults.Created($"/api/model-providers/{provider.Id}", provider);
    }

    private static async Task<Results<Ok<ModelProvider>, NotFound>> UpdateProviderAsync(
        string id,
        [FromBody] ModelProvider provider,
        [FromServices] IModelProviderRepository repository,
        CancellationToken ct)
    {
        var existing = await repository.GetProviderAsync(id, ct).ConfigureAwait(false);
        if (existing is null)
            return TypedResults.NotFound();

        provider.Id = id;
        provider.CreatedAt = existing.CreatedAt;
        provider.UpdatedAt = DateTime.UtcNow;

        await repository.UpsertProviderAsync(provider, ct).ConfigureAwait(false);
        return TypedResults.Ok(provider);
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteProviderAsync(
        string id,
        [FromServices] IModelProviderRepository repository,
        CancellationToken ct)
    {
        var existing = await repository.GetProviderAsync(id, ct).ConfigureAwait(false);
        if (existing is null)
            return TypedResults.NotFound();

        if (existing.IsBuiltIn)
            return TypedResults.Problem(
                "Built-in model providers cannot be deleted. You can edit their configuration instead.",
                statusCode: StatusCodes.Status400BadRequest);

        await repository.DeleteProviderAsync(id, ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<ModelProviderConnectionTestResult>> TestProviderAsync(
        string id,
        [FromServices] IModelProviderRepository repository,
        [FromServices] IModelProviderResolver resolver,
        CancellationToken ct)
    {
        var provider = await repository.GetProviderAsync(id, ct).ConfigureAwait(false);
        if (provider is null)
            return TypedResults.Ok(new ModelProviderConnectionTestResult(false, $"Provider '{id}' not found"));

        var result = await resolver.TestConnectionAsync(provider, ct).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    private static async Task<Ok<ModelProviderConnectionTestResult>> TestEmbeddingAsync(
        string id,
        [FromServices] IModelProviderRepository repository,
        [FromServices] IModelProviderResolver resolver,
        CancellationToken ct)
    {
        var provider = await repository.GetProviderAsync(id, ct).ConfigureAwait(false);
        if (provider is null)
            return TypedResults.Ok(new ModelProviderConnectionTestResult(false, $"Provider '{id}' not found"));

        var result = await resolver.TestEmbeddingConnectionAsync(provider, ct).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<ProviderModelsResponse>, NotFound>> ListProviderModelsAsync(
        string id,
        [FromServices] IModelProviderRepository repository,
        [FromServices] ProviderModelCatalogService modelCatalogService,
        CancellationToken ct)
    {
        var provider = await repository.GetProviderAsync(id, ct).ConfigureAwait(false);
        if (provider is null)
        {
            return TypedResults.NotFound();
        }

        var result = await modelCatalogService.ListModelsAsync(provider, ct).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<ModelProvider>, NotFound, BadRequest<string>>> SetProviderModelAsync(
        string id,
        [FromBody] SetProviderModelRequest request,
        [FromServices] IModelProviderRepository repository,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ModelName))
        {
            return TypedResults.BadRequest("Model name is required");
        }

        var provider = await repository.GetProviderAsync(id, ct).ConfigureAwait(false);
        if (provider is null)
        {
            return TypedResults.NotFound();
        }

        provider.ModelName = request.ModelName.Trim();
        provider.UpdatedAt = DateTime.UtcNow;

        await repository.UpsertProviderAsync(provider, ct).ConfigureAwait(false);
        return TypedResults.Ok(provider);
    }

    private static async Task<Ok<CopilotConnectResult>> CopilotConnectAsync(
        [FromBody] CopilotConnectRequest request,
        [FromServices] CopilotConnectService connectService,
        CancellationToken ct)
    {
        var result = await connectService.ConnectAndListModelsAsync(request.GithubToken, ct).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    private static async Task<Ok<OllamaModelsResponse>> ListOllamaModelsAsync(
        [FromBody] OllamaModelsRequest request,
        [FromServices] ProviderModelCatalogService modelCatalogService,
        CancellationToken ct)
    {
        var catalogResult = await modelCatalogService.ListOllamaModelsAsync(request.Endpoint, ct).ConfigureAwait(false);
        return TypedResults.Ok(new OllamaModelsResponse
        {
            Models = catalogResult.Models,
            Error = catalogResult.Error
        });
    }

    private static async Task<Ok<ProviderModelsResponse>> DiscoverProviderModelsAsync(
        [FromBody] ProviderModelDiscoveryRequest request,
        [FromServices] ProviderModelCatalogService modelCatalogService,
        CancellationToken ct)
    {
        var result = await modelCatalogService
            .ListModelsAsync(request.ProviderType, request.Endpoint, request.Auth, ct)
            .ConfigureAwait(false);

        return TypedResults.Ok(result);
    }
}
