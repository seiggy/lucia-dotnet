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

        group.MapGet("/", (ModelCatalogService catalog, ModelManager manager, bool? all) =>
        {
            // Combine streaming STT models with offline STT models
            var streamingModels = catalog.GetAvailableModels();
            var offlineModels = catalog.GetAvailableModels(EngineType.OfflineStt);
            IEnumerable<object> combined = streamingModels.Cast<object>().Concat(offlineModels);

            if (all != true)
                combined = streamingModels.Where(m => m.IsOnlineCompatible).Cast<object>()
                    .Concat(offlineModels);

            return Results.Ok(combined.ToList());
        }).WithName("GetAvailableModels");

        group.MapGet("/installed", (ModelCatalogService catalog) =>
        {
            // Combine streaming + offline installed models
            var streaming = catalog.GetInstalledModels();
            var offline = catalog.GetInstalledModels(EngineType.OfflineStt);
            return Results.Ok(streaming.Cast<object>().Concat(offline).ToList());
        }).WithName("GetInstalledModels");

        group.MapGet("/active", (ModelManager manager, IEnumerable<lucia.Wyoming.Stt.ISttEngine> sttEngines) =>
        {
            // Report the active model for whichever engine type is actually running
            var engines = sttEngines.ToArray();
            var activeEngine = engines.FirstOrDefault(static e => e.IsReady) ?? engines.FirstOrDefault();
            var activeModelId = activeEngine is lucia.Wyoming.Stt.HybridSttEngine
                ? manager.GetActiveModelId(EngineType.OfflineStt)
                : manager.ActiveModelId;
            return Results.Ok(new { ActiveModel = activeModelId });
        }).WithName("GetActiveModel");

        group.MapPost("/{modelId}/download", async (
            string modelId,
            ModelCatalogService catalog,
            ModelDownloader downloader,
            IOptions<SttModelOptions> modelOptions,
            IBackgroundTaskQueue taskQueue,
            BackgroundTaskTracker tracker) =>
        {
            var model = catalog.GetModelById(modelId);
            if (model is null)
            {
                return Results.NotFound($"Model '{modelId}' not found in catalog");
            }

            var handle = tracker.CreateTask($"Downloading {model.Name}", ["Download", "Extract", "Install"]);
            var basePath = modelOptions.Value.ModelBasePath;

            await taskQueue.QueueBackgroundWorkItemAsync(async ct =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                handle.MarkRunning();
                var stages = handle.CreateStageProgress(3);

                try
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
                        if (update.message.StartsWith("Extracting"))
                            stages.Report(1, update.percent, update.message);
                        else if (update.message.StartsWith("Installing"))
                            stages.Report(2, 0, update.message);
                    });

                    stages.Report(0, 0, "Starting…");

                    var result = await downloader
                        .DownloadModelAsync(model, basePath, downloadProgress, extractionProgress, ct)
                        .ConfigureAwait(false);

                    if (!result.Success)
                        throw new InvalidOperationException(result.Error ?? "Download failed");

                    stages.Report(1, 100, "Done");
                    stages.Report(2, 100, "Done");
                    handle.MarkComplete(sw.Elapsed.TotalMilliseconds);
                }
                catch (Exception ex)
                {
                    handle.MarkFailed(ex.Message, sw.Elapsed.TotalMilliseconds);
                }
            }).ConfigureAwait(false);

            return Results.Accepted($"/api/tasks/background/{handle.TaskId}", new { taskId = handle.TaskId });
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

        // Engine-type-aware endpoints for all engine types (STT, VAD, Wake Word, Speaker Embedding).
        var engineGroup = app.MapGroup("/api/wyoming/engines/{engineType}/models")
            .WithTags("Wyoming Models")
            .RequireAuthorization();

        engineGroup.MapGet("/", (string engineType, ModelCatalogService catalog) =>
        {
            var et = ParseEngineType(engineType);
            if (et is null) return Results.BadRequest($"Unknown engine type: {engineType}");
            return Results.Ok(catalog.GetAvailableModels(et.Value));
        }).WithName("GetAvailableModelsByEngine");

        engineGroup.MapGet("/installed", (string engineType, ModelCatalogService catalog) =>
        {
            var et = ParseEngineType(engineType);
            if (et is null) return Results.BadRequest($"Unknown engine type: {engineType}");
            return Results.Ok(catalog.GetInstalledModels(et.Value));
        }).WithName("GetInstalledModelsByEngine");

        engineGroup.MapGet("/active", (string engineType, ModelManager manager) =>
        {
            var et = ParseEngineType(engineType);
            if (et is null) return Results.BadRequest($"Unknown engine type: {engineType}");
            return Results.Ok(new { ActiveModel = manager.GetActiveModelId(et.Value) });
        }).WithName("GetActiveModelByEngine");

        engineGroup.MapPost("/{modelId}/download", async (
            string engineType,
            string modelId,
            ModelCatalogService catalog,
            ModelDownloader downloader,
            ModelManager manager,
            IBackgroundTaskQueue taskQueue,
            BackgroundTaskTracker tracker) =>
        {
            var et = ParseEngineType(engineType);
            if (et is null) return Results.BadRequest($"Unknown engine type: {engineType}");

            var model = catalog.GetModelById(et.Value, modelId);
            if (model is null) return Results.NotFound($"Model '{modelId}' not found in {engineType} catalog");

            var handle = tracker.CreateTask($"Downloading {model.Name}", ["Download", "Install"]);
            var basePath = manager.GetModelBasePath(et.Value);

            await taskQueue.QueueBackgroundWorkItemAsync(async ct =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                handle.MarkRunning();
                var stages = handle.CreateStageProgress(2);

                try
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
                        stages.Report(1, update.percent, update.message);
                    });

                    stages.Report(0, 0, "Starting…");

                    var result = await downloader
                        .DownloadModelAsync(model, basePath, downloadProgress, extractionProgress, ct)
                        .ConfigureAwait(false);

                    if (!result.Success)
                        throw new InvalidOperationException(result.Error ?? "Download failed");

                    stages.Report(1, 100, "Done");
                    handle.MarkComplete(sw.Elapsed.TotalMilliseconds);
                }
                catch (Exception ex)
                {
                    handle.MarkFailed(ex.Message, sw.Elapsed.TotalMilliseconds);
                }
            }).ConfigureAwait(false);

            return Results.Accepted($"/api/tasks/background/{handle.TaskId}", new { taskId = handle.TaskId });
        }).WithName("DownloadModelByEngine");

        engineGroup.MapPost("/{modelId}/activate", async (
            string engineType,
            string modelId,
            ModelManager manager,
            CancellationToken ct) =>
        {
            var et = ParseEngineType(engineType);
            if (et is null) return Results.BadRequest($"Unknown engine type: {engineType}");

            try
            {
                await manager.SwitchActiveModelAsync(et.Value, modelId, ct).ConfigureAwait(false);
                return Results.Ok(new { ActiveModel = modelId });
            }
            catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
            catch (InvalidOperationException ex) { return Results.BadRequest(ex.Message); }
        }).WithName("ActivateModelByEngine");

        engineGroup.MapDelete("/{modelId}", async (
            string engineType,
            string modelId,
            ModelManager manager,
            CancellationToken ct) =>
        {
            var et = ParseEngineType(engineType);
            if (et is null) return Results.BadRequest($"Unknown engine type: {engineType}");

            try
            {
                await manager.DeleteModelAsync(et.Value, modelId, ct).ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
        }).WithName("DeleteModelByEngine");
    }

    private static EngineType? ParseEngineType(string engineType) =>
        engineType.ToLowerInvariant() switch
        {
            "stt" => EngineType.Stt,
            "offline-stt" or "offlinestt" => EngineType.OfflineStt,
            "vad" => EngineType.Vad,
            "kws" or "wakeword" => EngineType.WakeWord,
            "speaker-embedding" or "diarization" => EngineType.SpeakerEmbedding,
            "speech-enhancement" => EngineType.SpeechEnhancement,
            _ => null,
        };

    /// <summary>Synchronous progress reporter — avoids SynchronizationContext.Post delays.</summary>
    private sealed class DirectProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
