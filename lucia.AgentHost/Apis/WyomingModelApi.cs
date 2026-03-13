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

        group.MapGet("/", (ModelCatalogService catalog, bool? all) =>
        {
            var models = catalog.GetAvailableModels();
            // By default, only return models compatible with the online/streaming recognizer.
            // Pass ?all=true to include offline-only models.
            if (all != true)
                models = models.Where(m => m.IsOnlineCompatible).ToList();
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
                    var lastReportedPercent = -1;
                    var downloadProgress = new DirectProgress<ModelDownloadProgress>(update =>
                    {
                        var pct = (int)Math.Round(update.PercentComplete);
                        if (pct == lastReportedPercent) return;
                        lastReportedPercent = pct;
                        var mbDownloaded = update.BytesDownloaded / (1024.0 * 1024.0);
                        var mbTotal = update.TotalBytes / (1024.0 * 1024.0);
                        stages.Report(0, pct, $"{mbDownloaded:F1}/{mbTotal:F0} MB");
                    });

                    var extractionProgress = new DirectProgress<(int percent, string message)>(update =>
                    {
                        // percent 0-100 = extraction progress, 100 = "Installing model files…"
                        if (update.message.StartsWith("Extracting"))
                            stages.Report(1, update.percent, update.message);
                        else if (update.message.StartsWith("Installing"))
                            stages.Report(2, 0, update.message);
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

                    stages.Report(1, 100, "Done");
                    stages.Report(2, 100, "Done");
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
            catch (InvalidOperationException ex)
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

    /// <summary>Synchronous progress reporter — avoids SynchronizationContext.Post delays.</summary>
    private sealed class DirectProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
