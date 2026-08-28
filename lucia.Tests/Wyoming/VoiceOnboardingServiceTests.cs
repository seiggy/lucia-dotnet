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
        Assert.Single(_audioClipService.GetClips(session.Id));
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
        Assert.Empty(_audioClipService.GetClips(session.Id));
    }

    [Fact]
    public async Task StartOnboarding_MissingProvisionalProfile_Throws()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.StartOnboardingAsync("Jane", "../../plugins", CancellationToken.None));
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

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProcessSampleAsync(second.Id, audio, 16_000, CancellationToken.None));
        var profile = await store.GetAsync("prov-123", CancellationToken.None);
        Assert.Equal("Alice", profile?.Name);
    }
}
