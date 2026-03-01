using lucia.Agents.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// Endpoints for browsing available plugins and installing them.
/// </summary>
public static class PluginStoreApi
{
    public static RouteGroupBuilder MapPluginStoreApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/plugins")
            .WithTags("Plugin Store")
            .RequireAuthorization();

        group.MapGet("/available", GetAvailablePluginsAsync);
        group.MapPost("/{id}/install", InstallPluginAsync);

        return group;
    }

    private static async Task<Ok<List<AvailablePlugin>>> GetAvailablePluginsAsync(
        PluginManagementService service,
        string? query,
        CancellationToken ct)
    {
        var plugins = await service.GetAvailablePluginsAsync(query, ct);
        return TypedResults.Ok(plugins);
    }

    private static async Task<Results<Ok, NotFound<string>>> InstallPluginAsync(
        string id,
        PluginManagementService service,
        CancellationToken ct)
    {
        // Find the plugin in available repos to get install info
        var available = await service.GetAvailablePluginsAsync(null, ct);
        var match = available.FirstOrDefault(p =>
            p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return TypedResults.NotFound($"Plugin '{id}' not found in any repository.");

        await service.InstallPluginAsync(
            match.Id,
            match.RepositoryId,
            new lucia.Agents.Configuration.PluginManifestEntry
            {
                Id = match.Id,
                Name = match.Name,
                Description = match.Description,
                Version = match.Version,
                Author = match.Author,
                Tags = match.Tags,
                Path = match.PluginPath,
                Homepage = match.Homepage,
            },
            ct);

        return TypedResults.Ok();
    }
}
