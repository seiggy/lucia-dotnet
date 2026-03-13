using lucia.Wyoming.Diarization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class VoiceOnboardingServiceTests
{
    private static VoiceOnboardingService CreateService(
        IDiarizationEngine? engine = null,
        ISpeakerProfileStore? store = null)
    {
        return new VoiceOnboardingService(
            engine ?? new TestDiarizationEngine(),
            store ?? new InMemorySpeakerProfileStore(),
            new AudioQualityAnalyzer(Options.Create(new VoiceProfileOptions())),
            Options.Create(new VoiceProfileOptions { OnboardingSampleCount = 3 }),
            NullLogger<VoiceOnboardingService>.Instance);
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
}
