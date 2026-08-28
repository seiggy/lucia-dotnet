using System.Text.Json;

namespace lucia.VoiceBenchmarks;

public sealed class SpeakerBenchmarkManifest
{
    public string ManifestPath { get; }
    public IReadOnlyList<SpeakerBenchmarkClip> Clips { get; }

    private SpeakerBenchmarkManifest(string manifestPath, IReadOnlyList<SpeakerBenchmarkClip> clips)
    {
        ManifestPath = manifestPath;
        Clips = clips
            .OrderBy(static clip => clip.SpeakerId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static clip => clip.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static SpeakerBenchmarkManifest Load(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        var fullPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Benchmark manifest not found: {fullPath}", fullPath);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
        if (!document.RootElement.TryGetProperty("clips", out var clipsElement) || clipsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Manifest JSON must contain a top-level 'clips' array.");
        }

        var clips = new List<SpeakerBenchmarkClip>();
        foreach (var clipElement in clipsElement.EnumerateArray())
        {
            if (clipElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Each clip entry must be an object.");
            }

            var path = GetJsonString(clipElement, "path");
            var speakerId = GetJsonString(clipElement, "speaker_id") ?? GetJsonString(clipElement, "speakerId");
            var split = GetJsonString(clipElement, "split");
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(speakerId) || string.IsNullOrWhiteSpace(split))
            {
                throw new InvalidOperationException("Each clip requires 'path', 'speaker_id', and 'split' fields.");
            }

            clips.Add(new SpeakerBenchmarkClip(path, speakerId, split, fullPath));
        }

        return new SpeakerBenchmarkManifest(fullPath, clips);
    }

    public static string ResolveRelativePath(string manifestPath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(manifestDirectory, relativePath));
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Clips.Count == 0)
        {
            errors.Add("Manifest does not contain any clips.");
            return errors;
        }

        var speakerGroups = Clips
            .GroupBy(static clip => clip.SpeakerId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (speakerGroups.Count < 2)
        {
            errors.Add("The benchmark requires at least two speakers to produce impostor scores and EER.");
        }

        var pathsInBothSplits = Clips
            .GroupBy(static clip => clip.ResolvedPath, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Select(clip => clip.Split).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(static group => group.Key);
        foreach (var path in pathsInBothSplits)
        {
            errors.Add($"Clip '{path}' is used for both enrollment and test.");
        }

        foreach (var speakerGroup in speakerGroups)
        {
            var enrollCount = speakerGroup.Count(static clip => string.Equals(clip.Split, "enroll", StringComparison.OrdinalIgnoreCase));
            var testCount = speakerGroup.Count(static clip => string.Equals(clip.Split, "test", StringComparison.OrdinalIgnoreCase));
            if (enrollCount == 0 || testCount == 0)
            {
                errors.Add($"Speaker '{speakerGroup.Key}' is missing at least one enrollment and one test clip; each evaluated speaker must include both.");
            }
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateContentHashes(
        IReadOnlyList<BenchmarkClipProvenance> clips)
    {
        return clips
            .GroupBy(static clip => clip.Sha256, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Select(clip => clip.Split)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1
                    ? $"Audio content with SHA-256 '{group.Key}' is used for both enrollment and test."
                    : $"Audio content with SHA-256 '{group.Key}' appears more than once.")
            .ToArray();
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }
}
