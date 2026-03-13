using System.Collections.Concurrent;

namespace lucia.Wyoming.Diarization;

public sealed class InMemorySpeakerProfileStore : ISpeakerProfileStore
{
    private readonly ConcurrentDictionary<string, SpeakerProfile> _profiles = new();

    public Task<SpeakerProfile?> GetAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return Task.FromResult(_profiles.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<SpeakerProfile>> GetAllAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<SpeakerProfile>>([.. _profiles.Values]);
    }

    public Task<IReadOnlyList<SpeakerProfile>> GetProvisionalProfilesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<SpeakerProfile>>(
            [.. _profiles.Values.Where(static profile => profile.IsProvisional)]);
    }

    public Task<IReadOnlyList<SpeakerProfile>> GetEnrolledProfilesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<SpeakerProfile>>(
            [.. _profiles.Values.Where(static profile => !profile.IsProvisional)]);
    }

    public Task CreateAsync(SpeakerProfile profile, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(profile);

        if (!_profiles.TryAdd(profile.Id, profile))
        {
            throw new InvalidOperationException($"Speaker profile '{profile.Id}' already exists.");
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(SpeakerProfile profile, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(profile);

        if (!_profiles.ContainsKey(profile.Id))
        {
            throw new KeyNotFoundException($"Speaker profile '{profile.Id}' was not found.");
        }

        _profiles[profile.Id] = profile;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        _profiles.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SpeakerProfile>> GetExpiredProvisionalProfilesAsync(int retentionDays, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(retentionDays);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);

        return Task.FromResult<IReadOnlyList<SpeakerProfile>>(
            [.. _profiles.Values.Where(profile => profile.IsProvisional && profile.LastSeenAt < cutoff)]);
    }
}
