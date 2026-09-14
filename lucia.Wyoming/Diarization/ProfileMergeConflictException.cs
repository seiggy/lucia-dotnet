namespace lucia.Wyoming.Diarization;

public sealed class ProfileMergeConflictException(string message) : InvalidOperationException(message);
