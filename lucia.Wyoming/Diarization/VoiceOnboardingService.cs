using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Wyoming.Diarization;

public sealed class VoiceOnboardingService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
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
    private readonly AudioClipService _audioClipService;
    private readonly VoiceProfileOptions _options;
    private readonly ILogger<VoiceOnboardingService> _logger;

    public VoiceOnboardingService(
        IDiarizationEngine diarization,
        ISpeakerProfileStore profileStore,
        AudioQualityAnalyzer qualityAnalyzer,
        AudioClipService audioClipService,
        IOptions<VoiceProfileOptions> options,
        ILogger<VoiceOnboardingService> logger)
    {
        _diarization = diarization;
        _profileStore = profileStore;
        _qualityAnalyzer = qualityAnalyzer;
        _audioClipService = audioClipService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OnboardingSession> StartOnboardingAsync(
        string speakerName,
        string? provisionalProfileId,
        CancellationToken ct)
    {
        await CleanupAbandonedSessionsAsync(ct).ConfigureAwait(false);

        if (provisionalProfileId is not null)
        {
            var provisionalProfile = await _profileStore.GetAsync(provisionalProfileId, ct).ConfigureAwait(false);
            if (provisionalProfile is not { IsProvisional: true })
            {
                throw new KeyNotFoundException($"Provisional profile '{provisionalProfileId}' was not found.");
            }
        }

        var sampleCount = _options.OnboardingSampleCount;
        var prompts = SelectPrompts(sampleCount);

        var session = new OnboardingSession
        {
            Id = Guid.NewGuid().ToString("N"),
            ProfileId = provisionalProfileId ?? Guid.NewGuid().ToString("N"),
            SpeakerName = speakerName,
            ProvisionalProfileId = provisionalProfileId,
            Prompts = prompts,
        };

        _sessions.TryAdd(session.Id, session);
        _logger.LogInformation("Started onboarding session {SessionId} for {Name}", session.Id, speakerName);

        return session;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _audioClipService.DeleteOnboardingStagingClips();
        using var timer = new PeriodicTimer(CleanupInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await CleanupAbandonedSessionsAsync(stoppingToken).ConfigureAwait(false);
                _audioClipService.DeleteOnboardingStagingClips(
                    _sessions.Keys.ToHashSet(StringComparer.Ordinal));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Deferring failed onboarding recording cleanup");
            }
        }
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
            session.LastActivityAt = DateTimeOffset.UtcNow;

            if (session.CurrentPromptIndex >= session.Prompts.Count)
            {
                return await CompleteEnrollmentAsync(session, ct).ConfigureAwait(false);
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
            await _audioClipService.SaveOnboardingClipAsync(
                session.Id,
                audioSamples,
                sampleRate,
                session.Prompts[session.CurrentPromptIndex],
                ct).ConfigureAwait(false);
            session.CollectedEmbeddings.Add(embedding.Vector);
            session.CurrentPromptIndex++;

            if (session.CurrentPromptIndex >= session.Prompts.Count)
            {
                return await CompleteEnrollmentAsync(session, ct).ConfigureAwait(false);
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
            var promoted = await _profileStore.UpdateAtomicAsync(
                session.ProvisionalProfileId,
                existing =>
                {
                    if (!existing.IsProvisional)
                    {
                        throw new InvalidOperationException(
                            $"Profile '{existing.Id}' is no longer provisional.");
                    }

                    return existing with
                    {
                        Name = session.SpeakerName,
                        IsProvisional = false,
                        IsAuthorized = true,
                        Embeddings = [.. session.CollectedEmbeddings],
                        AverageEmbedding = avgEmbedding,
                        UpdatedAt = DateTimeOffset.UtcNow,
                    };
                },
                ct).ConfigureAwait(false);
            if (promoted is not null)
            {
                _logger.LogInformation("Promoted provisional profile {Id} to {Name}", promoted.Id, promoted.Name);
                return promoted;
            }
        }

        var profile = new SpeakerProfile
        {
            Id = session.ProfileId,
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

    private async Task<OnboardingStepResult> CompleteEnrollmentAsync(
        OnboardingSession session,
        CancellationToken ct)
    {
        SpeakerProfile profile;
        if (session.ProfilePersisted)
        {
            profile = await _profileStore.GetAsync(session.ProfileId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Persisted profile '{session.ProfileId}' was not found.");
        }
        else
        {
            profile = await FinalizeEnrollmentAsync(session, ct).ConfigureAwait(false);
            session.ProfilePersisted = true;
        }

        await _audioClipService.MoveOnboardingClipsAsync(
            session.Id,
            profile.Id,
            ct).ConfigureAwait(false);
        session.Status = OnboardingStatus.Complete;
        session.CompletedAt = DateTimeOffset.UtcNow;

        return OnboardingStepResult.Complete(
            $"Voice profile created for {session.SpeakerName}. I'll recognize your voice from now on.",
            profile);
    }

    private async Task CleanupAbandonedSessionsAsync(CancellationToken ct)
    {
        var abandonedCutoff = DateTimeOffset.UtcNow.AddHours(-1);
        var completedCutoff = DateTimeOffset.UtcNow.AddMinutes(-5);

        foreach (var key in _sessions.Keys)
        {
            var sessionLock = _sessionLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
            var removeLock = false;
            await sessionLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!_sessions.TryGetValue(key, out var session))
                {
                    removeLock = true;
                }
                else
                {
                    var isStale = session.Status == OnboardingStatus.Complete
                        ? session.CompletedAt < completedCutoff
                        : session.LastActivityAt < abandonedCutoff;
                    if (!isStale)
                    {
                        continue;
                    }

                    if (session.Status != OnboardingStatus.Complete)
                    {
                        _audioClipService.DeleteOnboardingSessionClips(session.Id);
                    }

                    _sessions.TryRemove(key, out _);
                    removeLock = true;
                }
            }
            finally
            {
                sessionLock.Release();
            }

            if (removeLock)
            {
                _sessionLocks.TryRemove(key, out _);
            }
        }
    }

    private static List<string> SelectPrompts(int count)
    {
        var shuffled = OnboardingPrompts.OrderBy(_ => Random.Shared.Next()).ToList();
        return shuffled.Take(Math.Min(count, shuffled.Count)).ToList();
    }

}
