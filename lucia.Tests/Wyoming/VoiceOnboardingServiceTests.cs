using FakeItEasy;
using lucia.Wyoming.Diarization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class VoiceOnboardingServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "lucia-onboarding-tests",
        Guid.NewGuid().ToString("N"));
    private readonly AudioClipService _audioClipService;

    public VoiceOnboardingServiceTests()
    {
        var options = new VoiceProfileOptions
        {
            AudioClipBasePath = _tempRoot,
            MaxClipsPerProfile = 5,
            OnboardingSampleCount = 3,
        };
        _audioClipService = new AudioClipService(
            new OptionsMonitorStub<VoiceProfileOptions>(options),
            NullLogger<AudioClipService>.Instance);
    }

    private VoiceOnboardingService CreateService(
        IDiarizationEngine? engine = null,
        ISpeakerProfileStore? store = null)
    {
        var options = new VoiceProfileOptions
        {
            AudioClipBasePath = _tempRoot,
            MaxClipsPerProfile = 5,
            OnboardingSampleCount = 3,
        };

        return new VoiceOnboardingService(
            engine ?? new TestDiarizationEngine(),
            store ?? new InMemorySpeakerProfileStore(),
            new AudioQualityAnalyzer(Options.Create(new VoiceProfileOptions())),
            _audioClipService,
            Options.Create(options),
            NullLogger<VoiceOnboardingService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StartOnboarding_CreatesSession()
    {
        var service = CreateService();

        var session = await service.StartOnboardingAsync("Test User", null, CancellationToken.None);

        Assert.NotNull(session);
        Assert.Equal("Test User", session.SpeakerName);
        Assert.Equal(3, session.Prompts.Count);
    }

    [Fact]
    public async Task ProcessSample_AcceptableAudio_AdvancesPrompt()
    {
        var service = CreateService();
        var session = await service.StartOnboardingAsync("Test", null, CancellationToken.None);
        var audio = new float[32_000];
        Array.Fill(audio, 0.1f);

        var result = await service.ProcessSampleAsync(session.Id, audio, 16_000, CancellationToken.None);

        Assert.Equal(OnboardingStepStatus.NextPrompt, result.Status);
        Assert.Single(Directory.GetFiles(
            GetStagingDirectory(session.Id),
            "*.json"));
        Assert.Empty(_audioClipService.GetClips(session.ProfileId));
    }

    [Fact]
    public async Task ProcessSample_TooQuiet_ReturnsRetry()
    {
        var service = CreateService();
        var session = await service.StartOnboardingAsync("Test", null, CancellationToken.None);
        var audio = new float[32_000];

        var result = await service.ProcessSampleAsync(session.Id, audio, 16_000, CancellationToken.None);

        Assert.Equal(OnboardingStepStatus.Retry, result.Status);
        Assert.Contains("quiet", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessSample_TooShort_ReturnsRetry()
    {
        var service = CreateService();
        var session = await service.StartOnboardingAsync("Test", null, CancellationToken.None);
        var audio = new float[8_000];
        Array.Fill(audio, 0.1f);

        var result = await service.ProcessSampleAsync(session.Id, audio, 16_000, CancellationToken.None);

        Assert.Equal(OnboardingStepStatus.Retry, result.Status);
    }

    [Fact]
    public async Task AllSamples_CompletesEnrollment()
    {
        var store = new InMemorySpeakerProfileStore();
        var service = CreateService(store: store);
        var session = await service.StartOnboardingAsync("Jane", null, CancellationToken.None);

        OnboardingStepResult result = null!;

        for (var i = 0; i < 3; i++)
        {
            var audio = new float[32_000];
            Array.Fill(audio, 0.1f + (i * 0.01f));
            result = await service.ProcessSampleAsync(session.Id, audio, 16_000, CancellationToken.None);
        }

        Assert.Equal(OnboardingStepStatus.Complete, result.Status);
        Assert.NotNull(result.CompletedProfile);
        Assert.Equal("Jane", result.CompletedProfile.Name);

        var profiles = await store.GetEnrolledProfilesAsync(CancellationToken.None);
        Assert.Single(profiles);
        var completedSession = await service.GetSessionAsync(session.Id, CancellationToken.None);
        Assert.NotNull(completedSession);
        Assert.Equal(OnboardingStatus.Complete, completedSession.Status);
        Assert.Equal(3, _audioClipService.GetClips(result.CompletedProfile.Id).Count);
        Assert.False(Directory.Exists(GetStagingDirectory(session.Id)));
    }

    [Fact]
    public async Task StartOnboarding_MissingProvisionalProfile_Throws()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.StartOnboardingAsync("Jane", "../../plugins", CancellationToken.None));
    }

    [Fact]
    public async Task StartOnboarding_RemovesAbandonedSessionRecordings()
    {
        var service = CreateService();
        var abandoned = await service.StartOnboardingAsync("Jane", null, CancellationToken.None);
        var audio = new float[32_000];
        Array.Fill(audio, 0.1f);
        await service.ProcessSampleAsync(abandoned.Id, audio, 16_000, CancellationToken.None);
        abandoned.LastActivityAt = DateTimeOffset.UtcNow.AddHours(-2);

        await service.StartOnboardingAsync("Alice", null, CancellationToken.None);

        Assert.Null(await service.GetSessionAsync(abandoned.Id, CancellationToken.None));
        Assert.False(Directory.Exists(GetStagingDirectory(abandoned.Id)));
    }

    [Fact]
    public async Task StartOnboarding_ContinuesWhenAbandonedCleanupFails()
    {
        var store = A.Fake<ISpeakerProfileStore>();
        A.CallTo(() => store.GetAsync(A<string>._, A<CancellationToken>._))
            .ThrowsAsync(new IOException("cleanup failed"));
        var service = CreateService(store: store);
        var abandoned = await service.StartOnboardingAsync("Jane", null, CancellationToken.None);
        abandoned.ProfilePersisted = true;
        abandoned.LastActivityAt = DateTimeOffset.UtcNow.AddHours(-2);

        var session = await service.StartOnboardingAsync("Alice", null, CancellationToken.None);

        Assert.Equal("Alice", session.SpeakerName);
        Assert.NotNull(await service.GetSessionAsync(abandoned.Id, CancellationToken.None));
    }

    [Fact]
    public async Task StartOnboarding_RemovesStaleSessionWhenPersistedProfileWasDeleted()
    {
        var service = CreateService();
        var abandoned = await service.StartOnboardingAsync("Jane", null, CancellationToken.None);
        var audio = new float[32_000];
        Array.Fill(audio, 0.1f);
        await service.ProcessSampleAsync(abandoned.Id, audio, 16_000, CancellationToken.None);
        abandoned.ProfilePersisted = true;
        abandoned.LastActivityAt = DateTimeOffset.UtcNow.AddHours(-2);

        await service.StartOnboardingAsync("Alice", null, CancellationToken.None);

        Assert.Null(await service.GetSessionAsync(abandoned.Id, CancellationToken.None));
        Assert.False(Directory.Exists(GetStagingDirectory(abandoned.Id)));
    }

    [Fact]
    public async Task StartOnboarding_DoesNotSwallowCleanupCancellation()
    {
        var service = CreateService();
        await service.StartOnboardingAsync("Jane", null, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.StartOnboardingAsync("Alice", null, cancellation.Token));
    }

    [Fact]
    public async Task BackgroundService_RemovesStagingClipsFromInterruptedProcess()
    {
        await _audioClipService.SaveOnboardingClipAsync(
            "orphan",
            Enumerable.Repeat(0.1f, 32_000).ToArray(),
            16_000,
            "orphan");

        var service = CreateService();
        await service.StartAsync(CancellationToken.None);

        Assert.True(SpinWait.SpinUntil(
            () => !Directory.Exists(GetStagingDirectory("orphan")),
            TimeSpan.FromSeconds(1)));
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task BackgroundService_RecoversClipsAfterProfilePersistence()
    {
        var store = new InMemorySpeakerProfileStore();
        await store.CreateAsync(
            new SpeakerProfile
            {
                Id = "profile-1",
                Name = "Jane",
                IsProvisional = false,
                EnrollmentSessionId = "interrupted",
            },
            CancellationToken.None);
        await _audioClipService.SaveOnboardingClipAsync(
            "interrupted",
            Enumerable.Repeat(0.1f, 32_000).ToArray(),
            16_000,
            "accepted");
        await _audioClipService.SaveOnboardingPromotionMarkerAsync(
            "interrupted",
            "profile-1",
            CancellationToken.None);
        var service = CreateService(store: store);

        await service.StartAsync(CancellationToken.None);

        Assert.True(SpinWait.SpinUntil(
            () => _audioClipService.GetClips("profile-1").Count == 1,
            TimeSpan.FromSeconds(1)));
        Assert.False(Directory.Exists(GetStagingDirectory("interrupted")));
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ProvisionalProfile_IsPromoted()
    {
        var store = new InMemorySpeakerProfileStore();
        var provisional = new SpeakerProfile
        {
            Id = "prov-123",
            Name = "Unknown 1",
            IsProvisional = true,
            IsAuthorized = false,
            AverageEmbedding = new float[128],
        };

        await store.CreateAsync(provisional, CancellationToken.None);

        var service = CreateService(store: store);
        var session = await service.StartOnboardingAsync("Bob", "prov-123", CancellationToken.None);

        for (var i = 0; i < 3; i++)
        {
            var audio = new float[32_000];
            Array.Fill(audio, 0.1f);
            await service.ProcessSampleAsync(session.Id, audio, 16_000, CancellationToken.None);
        }

        var updated = await store.GetAsync("prov-123", CancellationToken.None);

        Assert.NotNull(updated);
        Assert.False(updated.IsProvisional);
        Assert.Equal("Bob", updated.Name);
        Assert.True(updated.IsAuthorized);
    }

    [Fact]
    public async Task DeletedProvisionalProfile_IsNotRecreated()
    {
        var store = new InMemorySpeakerProfileStore();
        await store.CreateAsync(
            new SpeakerProfile
            {
                Id = "prov-123",
                Name = "Unknown",
                IsProvisional = true,
                IsAuthorized = false,
                AverageEmbedding = new float[128],
            },
            CancellationToken.None);
        var service = CreateService(store: store);
        var session = await service.StartOnboardingAsync("Mallory", "prov-123", CancellationToken.None);
        await store.DeleteAsync("prov-123", CancellationToken.None);
        var audio = new float[32_000];
        Array.Fill(audio, 0.1f);

        for (var i = 0; i < 2; i++)
        {
            await service.ProcessSampleAsync(session.Id, audio, 16_000, CancellationToken.None);
        }

        await Assert.ThrowsAsync<OnboardingConflictException>(
            () => service.ProcessSampleAsync(session.Id, audio, 16_000, CancellationToken.None));
        Assert.Null(await store.GetAsync("prov-123", CancellationToken.None));
        Assert.False(Directory.Exists(GetStagingDirectory(session.Id)));
        Assert.Equal(
            OnboardingStatus.Failed,
            (await service.GetSessionAsync(session.Id, CancellationToken.None))?.Status);
    }

    [Fact]
    public async Task ConcurrentSessions_CannotPromoteSameProfileTwice()
    {
        var store = new InMemorySpeakerProfileStore();
        await store.CreateAsync(
            new SpeakerProfile
            {
                Id = "prov-123",
                Name = "Unknown",
                IsProvisional = true,
                IsAuthorized = false,
                AverageEmbedding = new float[128],
            },
            CancellationToken.None);
        var service = CreateService(store: store);
        var first = await service.StartOnboardingAsync("Alice", "prov-123", CancellationToken.None);
        var second = await service.StartOnboardingAsync("Mallory", "prov-123", CancellationToken.None);
        var audio = new float[32_000];
        Array.Fill(audio, 0.1f);

        for (var i = 0; i < 3; i++)
        {
            await service.ProcessSampleAsync(first.Id, audio, 16_000, CancellationToken.None);
        }
        for (var i = 0; i < 2; i++)
        {
            await service.ProcessSampleAsync(second.Id, audio, 16_000, CancellationToken.None);
        }

        await Assert.ThrowsAsync<OnboardingConflictException>(
            () => service.ProcessSampleAsync(second.Id, audio, 16_000, CancellationToken.None));
        var profile = await store.GetAsync("prov-123", CancellationToken.None);
        Assert.Equal("Alice", profile?.Name);
        Assert.False(Directory.Exists(GetStagingDirectory(second.Id)));
        Assert.Equal(
            OnboardingStatus.Failed,
            (await service.GetSessionAsync(second.Id, CancellationToken.None))?.Status);
    }

    [Fact]
    public async Task Enrollment_RecoversWhenCreateCommitsThenThrows()
    {
        var innerStore = new InMemorySpeakerProfileStore();
        var store = A.Fake<ISpeakerProfileStore>();
        A.CallTo(() => store.GetAsync(A<string>._, A<CancellationToken>._))
            .ReturnsLazily(call => innerStore.GetAsync(
                call.GetArgument<string>(0)!,
                call.GetArgument<CancellationToken>(1)));
        A.CallTo(() => store.CreateAsync(A<SpeakerProfile>._, A<CancellationToken>._))
            .ReturnsLazily(async call =>
            {
                await innerStore.CreateAsync(
                    call.GetArgument<SpeakerProfile>(0)!,
                    call.GetArgument<CancellationToken>(1));
                throw new IOException("connection dropped after commit");
            });
        var service = CreateService(store: store);
        var session = await service.StartOnboardingAsync("Jane", null, CancellationToken.None);
        var audio = new float[32_000];
        Array.Fill(audio, 0.1f);

        OnboardingStepResult? result = null;
        for (var index = 0; index < 3; index++)
        {
            result = await service.ProcessSampleAsync(session.Id, audio, 16000, CancellationToken.None);
        }

        Assert.Equal(OnboardingStepStatus.Complete, result?.Status);
        Assert.True(session.ProfilePersisted);
        Assert.Equal(3, _audioClipService.GetClips(session.ProfileId).Count);
    }

    [Fact]
    public async Task Enrollment_MergeClaimReturnsOnboardingConflict()
    {
        var store = new InMemorySpeakerProfileStore();
        await store.CreateAsync(
            new SpeakerProfile
            {
                Id = "prov-123",
                Name = "Unknown",
                IsProvisional = true,
                IsAuthorized = false,
                AverageEmbedding = new float[128],
            },
            CancellationToken.None);
        var service = CreateService(store: store);
        var session = await service.StartOnboardingAsync("Jane", "prov-123", CancellationToken.None);
        await store.UpdateAtomicAsync(
            "prov-123",
            profile => profile with { MergeTargetProfileId = "target" },
            CancellationToken.None);
        var audio = new float[32_000];
        Array.Fill(audio, 0.1f);
        for (var index = 0; index < 2; index++)
        {
            await service.ProcessSampleAsync(session.Id, audio, 16000, CancellationToken.None);
        }

        await Assert.ThrowsAsync<OnboardingConflictException>(
            () => service.ProcessSampleAsync(session.Id, audio, 16000, CancellationToken.None));

        Assert.Equal(OnboardingStatus.Failed, session.Status);
        Assert.False(Directory.Exists(GetStagingDirectory(session.Id)));
    }

    private string GetStagingDirectory(string sessionId) =>
        Path.Combine(_tempRoot, ".onboarding-staging", sessionId);
}
