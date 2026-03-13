using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using lucia.Agents.Abstractions;
using lucia.Wyoming.Audio;
using lucia.Wyoming.CommandRouting;
using lucia.Wyoming.Diarization;
using lucia.Wyoming.Discovery;
using lucia.Wyoming.Models;
using lucia.Wyoming.Stt;
using lucia.Wyoming.Vad;
using lucia.Wyoming.WakeWord;
using lucia.Wyoming.Wyoming;

namespace lucia.Wyoming.Extensions;

/// <summary>
/// Extension methods for registering Wyoming voice services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add the Wyoming voice protocol server and all voice processing services.
    /// </summary>
    public static IHostApplicationBuilder AddWyomingServer(
        this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<WyomingOptions>(
            builder.Configuration.GetSection(WyomingOptions.SectionName));
        builder.Services.Configure<SttOptions>(
            builder.Configuration.GetSection(SttOptions.SectionName));
        builder.Services.Configure<VadOptions>(
            builder.Configuration.GetSection(VadOptions.SectionName));
        builder.Services.Configure<WakeWordOptions>(
            builder.Configuration.GetSection(WakeWordOptions.SectionName));
        builder.Services.Configure<SttModelOptions>(
            builder.Configuration.GetSection(SttModelOptions.SectionName));
        builder.Services.Configure<DiarizationOptions>(
            builder.Configuration.GetSection(DiarizationOptions.SectionName));
        builder.Services.Configure<VoiceProfileOptions>(
            builder.Configuration.GetSection(VoiceProfileOptions.SectionName));
        builder.Services.Configure<CommandRoutingOptions>(
            builder.Configuration.GetSection(CommandRoutingOptions.SectionName));

        builder.Services.AddSingleton<ModelCatalogService>();
        builder.Services.AddSingleton<ModelManager>();
        builder.Services.AddSingleton<IModelChangeNotifier>(sp => sp.GetRequiredService<ModelManager>());
        builder.Services.AddSingleton<ModelDownloader>();
        builder.Services.AddSingleton<BackgroundTaskService>();

        builder.Services.AddSingleton<ISttEngine, SherpaSttEngine>();
        builder.Services.AddSingleton<IVadEngine, SherpaVadEngine>();
        builder.Services.AddSingleton<IWakeWordDetector, SherpaWakeWordDetector>();

        builder.Services.AddSingleton<IDiarizationEngine, SherpaDiarizationEngine>();
        builder.Services.AddSingleton<ISpeakerProfileStore, InMemorySpeakerProfileStore>();
        builder.Services.AddSingleton<SpeakerVerificationFilter>();
        builder.Services.AddSingleton<AdaptiveProfileUpdater>();
        builder.Services.AddSingleton<UnknownSpeakerTracker>();
        builder.Services.AddHostedService<ProvisionalProfileCleanupService>();

        builder.Services.AddSingleton<CommandPatternRegistry>(sp =>
        {
            var providers = sp.GetServices<ICommandPatternProvider>()
                .Concat(sp.GetServices<IOptimizableSkill>().OfType<ICommandPatternProvider>())
                .Distinct()
                .ToArray();

            return new CommandPatternRegistry(providers);
        });
        builder.Services.AddSingleton<CommandPatternMatcher>();
        builder.Services.AddSingleton<ICommandRouter, CommandPatternRouter>();
        builder.Services.AddSingleton<SkillDispatcher>();

        builder.Services.AddSingleton<AudioQualityAnalyzer>();
        builder.Services.AddSingleton<VoiceOnboardingService>();

        builder.Services.AddSingleton<WakeWordTokenizer>();
        builder.Services.AddSingleton<IWakeWordStore, InMemoryWakeWordStore>();
        builder.Services.AddSingleton<CustomWakeWordManager>();
        builder.Services.AddSingleton<IWakeWordChangeNotifier>(sp => sp.GetRequiredService<CustomWakeWordManager>());

        builder.Services.AddSingleton<WyomingServiceInfo>();
        builder.Services.AddHostedService<WyomingServer>();
        builder.Services.AddHostedService<ZeroconfAdvertiser>();

        return builder;
    }
}
