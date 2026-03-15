namespace lucia.Wyoming.Telemetry;

/// <summary>
/// Persists and queries voice pipeline transcript records for troubleshooting and quality analysis.
/// </summary>
public interface ITranscriptStore
{
    Task SaveAsync(TranscriptRecord record, CancellationToken ct = default);
    Task<TranscriptRecord?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<TranscriptRecord>> QueryAsync(
        string? sessionId = null,
        DateTimeOffset? since = null,
        string? speakerId = null,
        int limit = 50,
        CancellationToken ct = default);
    Task<IReadOnlyList<TranscriptRecord>> GetRecentAsync(int limit = 20, CancellationToken ct = default);
}
