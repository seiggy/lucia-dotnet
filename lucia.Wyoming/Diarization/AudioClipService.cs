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
    private const string OnboardingStagingDirectoryName = ".onboarding-staging";
    private const string DeletedProfilesDirectoryName = ".deleted-profiles";
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
    // ponytail: clip writes are low-volume; split this into per-profile locks only if measured contention appears.
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    // ponytail: profile lifecycle operations are administrative; use keyed locks if contention is measured.
    private readonly SemaphoreSlim _profileLifecycleLock = new(1, 1);
    private readonly HashSet<string> _blockedProfileIds = new(StringComparer.Ordinal);

    internal SemaphoreSlim ProfileLifecycleLock => _profileLifecycleLock;

    public async Task<string> SaveClipAsync(
        string profileId,
        ReadOnlyMemory<float> audio,
        int sampleRate,
        string? transcript,
        CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_blockedProfileIds.Contains(profileId)
                || File.Exists(GetProfileDeletionMarkerPath(profileId)))
            {
                throw new InvalidOperationException($"Profile '{profileId}' is being deleted.");
            }

            return await SaveClipCoreAsync(
                profileId,
                GetProfileDirectory(profileId),
                audio,
                sampleRate,
                transcript,
                ct).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public IReadOnlyList<string> GetStoredProfileIds()
    {
        _fileLock.Wait();
        try
        {
            var basePath = options.CurrentValue.AudioClipBasePath;
            return Directory.Exists(basePath)
                ? Directory.GetDirectories(basePath)
                    .Select(Path.GetFileName)
                    .Where(static id => !string.IsNullOrWhiteSpace(id) && !id.StartsWith('.'))
                    .Cast<string>()
                    .ToArray()
                : [];
        }

        finally
        {
            _fileLock.Release();
        }
    }

    public IReadOnlyList<string> GetTombstonedProfileIds()
    {
        _fileLock.Wait();
        try
        {
            var tombstoneDirectory = Path.Combine(
                options.CurrentValue.AudioClipBasePath,
                DeletedProfilesDirectoryName);
            return Directory.Exists(tombstoneDirectory)
                ? Directory.GetFiles(tombstoneDirectory)
                    .Select(Path.GetFileName)
                    .Where(static id => !string.IsNullOrWhiteSpace(id))
                    .Cast<string>()
                    .ToArray()
                : [];
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveOnboardingPromotionMarkerAsync(
        string sessionId,
        string targetProfileId,
        CancellationToken ct)
    {
        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var stagingDirectory = GetOnboardingStagingDirectory(sessionId);
            Directory.CreateDirectory(stagingDirectory);
            var safeTargetProfileId = GetSafePathSegment(targetProfileId, nameof(targetProfileId));
            var markerPath = Path.Combine(stagingDirectory, "target-profile.txt");
            var stagingPath = $"{markerPath}.tmp";
            try
            {
                await File.WriteAllTextAsync(stagingPath, safeTargetProfileId, ct).ConfigureAwait(false);
                File.Move(stagingPath, markerPath, overwrite: true);
            }
            finally
            {
                DeleteIfExists(stagingPath);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public IReadOnlyList<OnboardingClipPromotion> GetOnboardingClipPromotions()
    {
        _fileLock.Wait();
        try
        {
            var stagingRoot = GetOnboardingStagingRoot();
            if (!Directory.Exists(stagingRoot))
            {
                return [];
            }

            return Directory.GetDirectories(stagingRoot)
                .Select(directory =>
                {
                    var markerPath = Path.Combine(directory, "target-profile.txt");
                    return new OnboardingClipPromotion(
                        Path.GetFileName(directory),
                        File.Exists(markerPath) ? File.ReadAllText(markerPath).Trim() : null);
                })
                .ToArray();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<string> SaveOnboardingClipAsync(
        string sessionId,
        ReadOnlyMemory<float> audio,
        int sampleRate,
        string? transcript,
        CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await SaveClipCoreAsync(
                GetStagingProfileId(sessionId),
                GetOnboardingStagingDirectory(sessionId),
                audio,
                sampleRate,
                transcript,
                ct).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<string> SaveClipCoreAsync(
        string profileId,
        string profileDir,
        ReadOnlyMemory<float> audio,
        int sampleRate,
        string? transcript,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        var maxClips = options.CurrentValue.MaxClipsPerProfile;
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
        var metadataPath = Path.Combine(profileDir, $"{clipId}.json");
        var wavStagingPath = $"{wavPath}.tmp";
        var metadataStagingPath = $"{metadataPath}.tmp";
        try
        {
            await WavWriter.WriteAsync(wavStagingPath, audio, sampleRate, ct).ConfigureAwait(false);
            File.Move(wavStagingPath, wavPath);

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
            var json = JsonSerializer.Serialize(metadata, JsonOptions);
            await File.WriteAllTextAsync(metadataStagingPath, json, ct).ConfigureAwait(false);
            File.Move(metadataStagingPath, metadataPath);

            logger.LogInformation(
                "Saved audio clip {ClipId} for profile {ProfileId} ({Duration:F1}s, {Size} bytes)",
                clipId, profileId, duration.TotalSeconds, fileInfo.Length);
        }
        catch
        {
            DeleteIfExists(wavPath);
            DeleteIfExists(metadataPath);
            throw;
        }
        finally
        {
            DeleteIfExists(wavStagingPath);
            DeleteIfExists(metadataStagingPath);
        }

        return clipId;
    }

    public IReadOnlyList<AudioClipInfo> GetClips(string profileId)
    {
        _fileLock.Wait();
        try
        {
            var profileDir = GetProfileDirectory(profileId);
            return GetClipsInternal(profileDir);
        }

        finally
        {
            _fileLock.Release();
        }
    }

    public void RemoveIncompleteProfileClips(string profileId)
    {
        _fileLock.Wait();
        try
        {
            var profileDir = GetProfileDirectory(profileId);
            if (!Directory.Exists(profileDir))
            {
                return;
            }

            foreach (var temporaryFile in Directory.GetFiles(profileDir, "*.tmp"))
            {
                File.Delete(temporaryFile);
            }
            foreach (var wavFile in Directory.GetFiles(profileDir, "*.wav"))
            {
                if (!File.Exists(Path.ChangeExtension(wavFile, ".json"))
                    && !TryRecoverMovedMetadata(profileId, wavFile))
                {
                    File.Delete(wavFile);
                }
            }
            foreach (var metadataFile in Directory.GetFiles(profileDir, "*.json"))
            {
                if (!File.Exists(Path.ChangeExtension(metadataFile, ".wav")))
                {
                    try
                    {
                        var metadata = JsonSerializer.Deserialize<AudioClipInfo>(
                            File.ReadAllText(metadataFile),
                            JsonOptions);
                        if (!string.Equals(metadata?.ProfileId, profileId, StringComparison.Ordinal))
                        {
                            continue;
                        }
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex, "Removing orphaned corrupt audio clip metadata {MetadataPath}", metadataFile);
                    }

                    File.Delete(metadataFile);
                }
            }

        }
        finally
        {
            _fileLock.Release();
        }
    }

    private bool TryRecoverMovedMetadata(string profileId, string wavFile)
    {
        var basePath = options.CurrentValue.AudioClipBasePath;
        var metadataName = $"{Path.GetFileNameWithoutExtension(wavFile)}.json";
        foreach (var directory in Directory.GetDirectories(basePath))
        {
            if (string.Equals(directory, Path.GetDirectoryName(wavFile), StringComparison.Ordinal)
                || Path.GetFileName(directory).StartsWith('.'))
            {
                continue;
            }

            var candidate = Path.Combine(directory, metadataName);
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                var metadata = JsonSerializer.Deserialize<AudioClipInfo>(
                    File.ReadAllText(candidate),
                    JsonOptions);
                if (!string.Equals(metadata?.ProfileId, profileId, StringComparison.Ordinal))
                {
                    continue;
                }

                File.Move(candidate, Path.ChangeExtension(wavFile, ".json"));
                return true;
            }
            catch (JsonException)
            {
                continue;
            }
        }

        return false;
    }

    public string? GetClipFilePath(string profileId, string clipId)
    {
        _fileLock.Wait();
        try
        {
            var safeClipId = GetSafePathSegment(clipId, nameof(clipId));
            var path = Path.Combine(GetProfileDirectory(profileId), $"{safeClipId}.wav");
            return File.Exists(path) ? path : null;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public void DeleteClip(string profileId, string clipId)
    {
        _fileLock.Wait();
        try
        {
            var safeClipId = GetSafePathSegment(clipId, nameof(clipId));
            var profileDir = GetProfileDirectory(profileId);
            DeleteClipFiles(profileDir, safeClipId);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public void DeleteProfileClips(string profileId)
    {
        _fileLock.Wait();
        try
        {
            var safeProfileId = GetSafePathSegment(profileId, nameof(profileId));
            if (!_blockedProfileIds.Add(safeProfileId))
            {
                throw new InvalidOperationException($"Profile '{profileId}' already has an active lifecycle operation.");
            }
            var profileDir = Path.Combine(options.CurrentValue.AudioClipBasePath, safeProfileId);
            if (Directory.Exists(profileDir))
            {
                Directory.Delete(profileDir, recursive: true);
            }
        }
        catch
        {
            _blockedProfileIds.Remove(GetSafePathSegment(profileId, nameof(profileId)));
            throw;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public void PurgeProfileClips(string profileId)
    {
        _fileLock.Wait();
        try
        {
            var profileDir = GetProfileDirectory(profileId);
            if (Directory.Exists(profileDir))
            {
                Directory.Delete(profileDir, recursive: true);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public void AllowProfileClips(string profileId)
    {
        _fileLock.Wait();
        try
        {
            _blockedProfileIds.Remove(GetSafePathSegment(profileId, nameof(profileId)));
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public void BlockProfileClips(string profileId)
    {
        _fileLock.Wait();
        try
        {
            var safeProfileId = GetSafePathSegment(profileId, nameof(profileId));
            if (!_blockedProfileIds.Add(safeProfileId))
            {
                throw new InvalidOperationException($"Profile '{profileId}' already has an active lifecycle operation.");
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public bool TombstoneProfileClips(string profileId)
    {
        _fileLock.Wait();
        try
        {
            var safeProfileId = GetSafePathSegment(profileId, nameof(profileId));
            var blockAdded = _blockedProfileIds.Add(safeProfileId);
            var tombstoneDirectory = Path.Combine(
                options.CurrentValue.AudioClipBasePath,
                DeletedProfilesDirectoryName);
            try
            {
                Directory.CreateDirectory(tombstoneDirectory);
                var tombstonePath = Path.Combine(tombstoneDirectory, safeProfileId);
                if (File.Exists(tombstonePath))
                {
                    return false;
                }

                File.WriteAllText(tombstonePath, string.Empty);
                return true;
            }
            catch
            {
                if (blockAdded)
                {
                    _blockedProfileIds.Remove(safeProfileId);
                }

                throw;
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public void RemoveProfileClipTombstone(string profileId)
    {
        _fileLock.Wait();
        try
        {
            DeleteIfExists(GetProfileDeletionMarkerPath(profileId));
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public void DeleteOnboardingStagingClips(IReadOnlySet<string>? activeSessionIds = null)
    {
        _fileLock.Wait();
        try
        {
            var stagingRoot = GetOnboardingStagingRoot();
            if (!Directory.Exists(stagingRoot))
            {
                return;
            }

            string[] stagingDirectories;
            try
            {
                stagingDirectories = Directory.GetDirectories(stagingRoot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Unable to enumerate onboarding staging directory {StagingRoot}", stagingRoot);
                return;
            }

            foreach (var stagingDirectory in stagingDirectories)
            {
                var sessionId = Path.GetFileName(stagingDirectory);
                if (activeSessionIds?.Contains(sessionId) == true)
                {
                    continue;
                }

                try
                {
                    Directory.Delete(stagingDirectory, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(
                        ex,
                        "Unable to delete abandoned onboarding staging directory {StagingDirectory}",
                        stagingDirectory);
                }
            }

        }

        finally
        {
            _fileLock.Release();
        }
    }

    public void DeleteOnboardingSessionClips(string sessionId)
    {
        _fileLock.Wait();
        try
        {
            var stagingDirectory = GetOnboardingStagingDirectory(sessionId);
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task ReassignClipAsync(
        string sourceProfileId,
        string clipId,
        string targetProfileId,
        CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ReassignClipCoreAsync(
                sourceProfileId,
                clipId,
                targetProfileId,
                ct).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task ReassignClipCoreAsync(
        string sourceProfileId,
        string clipId,
        string targetProfileId,
        CancellationToken ct,
        bool sourceIsOnboardingStaging = false,
        bool allowBlockedSource = false,
        bool allowBlockedTarget = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetProfileId);
        ThrowIfMoveBlocked(
            sourceProfileId,
            targetProfileId,
            sourceIsOnboardingStaging,
            allowBlockedSource,
            allowBlockedTarget);
        var safeClipId = GetSafePathSegment(clipId, nameof(clipId));

        var sourceDir = sourceIsOnboardingStaging
            ? GetOnboardingStagingDirectory(sourceProfileId)
            : GetProfileDirectory(sourceProfileId);
        var targetDir = GetProfileDirectory(targetProfileId);
        Directory.CreateDirectory(targetDir);

        var wavSource = Path.Combine(sourceDir, $"{safeClipId}.wav");
        var jsonSource = Path.Combine(sourceDir, $"{safeClipId}.json");
        var wavDest = Path.Combine(targetDir, $"{safeClipId}.wav");
        var jsonDest = Path.Combine(targetDir, $"{safeClipId}.json");
        var wavStagingPath = $"{wavDest}.tmp";
        var jsonStagingPath = $"{jsonDest}.tmp";

        if (!File.Exists(wavSource) || !File.Exists(jsonSource))
        {
            if (!File.Exists(wavSource)
                && File.Exists(jsonSource)
                && File.Exists(wavDest))
            {
                File.Move(jsonSource, jsonDest, overwrite: true);
                return;
            }

            DeleteIfExists(wavSource);
            DeleteIfExists(jsonSource);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(jsonSource, ct).ConfigureAwait(false);
            var clip = JsonSerializer.Deserialize<AudioClipInfo>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Audio clip metadata '{jsonSource}' is empty.");
            var updatedJson = JsonSerializer.Serialize(
                clip with { ProfileId = targetProfileId },
                JsonOptions);
            await File.WriteAllTextAsync(jsonStagingPath, updatedJson, ct).ConfigureAwait(false);
            File.Copy(wavSource, wavStagingPath, overwrite: true);
            File.Move(wavStagingPath, wavDest, overwrite: true);
            File.Move(jsonStagingPath, jsonDest, overwrite: true);
            DeleteIfExists(jsonSource);
            DeleteIfExists(wavSource);
        }
        finally
        {
            DeleteIfExists(wavStagingPath);
            DeleteIfExists(jsonStagingPath);
        }

        logger.LogInformation(
            "Reassigned clip {ClipId} from profile {SourceProfile} to {TargetProfile}",
            clipId, sourceProfileId, targetProfileId);
    }

    public async Task MoveClipsAsync(string sourceProfileId, string targetProfileId, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await MoveClipsCoreAsync(sourceProfileId, targetProfileId, ct).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    internal async Task MoveTombstonedProfileClipsAsync(
        string sourceProfileId,
        string targetProfileId)
    {
        await _fileLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await MoveClipsCoreAsync(
                sourceProfileId,
                targetProfileId,
                CancellationToken.None,
                allowBlockedSource: true).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task MoveOnboardingClipsAsync(
        string sessionId,
        string targetProfileId,
        CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await MoveClipsCoreAsync(
                sessionId,
                targetProfileId,
                ct,
                sourceIsOnboardingStaging: true).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task MoveClipsCoreAsync(
        string sourceProfileId,
        string targetProfileId,
        CancellationToken ct,
        bool sourceIsOnboardingStaging = false,
        bool allowBlockedSource = false,
        bool allowBlockedTarget = false)
    {
        ThrowIfMoveBlocked(
            sourceProfileId,
            targetProfileId,
            sourceIsOnboardingStaging,
            allowBlockedSource,
            allowBlockedTarget);
        var sourceDir = sourceIsOnboardingStaging
            ? GetOnboardingStagingDirectory(sourceProfileId)
            : GetProfileDirectory(sourceProfileId);
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
            await ReassignClipCoreAsync(
                sourceProfileId,
                clipId,
                targetProfileId,
                ct,
                sourceIsOnboardingStaging,
                allowBlockedSource,
                allowBlockedTarget).ConfigureAwait(false);
        }
        foreach (var wavPath in Directory.GetFiles(sourceDir, "*.wav"))
        {
            var metadataPath = Path.ChangeExtension(wavPath, ".json");
            if (!File.Exists(metadataPath))
            {
                File.Delete(wavPath);
            }
        }
        foreach (var stagingPath in Directory.GetFiles(sourceDir, "*.tmp"))
        {
            File.Delete(stagingPath);
        }
        if (sourceIsOnboardingStaging)
        {
            DeleteIfExists(Path.Combine(sourceDir, "target-profile.txt"));
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
        var safeProfileId = GetSafePathSegment(profileId, nameof(profileId));
        return Path.Combine(options.CurrentValue.AudioClipBasePath, safeProfileId);
    }

    private string GetOnboardingStagingDirectory(string sessionId)
    {
        var safeSessionId = GetSafePathSegment(sessionId, nameof(sessionId));
        return Path.Combine(GetOnboardingStagingRoot(), safeSessionId);
    }

    private string GetOnboardingStagingRoot() =>
        Path.Combine(options.CurrentValue.AudioClipBasePath, OnboardingStagingDirectoryName);

    private string GetProfileDeletionMarkerPath(string profileId) =>
        Path.Combine(
            options.CurrentValue.AudioClipBasePath,
            DeletedProfilesDirectoryName,
            GetSafePathSegment(profileId, nameof(profileId)));

    private static string GetStagingProfileId(string sessionId) => $"onboarding-{sessionId}";

    private static string GetSafePathSegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var safeValue = Path.GetFileName(value);
        if (!string.Equals(value, safeValue, StringComparison.Ordinal)
            || !string.Equals(safeValue, safeValue.ToLowerInvariant(), StringComparison.Ordinal)
            || safeValue.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')
            || s_windowsReservedNames.Contains(safeValue))
        {
            throw new ArgumentException($"Invalid path segment: '{value}'", parameterName);
        }

        return safeValue;
    }

    private static void DeleteClipFiles(string profileDir, string clipId)
    {
        var safeClipId = GetSafePathSegment(clipId, nameof(clipId));
        var wavFile = Path.Combine(profileDir, $"{safeClipId}.wav");
        var jsonFile = Path.Combine(profileDir, $"{safeClipId}.json");
        if (File.Exists(wavFile)) File.Delete(wavFile);
        if (File.Exists(jsonFile)) File.Delete(jsonFile);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void ThrowIfMoveBlocked(
        string sourceProfileId,
        string targetProfileId,
        bool sourceIsOnboardingStaging,
        bool allowBlockedSource,
        bool allowBlockedTarget)
    {
        if ((!sourceIsOnboardingStaging
                && !allowBlockedSource
                && IsProfileClipsBlocked(sourceProfileId))
            || (!allowBlockedTarget && IsProfileClipsBlocked(targetProfileId)))
        {
            throw new InvalidOperationException("Cannot move clips to or from a profile being deleted.");
        }
    }

    private bool IsProfileClipsBlocked(string profileId) =>
        _blockedProfileIds.Contains(profileId)
        || File.Exists(GetProfileDeletionMarkerPath(profileId));
}
