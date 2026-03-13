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

            var taskId = taskService.StartStagedTask(
                $"Downloading {model.Name}",
                ["Download", "Extract", "Install"],
                async (_, stages, ct) =>
                {
                    var downloadProgress = new Progress<ModelDownloadProgress>(update =>
                    {
                        var mbDownloaded = update.BytesDownloaded / 1024 / 1024;
                        var mbTotal = update.TotalBytes / 1024 / 1024;
                        stages.Report(0, (int)Math.Round(update.PercentComplete), $"{mbDownloaded}/{mbTotal} MB");
                    });

                    var extractionProgress = new Progress<(int percent, string message)>(update =>
                    {
                        if (update.percent <= 85)
                            stages.Report(1, update.percent == 80 ? 50 : 0, update.message);
                        else
                            stages.Report(2, update.percent >= 95 ? 50 : 0, update.message);
                    });

                    stages.Report(0, 0, "Starting…");

                    var result = await downloader
                        .DownloadModelAsync(
                            model,
                            modelOptions.Value.ModelBasePath,
                            downloadProgress,
                            extractionProgress,
                            ct)
                        .ConfigureAwait(false);

                    if (!result.Success)
                    {
                        throw new InvalidOperationException(result.Error ?? "Download failed");
                    }

                    stages.Report(2, 100, "Complete");
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
