using lucia.Agents.Models;
using lucia.Agents.Services;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// REST API endpoints for presence detection sensor management.
/// Provides CRUD for sensor-to-area mappings, occupancy queries, and global config.
/// </summary>
public static class PresenceApi
{
    public static IEndpointRouteBuilder MapPresenceApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/presence")
            .WithTags("Presence");

        // --- Occupancy queries ---
        group.MapGet("/occupied", GetOccupiedAreasAsync);
        group.MapGet("/occupied/{areaId}", GetAreaOccupancyAsync);

        // --- Sensor mappings ---
        group.MapGet("/sensors", GetSensorMappingsAsync);
        group.MapPut("/sensors/{entityId}", UpdateSensorMappingAsync);
        group.MapDelete("/sensors/{entityId}", DeleteSensorMappingAsync);
        group.MapPost("/sensors/refresh", RefreshSensorsAsync);

        // --- Global config ---
        group.MapGet("/config", GetConfigAsync);
        group.MapPut("/config", UpdateConfigAsync);

        return endpoints;
    }

    private static async Task<IResult> GetOccupiedAreasAsync(
        IPresenceDetectionService presenceService,
        CancellationToken ct)
    {
        var areas = await presenceService.GetOccupiedAreasAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(areas);
    }

    private static async Task<IResult> GetAreaOccupancyAsync(
        string areaId,
        IPresenceDetectionService presenceService,
        CancellationToken ct)
    {
        var isOccupied = await presenceService.IsOccupiedAsync(areaId, ct).ConfigureAwait(false);
        var occupantCount = await presenceService.GetOccupantCountAsync(areaId, ct).ConfigureAwait(false);

        return TypedResults.Ok(new
        {
            areaId,
            isOccupied,
            occupantCount
        });
    }

    private static async Task<IResult> GetSensorMappingsAsync(
        IPresenceDetectionService presenceService,
        CancellationToken ct)
    {
        var mappings = await presenceService.GetSensorMappingsAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(mappings);
    }

    private static async Task<IResult> UpdateSensorMappingAsync(
        string entityId,
        UpdatePresenceSensorRequest request,
        IPresenceSensorRepository repo,
        CancellationToken ct)
    {
        var all = await repo.GetAllMappingsAsync(ct).ConfigureAwait(false);
        var existing = all.FirstOrDefault(m => m.EntityId == entityId);

        if (existing is null)
            return TypedResults.NotFound($"Sensor mapping '{entityId}' not found.");

        var updated = new PresenceSensorMapping
        {
            EntityId = entityId,
            AreaId = request.AreaId ?? existing.AreaId,
            AreaName = request.AreaName ?? existing.AreaName,
            Confidence = request.Confidence ?? existing.Confidence,
            IsUserOverride = true,
            IsDisabled = request.IsDisabled ?? existing.IsDisabled
        };

        await repo.UpsertMappingAsync(updated, ct).ConfigureAwait(false);
        return TypedResults.Ok(updated);
    }

    private static async Task<IResult> DeleteSensorMappingAsync(
        string entityId,
        IPresenceSensorRepository repo,
        CancellationToken ct)
    {
        await repo.DeleteMappingAsync(entityId, ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RefreshSensorsAsync(
        IPresenceDetectionService presenceService,
        CancellationToken ct)
    {
        await presenceService.RefreshSensorMappingsAsync(ct).ConfigureAwait(false);
        var mappings = await presenceService.GetSensorMappingsAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(mappings);
    }

    private static async Task<IResult> GetConfigAsync(
        IPresenceDetectionService presenceService,
        CancellationToken ct)
    {
        var enabled = await presenceService.IsEnabledAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new { enabled });
    }

    private static async Task<IResult> UpdateConfigAsync(
        UpdatePresenceConfigRequest request,
        IPresenceDetectionService presenceService,
        CancellationToken ct)
    {
        if (request.Enabled is not null)
        {
            await presenceService.SetEnabledAsync(request.Enabled.Value, ct).ConfigureAwait(false);
        }

        var enabled = await presenceService.IsEnabledAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new { enabled });
    }
}
