namespace lucia.Wyoming.Diarization;

public static class SpeakerProfileUpdate
{
    public static SpeakerProfile ApplyAtomic(
        SpeakerProfile existing,
        Func<SpeakerProfile, SpeakerProfile> transform)
    {
        if (existing.MergeTargetProfileId is not null)
        {
            var updated = transform(existing);
            if (updated.MergeTargetProfileId is null
                && updated with { MergeTargetProfileId = existing.MergeTargetProfileId } == existing)
            {
                return updated;
            }

            throw new ProfileMergeConflictException("A profile being merged cannot be updated.");
        }

        return transform(existing);
    }

    public static void EnsureNotClaimed(SpeakerProfile existing)
    {
        if (existing.MergeTargetProfileId is not null)
        {
            throw new ProfileMergeConflictException("A profile being merged cannot be updated.");
        }
    }
}
