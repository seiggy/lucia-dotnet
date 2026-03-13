namespace lucia.Wyoming.Diarization;

public interface ISpeakerProfileStore
{
    Task<SpeakerProfile?> GetAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<SpeakerProfile>> GetAllAsync(CancellationToken ct);
    Task<IReadOnlyList<SpeakerProfile>> GetProvisionalProfilesAsync(CancellationToken ct);
    Task<IReadOnlyList<SpeakerProfile>> GetEnrolledProfilesAsync(CancellationToken ct);
    Task CreateAsync(SpeakerProfile profile, CancellationToken ct);
    Task UpdateAsync(SpeakerProfile profile, CancellationToken ct);
    Task DeleteAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<SpeakerProfile>> GetExpiredProvisionalProfilesAsync(int retentionDays, CancellationToken ct);
}
