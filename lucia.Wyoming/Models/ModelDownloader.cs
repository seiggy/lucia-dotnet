using Microsoft.Extensions.Logging;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace lucia.Wyoming.Models;

public sealed class ModelDownloader(
    IHttpClientFactory httpClientFactory,
    HuggingFaceModelDownloader hfDownloader,
    ILogger<ModelDownloader> logger)
{
    private const int BufferSize = 81_920;

    public async Task<ModelDownloadResult> DownloadModelAsync(
        WyomingModelDefinition model,
        string targetBasePath,
        IProgress<ModelDownloadProgress>? progress = null,
        IProgress<(int percent, string message)>? extractionProgress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetBasePath);

        // Dispatch HuggingFace models to the HF CLI downloader
        if (model.Source == ModelSource.HuggingFace && !string.IsNullOrWhiteSpace(model.RepoId))
        {
            var hfTargetDirectory = Path.Combine(targetBasePath, model.Id);
            if (IsModelDirectoryReady(hfTargetDirectory))
            {
                return ModelDownloadResult.AlreadyExists(model.Id, hfTargetDirectory);
            }

            var hfResult = await hfDownloader.DownloadModelAsync(model.RepoId, targetBasePath, progress, ct);
            if (!hfResult.Success)
            {
                return hfResult;
            }

            // Signal download phase complete
            progress?.Report(new ModelDownloadProgress
            {
                ModelId = model.Id,
                BytesDownloaded = model.SizeBytes,
                TotalBytes = model.SizeBytes,
                PercentComplete = 100d,
            });

            // HF CLI downloads into a cache structure (models--org--name/snapshots/{hash}/).
            // Copy the snapshot contents into the flat {basePath}/{modelId}/ structure
            // that the model catalog expects.
            if (!string.Equals(hfResult.LocalPath, hfTargetDirectory, StringComparison.Ordinal)
                && Directory.Exists(hfResult.LocalPath))
            {
                extractionProgress?.Report((0, "Installing model files..."));

                Directory.CreateDirectory(hfTargetDirectory);
                var files = Directory.GetFiles(hfResult.LocalPath, "*", SearchOption.AllDirectories);

                for (var i = 0; i < files.Length; i++)
                {
                    var relativePath = Path.GetRelativePath(hfResult.LocalPath, files[i]);
                    var destPath = Path.Combine(hfTargetDirectory, relativePath);
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrWhiteSpace(destDir))
                        Directory.CreateDirectory(destDir);

                    File.Copy(files[i], destPath, overwrite: true);

                    var percent = (int)((double)(i + 1) / files.Length * 100);
                    extractionProgress?.Report((percent, $"Installing {relativePath}"));
                }

                extractionProgress?.Report((100, "Installation complete"));

                logger.LogInformation(
                    "Installed {FileCount} files from HuggingFace snapshot to {TargetPath}",
                    files.Length, hfTargetDirectory);
            }

            return ModelDownloadResult.Successful(model.Id, hfTargetDirectory);
        }

        var targetDirectory = Path.Combine(targetBasePath, model.Id);
        if (IsModelDirectoryReady(targetDirectory))
        {
            return ModelDownloadResult.AlreadyExists(model.Id, targetDirectory);
        }

        var stagingRoot = Path.Combine(
            Path.GetTempPath(),
            "lucia-wyoming-models",
            Guid.NewGuid().ToString("N"));

        var downloadFileName = GetDownloadFileName(model.DownloadUrl, model.Id);
        var downloadPath = Path.Combine(stagingRoot, downloadFileName);
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
            await using (var archiveStream = File.Create(downloadPath))
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

            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }

            if (IsArchiveFile(downloadPath))
            {
                extractionProgress?.Report((0, "Extracting model archive…"));
                var archiveProgress = new Progress<ProgressReport>(report =>
                {
                    extractionProgress?.Report((Convert.ToInt32(report.PercentComplete), "Extracting..."));
                });
                ExtractArchive(downloadPath, extractionDirectory, archiveProgress);

                extractionProgress?.Report((100, "Installing model files…"));
                var extractedModelDirectory = ResolveExtractedModelDirectory(extractionDirectory, model.Id);
                CopyDirectory(extractedModelDirectory, targetDirectory);
            }
            else
            {
                extractionProgress?.Report((100, "Installing model file…"));
                Directory.CreateDirectory(targetDirectory);
                var destFile = Path.Combine(targetDirectory, Path.GetFileName(downloadPath));
                File.Copy(downloadPath, destFile, overwrite: true);
            }

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

    private static void ExtractArchive(string archivePath, string extractionDirectory, IProgress<ProgressReport>? onProgress = null)
    {
        // SharpCompress uses Constants.RewindableBufferSize for the ring buffer during
        // format detection. The default (81920) is too small for bz2-compressed tar archives
        // where ~800KB of decompressed data must be inspected to confirm the inner tar format.
        // ReaderOptions.WithRewindableBufferSize doesn't flow through to the stream constructor,
        // so we set the static default directly.
        Constants.RewindableBufferSize = 1_048_576;

        var options = ReaderOptions.ForFilePath
            .WithProgress(onProgress);
        using var stream = File.OpenRead(archivePath);
        using var reader = ReaderFactory.OpenReader(stream, options);

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

    private static string GetDownloadFileName(string url, string modelId)
    {
        var uri = new Uri(url);
        var fileName = Path.GetFileName(uri.LocalPath);
        return string.IsNullOrWhiteSpace(fileName) ? $"{modelId}.tar.bz2" : fileName;
    }

    private static bool IsArchiveFile(string filePath) =>
        filePath.EndsWith(".tar.bz2", StringComparison.OrdinalIgnoreCase)
        || filePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
        || filePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)
        || filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    private static bool IsModelDirectoryReady(string modelDirectory) =>
        Directory.Exists(modelDirectory)
        && Directory.EnumerateFiles(modelDirectory, "*.onnx", SearchOption.AllDirectories).Any();
}
