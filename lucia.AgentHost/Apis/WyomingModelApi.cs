using lucia.Wyoming.Models;
using Microsoft.Extensions.Options;

namespace lucia.AgentHost.Apis;

/// <summary>
/// REST API endpoints for Wyoming model management.
/// </summary>
public static class WyomingModelApi
{
    public static void MapWyomingModelEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/wyoming/models")
            .WithTags("Wyoming Models");

        group.MapGet("/", (ModelCatalogService catalog) =>
        {
            var models = catalog.GetAvailableModels();
            return Results.Ok(models);
        }).WithName("GetAvailableModels");

        group.MapGet("/installed", (ModelCatalogService catalog) =>
        {
            var models = catalog.GetInstalledModels();
            return Results.Ok(models);
        }).WithName("GetInstalledModels");

        group.MapGet("/active", (ModelManager manager) =>
        {
            return Results.Ok(new { ActiveModel = manager.ActiveModelId });
        }).WithName("GetActiveModel");

        group.MapPost("/{modelId}/download", async (
            string modelId,
            ModelCatalogService catalog,
            ModelDownloader downloader,
            IOptionsMonitor<SttModelOptions> modelOptions,
            CancellationToken ct) =>
        {
            var model = catalog.GetModelById(modelId);
            if (model is null)
            {
                return Results.NotFound($"Model '{modelId}' not found in catalog");
            }

            var result = await downloader
                .DownloadModelAsync(model, modelOptions.CurrentValue.ModelBasePath, ct: ct)
                .ConfigureAwait(false);
            return result.Success
                ? Results.Ok(result)
                : Results.BadRequest(result);
        }).WithName("DownloadModel");

        group.MapPost("/{modelId}/activate", async (
            string modelId,
            ModelManager manager,
            CancellationToken ct) =>
        {
            try
            {
                await manager.SwitchActiveModelAsync(modelId, ct).ConfigureAwait(false);
                return Results.Ok(new { ActiveModel = modelId });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        }).WithName("ActivateModel");

        group.MapDelete("/{modelId}", async (
            string modelId,
            ModelManager manager,
            CancellationToken ct) =>
        {
            try
            {
                await manager.DeleteModelAsync(modelId, ct).ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        }).WithName("DeleteModel");
    }
}
