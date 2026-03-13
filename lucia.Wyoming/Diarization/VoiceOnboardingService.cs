using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Diarization;

public sealed class VoiceOnboardingService
{
    private static readonly string[] OnboardingPrompts =
    [
        "Please say: Turn on the living room lights",
        "Please say: What's the weather like today",
        "Please say: Set the thermostat to seventy two degrees",
        "Please say: Play some music in the kitchen",
        "Please say: Hey Lucia, good morning",
        "Please say: Set a timer for five minutes",
        "Please say: Turn off all the lights",
    ];

    private readonly ConcurrentDictionary<string, OnboardingSession> _sessions = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();
    private readonly IDiarizationEngine _diarization;
    private readonly ISpeakerProfileStore _profileStore;
    private readonly AudioQualityAnalyzer _qualityAnalyzer;
    private readonly VoiceProfileOptions _options;
    private readonly ILogger<VoiceOnboardingService> _logger;

    public VoiceOnboardingService(
        IDiarizationEngine diarization,
        ISpeakerProfileStore profileStore,
        AudioQualityAnalyzer qualityAnalyzer,
        IOptions<VoiceProfileOptions> options,
        ILogger<VoiceOnboardingService> logger)
    {
        _diarization = diarization;
        _profileStore = profileStore;
        _qualityAnalyzer = qualityAnalyzer;
        _options = options.Value;
        _logger = logger;
    }

    public Task<OnboardingSession> StartOnboardingAsync(
        string speakerName,
        string? provisionalProfileId,
        CancellationToken ct)
    {
        _ = ct;
        CleanupAbandonedSessions();

        var sampleCount = _options.OnboardingSampleCount;
        var prompts = SelectPrompts(sampleCount);

        var session = new OnboardingSession
        {
            Id = Guid.NewGuid().ToString("N"),
            SpeakerName = speakerName,
            ProvisionalProfileId = provisionalProfileId,
            Prompts = prompts,
        };

        _sessions.TryAdd(session.Id, session);
        _logger.LogInformation("Started onboarding session {SessionId} for {Name}", session.Id, speakerName);

        return Task.FromResult(session);
    }

    public async Task<OnboardingStepResult> ProcessSampleAsync(
        string sessionId,
        ReadOnlyMemory<float> audioSamples,
        int sampleRate,
        CancellationToken ct)
    {
        var sessionLock = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await sessionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                throw new InvalidOperationException($"Onboarding session '{sessionId}' not found");
            }

            var quality = _qualityAnalyzer.Analyze(audioSamples.Span, sampleRate);
            if (!quality.IsAcceptable)
            {
                if (quality.IsTooQuiet)
                {
                    return OnboardingStepResult.Retry("That was a bit quiet. Could you speak a little louder?");
                }

                if (quality.IsTooShort)
                {
                    return OnboardingStepResult.Retry("I need a longer sample. Please say the full phrase.");
                }
            }

            var embedding = _diarization.ExtractEmbedding(audioSamples.Span, sampleRate);
            session.CollectedEmbeddings.Add(embedding.Vector);
            session.CurrentPromptIndex++;

            if (session.CurrentPromptIndex >= session.Prompts.Count)
            {
                var profile = await FinalizeEnrollmentAsync(session, ct).ConfigureAwait(false);
                session.Status = OnboardingStatus.Complete;
                session.CompletedAt = DateTimeOffset.UtcNow;
                RemoveSession(sessionId);

                return OnboardingStepResult.Complete(
                    $"Voice profile created for {session.SpeakerName}. I'll recognize your voice from now on.",
                    profile);
            }

            var progress = (int)(session.CurrentPromptIndex * 100.0 / session.Prompts.Count);
            return OnboardingStepResult.CreateNextPrompt(session.Prompts[session.CurrentPromptIndex], progress);
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public Task<OnboardingSession?> GetSessionAsync(string sessionId, CancellationToken ct)
    {
        _ = ct;
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    private async Task<SpeakerProfile> FinalizeEnrollmentAsync(OnboardingSession session, CancellationToken ct)
    {
        var avgEmbedding = IDiarizationEngine.ComputeAverageEmbedding(session.CollectedEmbeddings);

        if (session.ProvisionalProfileId is not null)
        {
            var existing = await _profileStore.GetAsync(session.ProvisionalProfileId, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                var promoted = existing with
                {
                    Name = session.SpeakerName,
                    IsProvisional = false,
                    IsAuthorized = true,
                    Embeddings = [.. session.CollectedEmbeddings],
                    AverageEmbedding = avgEmbedding,
                    UpdatedAt = DateTimeOffset.UtcNow,
                };

                await _profileStore.UpdateAsync(promoted, ct).ConfigureAwait(false);
                _logger.LogInformation("Promoted provisional profile {Id} to {Name}", promoted.Id, promoted.Name);
                return promoted;
            }
        }

        var profile = new SpeakerProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = session.SpeakerName,
            IsProvisional = false,
            IsAuthorized = true,
            Embeddings = [.. session.CollectedEmbeddings],
            AverageEmbedding = avgEmbedding,
        };

        await _profileStore.CreateAsync(profile, ct).ConfigureAwait(false);
        _logger.LogInformation("Created voice profile {Id} for {Name}", profile.Id, profile.Name);
        return profile;
    }

    private void CleanupAbandonedSessions()
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-1);
        var abandoned = _sessions
            .Where(kvp => kvp.Value.StartedAt < cutoff && kvp.Value.Status != OnboardingStatus.Complete)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in abandoned)
        {
            RemoveSession(key);
        }
    }

    private void RemoveSession(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        _sessionLocks.TryRemove(sessionId, out _);
    }

    private static List<string> SelectPrompts(int count)
    {
        var shuffled = OnboardingPrompts.OrderBy(_ => Random.Shared.Next()).ToList();
        return shuffled.Take(Math.Min(count, shuffled.Count)).ToList();
    }
}
