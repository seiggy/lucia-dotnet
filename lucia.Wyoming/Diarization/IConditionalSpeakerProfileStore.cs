namespace lucia.Wyoming.Diarization;

public interface IConditionalSpeakerProfileStore
{
    Task<bool> DeleteExpiredProvisionalAsync(string id, DateTimeOffset cutoff, CancellationToken ct);
}
