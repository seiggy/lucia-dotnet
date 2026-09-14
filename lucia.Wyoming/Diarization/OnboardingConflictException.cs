namespace lucia.Wyoming.Diarization;

public sealed class OnboardingConflictException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
