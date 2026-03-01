using lucia.Agents.Configuration;
using lucia.Agents.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// Endpoints for managing installed plugins (list, enable, disable, uninstall).
/// </summary>
public static class InstalledPluginApi
{
    public static RouteGroupBuilder MapInstalledPluginApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/plugins")
            .WithTags("Installed Plugins")
            .RequireAuthorization();

        group.MapGet("/installed", GetInstalledPluginsAsync);
        group.MapPost("/{id}/enable", EnablePluginAsync);
        group.MapPost("/{id}/disable", DisablePluginAsync);
        group.MapDelete("/{id}", UninstallPluginAsync);

        return group;
    }

    private static async Task<Ok<List<InstalledPluginRecord>>> GetInstalledPluginsAsync(
        PluginManagementService service, CancellationToken ct)
    {
        var plugins = await service.GetInstalledPluginsAsync(ct);
        return TypedResults.Ok(plugins);
    }

    private static async Task<Ok> EnablePluginAsync(
        string id, PluginManagementService service, CancellationToken ct)
    {
        await service.SetPluginEnabledAsync(id, true, ct);
        return TypedResults.Ok();
    }

    private static async Task<Ok> DisablePluginAsync(
        string id, PluginManagementService service, CancellationToken ct)
    {
        await service.SetPluginEnabledAsync(id, false, ct);
        return TypedResults.Ok();
    }

    private static async Task<NoContent> UninstallPluginAsync(
        string id, PluginManagementService service, CancellationToken ct)
    {
        await service.UninstallPluginAsync(id, ct);
        return TypedResults.NoContent();
    }
}
