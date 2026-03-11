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

        group.MapGet("/", ListProvidersAsync)
            .WithSummary("List model providers")
            .WithDescription("Returns all configured LLM providers. Optionally filter by purpose (Chat or Embedding).");
        group.MapGet("/{id}", GetProviderAsync)
            .WithSummary("Get model provider by ID");
        group.MapPost("/", CreateProviderAsync)
            .WithSummary("Create a model provider")
            .WithDescription("Registers a new LLM provider configuration (OpenAI, Azure, Ollama, Anthropic, etc.).");
        group.MapPut("/{id}", UpdateProviderAsync)
            .WithSummary("Update a model provider");
        group.MapDelete("/{id}", DeleteProviderAsync)
            .WithSummary("Delete a model provider")
            .WithDescription("Built-in providers cannot be deleted.");
        group.MapPost("/{id}/test", TestProviderAsync)
            .WithSummary("Test provider chat connection")
            .WithDescription("Sends a test completion request to verify the provider is configured correctly.");
        group.MapPost("/{id}/test-embedding", TestEmbeddingAsync)
            .WithSummary("Test provider embedding connection");
        group.MapPost("/{id}/models", ListProviderModelsAsync)
            .WithSummary("List available models for a provider");
        group.MapPost("/{id}/model", SetProviderModelAsync)
            .WithSummary("Set the active model for a provider");
        group.MapPost("/copilot/connect", CopilotConnectAsync)
            .WithSummary("Connect to GitHub Copilot")
            .WithDescription("Experimental. Initiates Copilot connection and discovers available models.");
        group.MapPost("/ollama/models", ListOllamaModelsAsync)
            .WithSummary("List Ollama models from a remote endpoint");
        group.MapPost("/models/discover", DiscoverProviderModelsAsync)
            .WithSummary("Discover models from a provider endpoint")
            .WithDescription("Queries the provider's model catalog without saving a provider configuration.");

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
