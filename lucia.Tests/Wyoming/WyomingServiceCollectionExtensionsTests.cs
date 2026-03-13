using lucia.Wyoming.CommandRouting;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Extensions;
using lucia.Wyoming.WakeWord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace lucia.Tests.Wyoming;

public sealed class WyomingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddWyomingServer_RegistersPhase2ServicesAndOptions()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Wyoming:Diarization:Enabled"] = "true",
                ["Wyoming:Diarization:SpeakerThreshold"] = "0.7",
                ["Wyoming:VoiceProfiles:AdaptiveProfiles"] = "true",
                ["Wyoming:VoiceProfiles:AdaptiveAlpha"] = "0.05",
                ["Wyoming:CommandRouting:Enabled"] = "true",
                ["Wyoming:CommandRouting:ConfidenceThreshold"] = "0.8",
            });

        builder.AddWyomingServer();

        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IDiarizationEngine)
                && descriptor.ImplementationType == typeof(SherpaDiarizationEngine));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(ISpeakerProfileStore)
                && descriptor.ImplementationType == typeof(InMemorySpeakerProfileStore));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(SpeakerVerificationFilter)
                && descriptor.ImplementationType == typeof(SpeakerVerificationFilter));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(AdaptiveProfileUpdater)
                && descriptor.ImplementationType == typeof(AdaptiveProfileUpdater));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(UnknownSpeakerTracker)
                && descriptor.ImplementationType == typeof(UnknownSpeakerTracker));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(ProvisionalProfileCleanupService));

        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(CommandPatternRegistry)
                && descriptor.ImplementationType == typeof(CommandPatternRegistry));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(CommandPatternMatcher)
                && descriptor.ImplementationType == typeof(CommandPatternMatcher));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(ICommandRouter)
                && descriptor.ImplementationType == typeof(CommandPatternRouter));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(SkillDispatcher)
                && descriptor.ImplementationType == typeof(SkillDispatcher));

        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(AudioQualityAnalyzer)
                && descriptor.ImplementationType == typeof(AudioQualityAnalyzer));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(VoiceOnboardingService)
                && descriptor.ImplementationType == typeof(VoiceOnboardingService));

        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(WakeWordTokenizer)
                && descriptor.ImplementationType == typeof(WakeWordTokenizer));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IWakeWordStore)
                && descriptor.ImplementationType == typeof(InMemoryWakeWordStore));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(CustomWakeWordManager)
                && descriptor.ImplementationType == typeof(CustomWakeWordManager));

        using var serviceProvider = builder.Services.BuildServiceProvider();
        var diarizationOptions = serviceProvider.GetRequiredService<IOptions<DiarizationOptions>>().Value;
        var voiceProfileOptions = serviceProvider.GetRequiredService<IOptions<VoiceProfileOptions>>().Value;
        var commandRoutingOptions = serviceProvider.GetRequiredService<IOptions<CommandRoutingOptions>>().Value;

        Assert.True(diarizationOptions.Enabled);
        Assert.Equal(0.7f, diarizationOptions.SpeakerThreshold);
        Assert.True(voiceProfileOptions.AdaptiveProfiles);
        Assert.Equal(0.05f, voiceProfileOptions.AdaptiveAlpha);
        Assert.True(commandRoutingOptions.Enabled);
        Assert.Equal(0.8f, commandRoutingOptions.ConfidenceThreshold);
    }
}
