using System.Security.Cryptography;
using System.Text;
using lucia.AgentHost.Extensions;
using lucia.AgentHost.PluginFramework.Models;
using lucia.Agents.Configuration;
using lucia.Agents.PluginFramework;
using lucia.Agents.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace lucia.AgentHost.PluginFramework;

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
        var repos = await service.GetRepositoriesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(repos);
    }

    private static async Task<Ok<PluginRepositoryDefinition>> AddRepositoryAsync(
        AddPluginRepositoryRequest request,
        PluginManagementService service,
        CancellationToken ct)
    {
        var repo = new PluginRepositoryDefinition
        {
            Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Url.ToLowerInvariant())))[..16],
            Name = request.Url.Split('/').LastOrDefault() ?? request.Url,
            Url = request.Url,
            Branch = request.Branch ?? "main",
            ManifestPath = request.ManifestPath ?? "lucia-plugins.json",
            Type = "git",
            Enabled = true,
        };

        await service.AddRepositoryAsync(repo, ct).ConfigureAwait(false);
        return TypedResults.Ok(repo);
    }

    private static async Task<NoContent> RemoveRepositoryAsync(
        string id,
        PluginManagementService service,
        CancellationToken ct)
    {
        await service.RemoveRepositoryAsync(id, ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<Ok> SyncRepositoryAsync(
        string id,
        PluginManagementService service,
        CancellationToken ct)
    {
        await service.SyncRepositoryAsync(id, ct).ConfigureAwait(false);
        return TypedResults.Ok();
    }
}
