namespace lucia.Wyoming.Diarization;

public static class SpeakerProfileUpdate
{
    public static SpeakerProfile ApplyAtomic(
        SpeakerProfile existing,
        Func<SpeakerProfile, SpeakerProfile> transform)
    {
        if (existing.MergeTargetProfileId is not null)
        {
            throw new InvalidOperationException("A profile being merged cannot be updated.");
        }

        return transform(existing);
    }

    public static void EnsureNotClaimed(SpeakerProfile existing)
    {
        if (existing.MergeTargetProfileId is not null)
        {
            throw new InvalidOperationException("A profile being merged cannot be updated.");
        }
    }
}
