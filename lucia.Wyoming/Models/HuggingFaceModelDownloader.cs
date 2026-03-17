using System.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Models;

public sealed class HuggingFaceModelDownloader(
    IOptionsMonitor<HuggingFaceOptions> options,
    ILogger<HuggingFaceModelDownloader> logger)
{
    public async Task<bool> EnsureAuthenticatedAsync(CancellationToken ct)
    {
        var (exitCode, stdout, _) = await RunProcessAsync("hf", "auth whoami", ct);

        if (exitCode == 0)
        {
            logger.LogInformation("Already authenticated with Hugging Face as {Identity}", stdout.Trim());
            return true;
        }

        var token = options.CurrentValue.ApiToken;

        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Hugging Face authentication failed and no API token is configured");
            return false;
        }

        var (loginExit, _, loginStderr) = await RunProcessAsync(
            "hf",
            $"auth login --token {token} --add-to-git-credential",
            ct);

        if (loginExit == 0)
        {
            logger.LogInformation("Successfully authenticated with Hugging Face");
            return true;
        }

        logger.LogWarning("Hugging Face login failed: {Error}", loginStderr.Trim());
        return false;
    }

    public async Task<ModelDownloadResult> DownloadModelAsync(
        string repoId,
        string cacheDirectory,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken ct)
    {
        if (!await EnsureAuthenticatedAsync(ct))
        {
            return ModelDownloadResult.Failure(repoId, "Hugging Face authentication failed");
        }

        Directory.CreateDirectory(cacheDirectory);

        logger.LogInformation("Starting download of {RepoId} to {CacheDirectory}", repoId, cacheDirectory);

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "hf",
            Arguments = $"download {repoId} --cache-dir {cacheDirectory}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        process.Start();

        // Register cancellation to kill the process by PID
        var pid = process.Id;
        await using var ctr = ct.Register(() =>
        {
            try
            {
                using var killProcess = Process.Start("kill", pid.ToString());
                killProcess?.WaitForExit(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to terminate download process {Pid}", pid);
            }
        });

        var snapshotPath = string.Empty;
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        // Read stdout line by line to capture progress and the final snapshot path
        while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
        {
            if (TryParseProgress(line, repoId) is { } progressReport)
            {
                progress?.Report(progressReport);
            }

            // The hf CLI prints the snapshot path as the last non-empty line on success
            if (!string.IsNullOrWhiteSpace(line))
            {
                snapshotPath = line.Trim();
            }
        }

        var stderr = await stderrTask;
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            logger.LogWarning("Download of {RepoId} failed with exit code {ExitCode}: {Error}",
                repoId, process.ExitCode, stderr.Trim());
            return ModelDownloadResult.Failure(repoId, stderr.Trim());
        }

        if (string.IsNullOrWhiteSpace(snapshotPath) || !Directory.Exists(snapshotPath))
        {
            logger.LogWarning("Download of {RepoId} completed but snapshot path could not be resolved", repoId);
            return ModelDownloadResult.Failure(repoId, "Snapshot path not found in command output");
        }

        logger.LogInformation("Downloaded {RepoId} to {SnapshotPath}", repoId, snapshotPath);
        return ModelDownloadResult.Successful(repoId, snapshotPath);
    }

    public Task<bool> CheckForUpdateAsync(
        string repoId,
        string localModelPath,
        DateTimeOffset? remoteLastModified,
        CancellationToken ct)
    {
        _ = ct; // acknowledge the token for future use

        if (remoteLastModified is null)
        {
            logger.LogDebug("No remote modification date provided for {RepoId}; skipping update check", repoId);
            return Task.FromResult(false);
        }

        if (!Directory.Exists(localModelPath))
        {
            logger.LogDebug("Local model path {Path} does not exist for {RepoId}; update available", localModelPath, repoId);
            return Task.FromResult(true);
        }

        var newestLocal = Directory.EnumerateFiles(localModelPath, "*", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f).LastWriteTimeUtc)
            .DefaultIfEmpty(DateTimeOffset.MinValue.UtcDateTime)
            .Max();

        var updateAvailable = remoteLastModified.Value > newestLocal;

        if (updateAvailable)
        {
            logger.LogInformation("Update available for {RepoId}: remote={Remote}, local={Local}",
                repoId, remoteLastModified.Value, newestLocal);
        }

        return Task.FromResult(updateAvailable);
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string command,
        string arguments,
        CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        process.Start();

        var pid = process.Id;
        await using var ctr = ct.Register(() =>
        {
            try
            {
                using var killProcess = Process.Start("kill", pid.ToString());
                killProcess?.WaitForExit(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to terminate process {Pid}", pid);
            }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        await process.WaitForExitAsync(ct);

        return (process.ExitCode, stdout, stderr);
    }

    private static ModelDownloadProgress? TryParseProgress(string line, string modelId)
    {
        // The HF CLI outputs lines like: "Downloading model.onnx:  45%|████      | 450M/1.00G"
        var percentIndex = line.IndexOf('%');
        if (percentIndex < 0)
        {
            return null;
        }

        // Walk backwards from '%' to find the start of the number
        var start = percentIndex - 1;
        while (start >= 0 && (char.IsDigit(line[start]) || line[start] == '.'))
        {
            start--;
        }

        start++;

        if (start >= percentIndex)
        {
            return null;
        }

        var percentSpan = line.AsSpan(start, percentIndex - start);

        if (!double.TryParse(percentSpan, System.Globalization.CultureInfo.InvariantCulture, out var percent))
        {
            return null;
        }

        return new ModelDownloadProgress
        {
            ModelId = modelId,
            BytesDownloaded = 0,
            TotalBytes = 0,
            PercentComplete = percent,
        };
    }
}
