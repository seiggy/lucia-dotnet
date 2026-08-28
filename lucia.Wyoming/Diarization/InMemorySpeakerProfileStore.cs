using System.Collections.Concurrent;

namespace lucia.Wyoming.Diarization;

public sealed class InMemorySpeakerProfileStore : ISpeakerProfileStore
{
    private readonly ConcurrentDictionary<string, SpeakerProfile> _profiles = new();

    public Task<SpeakerProfile?> GetAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return Task.FromResult(
            _profiles.TryGetValue(id, out var profile)
                ? CloneProfile(profile)
                : null);
    }

    public Task<IReadOnlyList<SpeakerProfile>> GetAllAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<SpeakerProfile>>([.. _profiles.Values.Select(CloneProfile)]);
    }

    public Task<IReadOnlyList<SpeakerProfile>> GetProvisionalProfilesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<SpeakerProfile>>(
            [.. _profiles.Values.Where(static profile => profile.IsProvisional).Select(CloneProfile)]);
    }

    public Task<IReadOnlyList<SpeakerProfile>> GetEnrolledProfilesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<SpeakerProfile>>(
            [.. _profiles.Values.Where(static profile => !profile.IsProvisional).Select(CloneProfile)]);
    }

    public Task CreateAsync(SpeakerProfile profile, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(profile);

        if (!_profiles.TryAdd(profile.Id, CloneProfile(profile)))
        {
            throw new InvalidOperationException($"Speaker profile '{profile.Id}' already exists.");
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(SpeakerProfile profile, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(profile);

        if (!_profiles.TryGetValue(profile.Id, out var existing))
        {
            throw new KeyNotFoundException($"Speaker profile '{profile.Id}' was not found.");
        }
        SpeakerProfileUpdate.EnsureNotClaimed(existing);

        if (!_profiles.TryUpdate(profile.Id, CloneProfile(profile), existing))
        {
            throw new InvalidOperationException($"Speaker profile '{profile.Id}' changed during update.");
        }
        return Task.CompletedTask;
    }

    public Task<SpeakerProfile?> UpdateAtomicAsync(string id, Func<SpeakerProfile, SpeakerProfile> transform, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(transform);

        while (_profiles.TryGetValue(id, out var existing))
        {
            var transformInput = CloneProfile(existing);
            var updated = SpeakerProfileUpdate.ApplyAtomic(transformInput, transform);
            var stored = CloneProfile(updated);
            if (_profiles.TryUpdate(id, stored, existing))
            {
                return Task.FromResult<SpeakerProfile?>(CloneProfile(stored));
            }
        }

        return Task.FromResult<SpeakerProfile?>(null);
    }

    public Task DeleteAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        _profiles.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteExpiredProvisionalAsync(
        string id,
        DateTimeOffset cutoff,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_profiles.TryGetValue(id, out var profile)
            || !profile.IsProvisional
            || profile.LastSeenAt >= cutoff)
        {
            return Task.FromResult(false);
        }

        var removed = ((ICollection<KeyValuePair<string, SpeakerProfile>>)_profiles)
            .Remove(new KeyValuePair<string, SpeakerProfile>(id, profile));
        return Task.FromResult(removed);
    }

    public Task<IReadOnlyList<SpeakerProfile>> GetExpiredProvisionalProfilesAsync(int retentionDays, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(retentionDays);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);

        return Task.FromResult<IReadOnlyList<SpeakerProfile>>(
            [.. _profiles.Values.Where(profile => profile.IsProvisional && profile.LastSeenAt < cutoff).Select(CloneProfile)]);
    }

    private static SpeakerProfile CloneProfile(SpeakerProfile p) => p with
    {
        AverageEmbedding = p.AverageEmbedding.ToArray(),
        Embeddings = p.Embeddings.Select(e => e.ToArray()).ToArray(),
    };
}
