using System.Collections.Concurrent;

namespace lucia.Wyoming.Telemetry;

/// <summary>
/// In-memory transcript store used as a fallback when MongoDB is not available.
/// Caps storage at <see cref="MaxRecords"/> and drops the oldest records on overflow.
/// </summary>
public sealed class InMemoryTranscriptStore : ITranscriptStore
{
    internal const int MaxRecords = 1000;

    private readonly ConcurrentDictionary<string, TranscriptRecord> _byId = new();
    private readonly List<TranscriptRecord> _ordered = [];
    private readonly Lock _lock = new();

    public Task SaveAsync(TranscriptRecord record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(record);

        lock (_lock)
        {
            _byId[record.Id] = record;

            // Insert in timestamp-descending position for efficient recent queries.
            var index = _ordered.FindIndex(r => r.Timestamp <= record.Timestamp);
            if (index < 0)
            {
                _ordered.Add(record);
            }
            else
            {
                _ordered.Insert(index, record);
            }

            // Drop oldest records that exceed the cap.
            while (_ordered.Count > MaxRecords)
            {
                var oldest = _ordered[^1];
                _ordered.RemoveAt(_ordered.Count - 1);
                _byId.TryRemove(oldest.Id, out _);
            }
        }

        return Task.CompletedTask;
    }

    public Task<TranscriptRecord?> GetAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        _byId.TryGetValue(id, out var record);
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<TranscriptRecord>> QueryAsync(
        string? sessionId,
        DateTimeOffset? since,
        string? speakerId,
        int limit,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            IEnumerable<TranscriptRecord> query = _ordered;

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                query = query.Where(r => r.SessionId == sessionId);
            }

            if (since.HasValue)
            {
                query = query.Where(r => r.Timestamp >= since.Value);
            }

            if (!string.IsNullOrWhiteSpace(speakerId))
            {
                query = query.Where(r => r.SpeakerId == speakerId);
            }

            return Task.FromResult<IReadOnlyList<TranscriptRecord>>(
                [.. query.Take(limit)]);
        }
    }

    public Task<IReadOnlyList<TranscriptRecord>> GetRecentAsync(int limit, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<TranscriptRecord>>(
                [.. _ordered.Take(limit)]);
        }
    }
}
