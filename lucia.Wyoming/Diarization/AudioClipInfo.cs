namespace lucia.Wyoming.Diarization;

/// <summary>
/// Metadata for a captured voice audio clip associated with a speaker profile.
/// </summary>
public sealed record AudioClipInfo
{
    public required string Id { get; init; }
    public required string ProfileId { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required TimeSpan Duration { get; init; }
    public required int SampleRate { get; init; }
    public string? Transcript { get; init; }
    public required long FileSizeBytes { get; init; }
}
