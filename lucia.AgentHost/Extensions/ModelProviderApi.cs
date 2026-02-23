using lucia.Agents.Configuration;
using lucia.Agents.Mcp;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// Request body for the Copilot CLI connect endpoint.
/// </summary>
public sealed record CopilotConnectRequest(string? GithubToken);

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
        group.MapPost("/copilot/connect", CopilotConnectAsync);

        return endpoints;
    }

    private static async Task<Ok<List<ModelProvider>>> ListProvidersAsync(
        [FromServices] IModelProviderRepository repository,
        CancellationToken ct)
    {
        var providers = await repository.GetAllProvidersAsync(ct);
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

    private static async Task<Results<NoContent, NotFound>> DeleteProviderAsync(
        string id,
        [FromServices] IModelProviderRepository repository,
        CancellationToken ct)
    {
        var existing = await repository.GetProviderAsync(id, ct);
        if (existing is null)
            return TypedResults.NotFound();

        await repository.DeleteProviderAsync(id, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<ModelProviderTestResult>> TestProviderAsync(
        string id,
        [FromServices] IModelProviderRepository repository,
        [FromServices] IModelProviderFactory factory,
        CancellationToken ct)
    {
        var provider = await repository.GetProviderAsync(id, ct);
        if (provider is null)
            return TypedResults.Ok(new ModelProviderTestResult(false, $"Provider '{id}' not found"));

        var result = await factory.TestConnectionAsync(provider, ct);
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
}
