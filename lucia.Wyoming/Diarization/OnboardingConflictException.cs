namespace lucia.Wyoming.Diarization;

public sealed class OnboardingConflictException(string message) : InvalidOperationException(message);
