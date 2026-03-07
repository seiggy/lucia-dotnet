using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.PluginFramework;
using lucia.Agents.Services;
using lucia.AgentHost.PluginFramework.Models;
using Microsoft.AspNetCore.Http.HttpResults;

namespace lucia.AgentHost.PluginFramework;

/// <summary>
/// Endpoints for managing installed plugins (list, enable, disable, uninstall, update, config schemas).
/// </summary>
public static class InstalledPluginApi
{
    public static RouteGroupBuilder MapInstalledPluginApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/plugins")
            .WithTags("Installed Plugins")
            .RequireAuthorization();

        group.MapGet("/installed", GetInstalledPluginsAsync);
        group.MapGet("/updates", CheckForUpdatesAsync);
        group.MapPost("/{id}/update", UpdatePluginAsync);
        group.MapGet("/config/schemas", GetPluginConfigSchemasAsync);
        group.MapPost("/{id}/enable", EnablePluginAsync);
        group.MapPost("/{id}/disable", DisablePluginAsync);
        group.MapDelete("/{id}", UninstallPluginAsync);

        return group;
    }

    private static async Task<Ok<List<InstalledPluginDto>>> GetInstalledPluginsAsync(
        PluginManagementService service, CancellationToken ct)
    {
        var plugins = await service.GetInstalledPluginsWithUpdateInfoAsync(ct)
            .ConfigureAwait(false);

        var dtos = plugins.Select(p => new InstalledPluginDto
        {
            Id = p.Plugin.Id,
            Name = p.Plugin.Name,
            Version = p.Plugin.Version,
            Source = p.Plugin.Source,
            RepositoryId = p.Plugin.RepositoryId,
            Description = p.Plugin.Description,
            Author = p.Plugin.Author,
            PluginPath = p.Plugin.PluginPath,
            Enabled = p.Plugin.Enabled,
            InstalledAt = p.Plugin.InstalledAt,
            UpdateAvailable = p.UpdateAvailable,
            AvailableVersion = p.AvailableVersion,
        }).ToList();

        return TypedResults.Ok(dtos);
    }

    private static async Task<Ok<List<PluginUpdateInfoDto>>> CheckForUpdatesAsync(
        PluginManagementService service, CancellationToken ct)
    {
        var updates = await service.CheckForUpdatesAsync(ct).ConfigureAwait(false);

        var dtos = updates.Select(u => new PluginUpdateInfoDto
        {
            PluginId = u.PluginId,
            PluginName = u.PluginName,
            InstalledVersion = u.InstalledVersion,
            AvailableVersion = u.AvailableVersion,
            RepositoryId = u.RepositoryId,
        }).ToList();

        return TypedResults.Ok(dtos);
    }

    private static async Task<Results<Ok<string>, NotFound<string>>> UpdatePluginAsync(
        string id, PluginManagementService service, CancellationToken ct)
    {
        var result = await service.UpdatePluginAsync(id, ct).ConfigureAwait(false);
        return result switch
        {
            PluginUpdateResult.Updated => TypedResults.Ok($"Plugin '{id}' updated successfully."),
            PluginUpdateResult.AlreadyUpToDate => TypedResults.Ok($"Plugin '{id}' is already up to date."),
            PluginUpdateResult.PluginNotInstalled => TypedResults.NotFound($"Plugin '{id}' is not installed."),
            PluginUpdateResult.PluginNotInRepository => TypedResults.NotFound($"Plugin '{id}' not found in any repository."),
            _ => TypedResults.NotFound($"Plugin '{id}' update failed."),
        };
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
