using Microsoft.Extensions.Logging;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace lucia.Wyoming.Models;

public sealed class ModelDownloader(IHttpClientFactory httpClientFactory, ILogger<ModelDownloader> logger)
{
    private const int BufferSize = 81_920;

    public async Task<ModelDownloadResult> DownloadModelAsync(
        AsrModelDefinition model,
        string targetBasePath,
        IProgress<ModelDownloadProgress>? progress = null,
        IProgress<(int percent, string message)>? extractionProgress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetBasePath);

        var targetDirectory = Path.Combine(targetBasePath, model.Id);
        if (IsModelDirectoryReady(targetDirectory))
        {
            return ModelDownloadResult.AlreadyExists(model.Id, targetDirectory);
        }

        var stagingRoot = Path.Combine(
            Path.GetTempPath(),
            "lucia-wyoming-models",
            Guid.NewGuid().ToString("N"));

        var archivePath = Path.Combine(stagingRoot, $"{model.Id}.tar.bz2");
        var extractionDirectory = Path.Combine(stagingRoot, "extract");

        try
        {
            Directory.CreateDirectory(stagingRoot);
            Directory.CreateDirectory(extractionDirectory);
            Directory.CreateDirectory(targetBasePath);

            var client = httpClientFactory.CreateClient();
            using var response = await client
                .GetAsync(model.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? model.SizeBytes;
            await using (var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var archiveStream = File.Create(archivePath))
            {
                var buffer = new byte[BufferSize];
                long downloadedBytes = 0;

                while (true)
                {
                    var bytesRead = await contentStream
                        .ReadAsync(buffer.AsMemory(0, buffer.Length), ct)
                        .ConfigureAwait(false);

                    if (bytesRead == 0)
                    {
                        break;
                    }

                    await archiveStream
                        .WriteAsync(buffer.AsMemory(0, bytesRead), ct)
                        .ConfigureAwait(false);

                    downloadedBytes += bytesRead;
                    progress?.Report(CreateProgress(model.Id, downloadedBytes, totalBytes));
                }
            }

            extractionProgress?.Report((80, "Extracting model archive…"));
            ExtractArchive(archivePath, extractionDirectory);

            extractionProgress?.Report((90, "Installing model files…"));
            var extractedModelDirectory = ResolveExtractedModelDirectory(extractionDirectory, model.Id);

            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }

            CopyDirectory(extractedModelDirectory, targetDirectory);

            progress?.Report(
                new ModelDownloadProgress
                {
                    ModelId = model.Id,
                    BytesDownloaded = totalBytes,
                    TotalBytes = totalBytes,
                    PercentComplete = 100d,
                });

            logger.LogInformation(
                "Downloaded and extracted Wyoming model {ModelId} to {ModelPath}",
                model.Id,
                targetDirectory);

            return ModelDownloadResult.Successful(model.Id, targetDirectory);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Failed to download Wyoming model {ModelId}", model.Id);
            return ModelDownloadResult.Failure(model.Id, ex.Message);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }
        }
    }

    private static ModelDownloadProgress CreateProgress(string modelId, long bytesDownloaded, long totalBytes)
    {
        var percentComplete = totalBytes > 0
            ? Math.Min(100d, bytesDownloaded * 100d / totalBytes)
            : 0d;

        return new ModelDownloadProgress
        {
            ModelId = modelId,
            BytesDownloaded = bytesDownloaded,
            TotalBytes = totalBytes,
            PercentComplete = percentComplete,
        };
    }

    private static void ExtractArchive(string archivePath, string extractionDirectory)
    {
        using var stream = File.OpenRead(archivePath);
        using var reader = ReaderFactory.Open(stream);

        while (reader.MoveToNextEntry())
        {
            if (reader.Entry.IsDirectory)
            {
                continue;
            }

            reader.WriteEntryToDirectory(
                extractionDirectory,
                new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true,
                });
        }
    }

    private static string ResolveExtractedModelDirectory(string extractionDirectory, string modelId)
    {
        var expectedDirectory = Path.Combine(extractionDirectory, modelId);
        if (Directory.Exists(expectedDirectory))
        {
            return expectedDirectory;
        }

        var directories = Directory.GetDirectories(extractionDirectory, "*", SearchOption.TopDirectoryOnly);
        var files = Directory.GetFiles(extractionDirectory, "*", SearchOption.TopDirectoryOnly);

        if (directories.Length == 1 && files.Length == 0)
        {
            return directories[0];
        }

        return extractionDirectory;
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directoryPath in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativeDirectory = Path.GetRelativePath(sourceDirectory, directoryPath);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativeDirectory));
        }

        foreach (var filePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativeFile = Path.GetRelativePath(sourceDirectory, filePath);
            var destinationFile = Path.Combine(destinationDirectory, relativeFile);
            var destinationParent = Path.GetDirectoryName(destinationFile);

            if (!string.IsNullOrWhiteSpace(destinationParent))
            {
                Directory.CreateDirectory(destinationParent);
            }

            File.Copy(filePath, destinationFile, overwrite: true);
        }
    }

    private static bool IsModelDirectoryReady(string modelDirectory) =>
        Directory.Exists(modelDirectory)
        && Directory.EnumerateFiles(modelDirectory, "*.onnx", SearchOption.AllDirectories).Any();
}
