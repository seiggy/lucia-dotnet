using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// Endpoints for managing plugin repository sources (add/remove/sync).
/// </summary>
public static class PluginRepositoryApi
{
    public static RouteGroupBuilder MapPluginRepositoryApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/plugin-repos")
            .WithTags("Plugin Repositories")
            .RequireAuthorization();

        group.MapGet("/", ListRepositoriesAsync);
        group.MapPost("/", AddRepositoryAsync);
        group.MapDelete("/{id}", RemoveRepositoryAsync);
        group.MapPost("/{id}/sync", SyncRepositoryAsync);

        return group;
    }

    private static async Task<Ok<List<PluginRepositoryDefinition>>> ListRepositoriesAsync(
        PluginManagementService service, CancellationToken ct)
    {
        var repos = await service.GetRepositoriesAsync(ct);
        return TypedResults.Ok(repos);
    }

    private static async Task<Created> AddRepositoryAsync(
        PluginRepositoryDefinition repo,
        PluginManagementService service,
        CancellationToken ct)
    {
        await service.AddRepositoryAsync(repo, ct);
        return TypedResults.Created($"/api/plugin-repos/{repo.Id}");
    }

    private static async Task<NoContent> RemoveRepositoryAsync(
        string id,
        PluginManagementService service,
        CancellationToken ct)
    {
        await service.RemoveRepositoryAsync(id, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok> SyncRepositoryAsync(
        string id,
        PluginManagementService service,
        CancellationToken ct)
    {
        await service.SyncRepositoryAsync(id, ct);
        return TypedResults.Ok();
    }
}
