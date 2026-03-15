namespace lucia.Wyoming.Telemetry;

/// <summary>
/// Persistent record of a voice pipeline transcript with full diagnostic metadata.
/// Stored in MongoDB for troubleshooting and quality analysis.
/// </summary>
public sealed record TranscriptRecord
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }

    // STT result
    public required string Text { get; init; }
    public required float Confidence { get; init; }

    // Audio metadata
    public required double AudioDurationMs { get; init; }
    public required int SampleRate { get; init; }
    public required int SampleCount { get; init; }

    // Model info
    public required string SttModelId { get; init; }
    public string? VadModelId { get; init; }
    public required bool VadActive { get; init; }
    public string? DiarizationModelId { get; init; }
    public required bool DiarizationActive { get; init; }

    // Speaker identification
    public string? SpeakerId { get; init; }
    public string? SpeakerName { get; init; }
    public float? SpeakerSimilarity { get; init; }
    public bool? IsProvisionalSpeaker { get; init; }
    public bool NewProfileCreated { get; init; }

    // Command routing
    public string? RouteResult { get; init; }
    public string? MatchedSkill { get; init; }
    public float? RouteConfidence { get; init; }
    public bool CommandFiltered { get; init; }

    // Pipeline timing
    public required PipelineStageTiming[] Stages { get; init; }

    // Response
    public string? ResponseText { get; init; }
}
