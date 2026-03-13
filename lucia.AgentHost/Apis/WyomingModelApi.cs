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
            .WithTags("Wyoming Models")
            .RequireAuthorization();

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

        group.MapPost("/{modelId}/download", (
            string modelId,
            ModelCatalogService catalog,
            ModelDownloader downloader,
            IOptions<SttModelOptions> modelOptions,
            BackgroundTaskService taskService) =>
        {
            var model = catalog.GetModelById(modelId);
            if (model is null)
            {
                return Results.NotFound($"Model '{modelId}' not found in catalog");
            }

            var taskId = taskService.StartTask(
                $"Downloading {model.Name}",
                async (_, progress, ct) =>
                {
                    var downloadProgress = new Progress<ModelDownloadProgress>(update =>
                    {
                        progress.Report((
                            (int)Math.Round(update.PercentComplete),
                            $"Downloaded {update.BytesDownloaded / 1024 / 1024}MB"));
                    });

                    var result = await downloader
                        .DownloadModelAsync(
                            model,
                            modelOptions.Value.ModelBasePath,
                            downloadProgress,
                            ct)
                        .ConfigureAwait(false);

                    if (!result.Success)
                    {
                        throw new InvalidOperationException(result.Error ?? "Download failed");
                    }
                });

            return Results.Accepted($"/api/tasks/background/{taskId}", new { taskId });
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
