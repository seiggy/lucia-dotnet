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
    private static readonly HashSet<string> s_windowsReservedNames = new(
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5",
         "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5",
         "LPT6", "LPT7", "LPT8", "LPT9"],
        StringComparer.OrdinalIgnoreCase);
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

        var maxClips = options.CurrentValue.MaxClipsPerProfile;
        var profileDir = GetProfileDirectory(profileId);
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
        var profileDir = GetProfileDirectory(profileId);
        return GetClipsInternal(profileDir);
    }

    public string? GetClipFilePath(string profileId, string clipId)
    {
        ValidatePathSegment(clipId, nameof(clipId));
        var path = Path.Combine(GetProfileDirectory(profileId), $"{clipId}.wav");
        return File.Exists(path) ? path : null;
    }

    public void DeleteClip(string profileId, string clipId)
    {
        ValidatePathSegment(clipId, nameof(clipId));
        var profileDir = GetProfileDirectory(profileId);
        DeleteClipFiles(profileDir, clipId);
    }

    public void DeleteProfileClips(string profileId)
    {
        var profileDir = GetProfileDirectory(profileId);
        if (Directory.Exists(profileDir))
        {
            Directory.Delete(profileDir, recursive: true);
        }
    }

    public async Task ReassignClipAsync(
        string sourceProfileId,
        string clipId,
        string targetProfileId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetProfileId);
        ValidatePathSegment(clipId, nameof(clipId));

        var sourceDir = GetProfileDirectory(sourceProfileId);
        var targetDir = GetProfileDirectory(targetProfileId);
        Directory.CreateDirectory(targetDir);

        var wavSource = Path.Combine(sourceDir, $"{clipId}.wav");
        var jsonSource = Path.Combine(sourceDir, $"{clipId}.json");
        var wavDest = Path.Combine(targetDir, $"{clipId}.wav");
        var jsonDest = Path.Combine(targetDir, $"{clipId}.json");

        if (File.Exists(jsonSource))
        {
            var json = await File.ReadAllTextAsync(jsonSource, ct).ConfigureAwait(false);
            var clip = JsonSerializer.Deserialize<AudioClipInfo>(json, JsonOptions);
            if (clip is not null)
            {
                var updated = clip with { ProfileId = targetProfileId };
                var updatedJson = JsonSerializer.Serialize(updated, JsonOptions);
                var stagingPath = $"{jsonSource}.{Guid.NewGuid():N}.tmp";
                try
                {
                    await File.WriteAllTextAsync(stagingPath, updatedJson, ct).ConfigureAwait(false);
                    File.Move(stagingPath, jsonSource, overwrite: true);
                }
                finally
                {
                    if (File.Exists(stagingPath))
                    {
                        File.Delete(stagingPath);
                    }
                }
            }
        }

        ct.ThrowIfCancellationRequested();
        if (File.Exists(wavSource))
        {
            File.Move(wavSource, wavDest, overwrite: true);
        }
        if (File.Exists(jsonSource))
        {
            File.Move(jsonSource, jsonDest, overwrite: true);
        }

        logger.LogInformation(
            "Reassigned clip {ClipId} from profile {SourceProfile} to {TargetProfile}",
            clipId, sourceProfileId, targetProfileId);
    }

    public async Task MoveClipsAsync(string sourceProfileId, string targetProfileId, CancellationToken ct = default)
    {
        var sourceDir = GetProfileDirectory(sourceProfileId);
        var targetDir = GetProfileDirectory(targetProfileId);

        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        Directory.CreateDirectory(targetDir);

        foreach (var metadataPath in Directory.GetFiles(sourceDir, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            var clipId = Path.GetFileNameWithoutExtension(metadataPath);
            await ReassignClipAsync(sourceProfileId, clipId, targetProfileId, ct).ConfigureAwait(false);
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

    private List<AudioClipInfo> GetClipsInternal(string profileDir)
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
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Skipping corrupt audio clip metadata {MetadataPath}", jsonFile);
            }
        }

        return clips.OrderBy(c => c.CapturedAt).ToList();
    }

    private string GetProfileDirectory(string profileId)
    {
        ValidatePathSegment(profileId, nameof(profileId));
        return Path.Combine(options.CurrentValue.AudioClipBasePath, profileId);
    }

    private static void ValidatePathSegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
            || s_windowsReservedNames.Contains(value))
        {
            throw new ArgumentException($"Invalid path segment: '{value}'", parameterName);
        }
    }

    private static void DeleteClipFiles(string profileDir, string clipId)
    {
        ValidatePathSegment(clipId, nameof(clipId));
        var wavFile = Path.Combine(profileDir, $"{clipId}.wav");
        var jsonFile = Path.Combine(profileDir, $"{clipId}.json");
        if (File.Exists(wavFile)) File.Delete(wavFile);
        if (File.Exists(jsonFile)) File.Delete(jsonFile);
    }
}
