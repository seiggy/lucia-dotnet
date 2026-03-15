using lucia.Wyoming.Telemetry;

namespace lucia.AgentHost.Apis;

/// <summary>
/// REST endpoints for querying persisted voice transcript history.
/// </summary>
public static class TranscriptHistoryApi
{
    public static void MapTranscriptHistoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/wyoming/transcripts")
            .WithTags("Voice Transcripts");

        group.MapGet("/", async (
            ITranscriptStore store,
            string? sessionId,
            string? since,
            string? speakerId,
            int? limit,
            CancellationToken ct) =>
        {
            DateTimeOffset? sinceDate = null;
            if (!string.IsNullOrWhiteSpace(since) && DateTimeOffset.TryParse(since, out var parsed))
            {
                sinceDate = parsed;
            }

            var records = await store.QueryAsync(sessionId, sinceDate, speakerId, limit ?? 50, ct)
                .ConfigureAwait(false);
            return Results.Ok(records);
        }).WithName("QueryTranscripts");

        group.MapGet("/recent", async (
            ITranscriptStore store,
            int? limit,
            CancellationToken ct) =>
        {
            var records = await store.GetRecentAsync(limit ?? 20, ct).ConfigureAwait(false);
            return Results.Ok(records);
        }).WithName("GetRecentTranscripts");

        group.MapGet("/{id}", async (
            string id,
            ITranscriptStore store,
            CancellationToken ct) =>
        {
            var record = await store.GetAsync(id, ct).ConfigureAwait(false);
            return record is not null ? Results.Ok(record) : Results.NotFound();
        }).WithName("GetTranscript");
    }
}
