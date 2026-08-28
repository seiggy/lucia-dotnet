namespace lucia.VoiceBenchmarks;

public sealed record BenchmarkClipProvenance(
    string Path,
    string SpeakerId,
    string Split,
    string Sha256);
