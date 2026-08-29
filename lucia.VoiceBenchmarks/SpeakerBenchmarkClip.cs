namespace lucia.VoiceBenchmarks;

public sealed class SpeakerBenchmarkClip
{
    public string Path { get; }
    public string SpeakerId { get; }
    public string SessionId { get; }
    public string Split { get; }
    public string ResolvedPath { get; }

    public SpeakerBenchmarkClip(string path, string speakerId, string sessionId, string split, string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(speakerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(split);

        Path = path.Trim();
        SpeakerId = speakerId.Trim();
        SessionId = sessionId.Trim();
        Split = split.Trim();

        if (!string.Equals(Split, "enroll", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Split, "test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Clip split '{Split}' is invalid. Use 'enroll' or 'test'.");
        }

        ResolvedPath = SpeakerBenchmarkManifest.ResolveRelativePath(manifestPath, Path);
    }
}
