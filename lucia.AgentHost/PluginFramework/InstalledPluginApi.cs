using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.PluginFramework;
using lucia.Agents.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace lucia.AgentHost.PluginFramework;

/// <summary>
/// Endpoints for managing installed plugins (list, enable, disable, uninstall, config schemas).
/// </summary>
public static class InstalledPluginApi
{
    public static RouteGroupBuilder MapInstalledPluginApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/plugins")
            .WithTags("Installed Plugins")
            .RequireAuthorization();

        group.MapGet("/installed", GetInstalledPluginsAsync);
        group.MapGet("/config/schemas", GetPluginConfigSchemasAsync);
        group.MapPost("/{id}/enable", EnablePluginAsync);
        group.MapPost("/{id}/disable", DisablePluginAsync);
        group.MapDelete("/{id}", UninstallPluginAsync);

        return group;
    }

    private static async Task<Ok<List<InstalledPluginRecord>>> GetInstalledPluginsAsync(
        PluginManagementService service, CancellationToken ct)
    {
        var plugins = await service.GetInstalledPluginsAsync(ct)
            .ConfigureAwait(false);
        return TypedResults.Ok(plugins);
    }

    /// <summary>
    /// Returns configuration schemas declared by all loaded plugins.
    /// The dashboard uses these to render dynamic config forms on the Plugins page.
    /// </summary>
    private static Ok<List<PluginConfigSchemaDto>> GetPluginConfigSchemasAsync(
        IEnumerable<ILuciaPlugin> plugins)
    {
        var schemas = plugins
            .Where(p => p.ConfigSection is not null && p.ConfigProperties.Count > 0)
            .Select(p => new PluginConfigSchemaDto
            {
                PluginId = p.PluginId,
                Section = p.ConfigSection!,
                Description = p.ConfigDescription ?? "",
                Properties = p.ConfigProperties.Select(cp => new PluginConfigPropertyDto
                {
                    Name = cp.Name,
                    Type = cp.Type,
                    Description = cp.Description,
                    DefaultValue = cp.DefaultValue,
                    IsSensitive = cp.IsSensitive,
                }).ToList(),
            })
            .ToList();

        return TypedResults.Ok(schemas);
    }

    private static async Task<Ok> EnablePluginAsync(
        string id, PluginManagementService service, CancellationToken ct)
    {
        await service.SetPluginEnabledAsync(id, true, ct)
            .ConfigureAwait(false);
        return TypedResults.Ok();
    }

    private static async Task<Ok> DisablePluginAsync(
        string id, PluginManagementService service, CancellationToken ct)
    {
        await service.SetPluginEnabledAsync(id, false, ct)
            .ConfigureAwait(false);
        return TypedResults.Ok();
    }

    private static async Task<NoContent> UninstallPluginAsync(
        string id, PluginManagementService service, CancellationToken ct)
    {
        await service.UninstallPluginAsync(id, ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }
}
