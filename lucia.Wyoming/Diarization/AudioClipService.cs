using System.Text.Json;
using lucia.Wyoming.Audio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Diarization;

/// <summary>
/// Manages voice audio clips on disk with FIFO rotation per profile.
/// Clips are stored at {AudioClipBasePath}/{profileId}/{clipId}.wav with metadata JSON alongside.
/// </summary>
public sealed class AudioClipService(
    IOptionsMonitor<VoiceProfileOptions> options,
    ILogger<AudioClipService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public async Task<string> SaveClipAsync(
        string profileId,
        ReadOnlyMemory<float> audio,
        int sampleRate,
        string? transcript,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var basePath = options.CurrentValue.AudioClipBasePath;
        var maxClips = options.CurrentValue.MaxClipsPerProfile;
        var profileDir = Path.Combine(basePath, profileId);
        Directory.CreateDirectory(profileDir);

        // FIFO rotation: delete oldest clips if at capacity
        var existingClips = GetClipsInternal(profileDir);
        while (existingClips.Count >= maxClips)
        {
            var oldest = existingClips.MinBy(c => c.CapturedAt);
            if (oldest is not null)
            {
                DeleteClipFiles(profileDir, oldest.Id);
                existingClips = GetClipsInternal(profileDir);
            }
            else
            {
                break;
            }
        }

        var clipId = Guid.NewGuid().ToString("N");
        var wavPath = Path.Combine(profileDir, $"{clipId}.wav");

        await WavWriter.WriteAsync(wavPath, audio, sampleRate, ct).ConfigureAwait(false);

        var fileInfo = new FileInfo(wavPath);
        var duration = TimeSpan.FromSeconds((double)audio.Length / sampleRate);

        var metadata = new AudioClipInfo
        {
            Id = clipId,
            ProfileId = profileId,
            CapturedAt = DateTimeOffset.UtcNow,
            Duration = duration,
            SampleRate = sampleRate,
            Transcript = transcript,
            FileSizeBytes = fileInfo.Length,
        };

        var metadataPath = Path.Combine(profileDir, $"{clipId}.json");
        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        await File.WriteAllTextAsync(metadataPath, json, ct).ConfigureAwait(false);

        logger.LogInformation(
            "Saved audio clip {ClipId} for profile {ProfileId} ({Duration:F1}s, {Size} bytes)",
            clipId, profileId, duration.TotalSeconds, fileInfo.Length);

        return clipId;
    }

    public IReadOnlyList<AudioClipInfo> GetClips(string profileId)
    {
        var profileDir = Path.Combine(options.CurrentValue.AudioClipBasePath, profileId);
        return GetClipsInternal(profileDir);
    }

    public string? GetClipFilePath(string profileId, string clipId)
    {
        var path = Path.Combine(options.CurrentValue.AudioClipBasePath, profileId, $"{clipId}.wav");
        return File.Exists(path) ? path : null;
    }

    public void DeleteClip(string profileId, string clipId)
    {
        var profileDir = Path.Combine(options.CurrentValue.AudioClipBasePath, profileId);
        DeleteClipFiles(profileDir, clipId);
    }

    public async Task ReassignClipAsync(
        string sourceProfileId,
        string clipId,
        string targetProfileId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clipId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetProfileId);

        var basePath = options.CurrentValue.AudioClipBasePath;
        var sourceDir = Path.Combine(basePath, sourceProfileId);
        var targetDir = Path.Combine(basePath, targetProfileId);
        Directory.CreateDirectory(targetDir);

        var wavSource = Path.Combine(sourceDir, $"{clipId}.wav");
        var jsonSource = Path.Combine(sourceDir, $"{clipId}.json");
        var wavDest = Path.Combine(targetDir, $"{clipId}.wav");
        var jsonDest = Path.Combine(targetDir, $"{clipId}.json");

        if (File.Exists(wavSource))
        {
            File.Move(wavSource, wavDest, overwrite: true);
        }

        if (File.Exists(jsonSource))
        {
            File.Move(jsonSource, jsonDest, overwrite: true);

            // Update profileId in metadata
            var json = await File.ReadAllTextAsync(jsonDest, ct).ConfigureAwait(false);
            var clip = JsonSerializer.Deserialize<AudioClipInfo>(json, JsonOptions);
            if (clip is not null)
            {
                var updated = clip with { ProfileId = targetProfileId };
                var updatedJson = JsonSerializer.Serialize(updated, JsonOptions);
                await File.WriteAllTextAsync(jsonDest, updatedJson, ct).ConfigureAwait(false);
            }
        }

        logger.LogInformation(
            "Reassigned clip {ClipId} from profile {SourceProfile} to {TargetProfile}",
            clipId, sourceProfileId, targetProfileId);
    }

    public async Task MoveClipsAsync(string sourceProfileId, string targetProfileId, CancellationToken ct = default)
    {
        var basePath = options.CurrentValue.AudioClipBasePath;
        var sourceDir = Path.Combine(basePath, sourceProfileId);
        var targetDir = Path.Combine(basePath, targetProfileId);

        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            ct.ThrowIfCancellationRequested();
            var destFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Move(file, destFile, overwrite: true);

            // Update profileId in metadata JSON via proper deserialization
            if (destFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var json = await File.ReadAllTextAsync(destFile, ct).ConfigureAwait(false);
                var clip = JsonSerializer.Deserialize<AudioClipInfo>(json, JsonOptions);
                if (clip is not null)
                {
                    var updated = clip with { ProfileId = targetProfileId };
                    var updatedJson = JsonSerializer.Serialize(updated, JsonOptions);
                    await File.WriteAllTextAsync(destFile, updatedJson, ct).ConfigureAwait(false);
                }
            }
        }

        // Clean up empty source directory
        if (Directory.Exists(sourceDir) && !Directory.EnumerateFileSystemEntries(sourceDir).Any())
        {
            Directory.Delete(sourceDir);
        }

        // Enforce max clips on target after merge
        var maxClips = options.CurrentValue.MaxClipsPerProfile;
        var targetClips = GetClipsInternal(targetDir);
        while (targetClips.Count > maxClips)
        {
            var oldest = targetClips.MinBy(c => c.CapturedAt);
            if (oldest is not null)
            {
                DeleteClipFiles(targetDir, oldest.Id);
                targetClips = GetClipsInternal(targetDir);
            }
            else
            {
                break;
            }
        }
    }

    private static List<AudioClipInfo> GetClipsInternal(string profileDir)
    {
        if (!Directory.Exists(profileDir))
        {
            return [];
        }

        var clips = new List<AudioClipInfo>();
        foreach (var jsonFile in Directory.GetFiles(profileDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(jsonFile);
                var clip = JsonSerializer.Deserialize<AudioClipInfo>(json, JsonOptions);
                if (clip is not null)
                {
                    clips.Add(clip);
                }
            }
            catch (JsonException)
            {
                // Skip corrupt metadata
            }
        }

        return clips.OrderBy(c => c.CapturedAt).ToList();
    }

    private static void DeleteClipFiles(string profileDir, string clipId)
    {
        var wavFile = Path.Combine(profileDir, $"{clipId}.wav");
        var jsonFile = Path.Combine(profileDir, $"{clipId}.json");
        if (File.Exists(wavFile)) File.Delete(wavFile);
        if (File.Exists(jsonFile)) File.Delete(jsonFile);
    }
}
