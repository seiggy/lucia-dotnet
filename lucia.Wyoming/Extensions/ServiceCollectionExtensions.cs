using Microsoft.Extensions.Configuration;
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
using lucia.Wyoming.Telemetry;
using lucia.Wyoming.Wyoming;
using MongoDB.Driver;

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
        builder.Services.Configure<SpeechEnhancementOptions>(
            builder.Configuration.GetSection(SpeechEnhancementOptions.SectionName));
        builder.Services.Configure<GraniteOptions>(
            builder.Configuration.GetSection(GraniteOptions.SectionName));
        builder.Services.Configure<HybridSttOptions>(
            builder.Configuration.GetSection(HybridSttOptions.SectionName));
        builder.Services.Configure<VoiceProfileOptions>(
            builder.Configuration.GetSection(VoiceProfileOptions.SectionName));
        builder.Services.Configure<CommandRoutingOptions>(
            builder.Configuration.GetSection(CommandRoutingOptions.SectionName));

        builder.Services.AddSingleton<ModelCatalogService>();
        builder.Services.AddSingleton<ModelManager>();
        builder.Services.AddSingleton<IModelChangeNotifier>(sp => sp.GetRequiredService<ModelManager>());
        builder.Services.AddSingleton<ModelDownloader>();
        builder.Services.AddSingleton<OnnxProviderDetector>();
        builder.Services.AddSingleton<IBackgroundTaskQueue>(_ => new BackgroundTaskQueue(capacity: 100));
        builder.Services.AddSingleton<BackgroundTaskTracker>();
        builder.Services.AddHostedService<BackgroundTaskProcessor>();
        builder.Services.AddHostedService<ModelStartupValidator>();

        builder.Services.AddSingleton<ISttEngine, HybridSttEngine>();
        builder.Services.AddSingleton<ISttEngine, SherpaSttEngine>();
        builder.Services.AddSingleton<IGraniteEngine, GraniteOnnxEngine>();
        builder.Services.AddSingleton<IVadEngine, SherpaVadEngine>();
        builder.Services.AddSingleton<IWakeWordDetector, SherpaWakeWordDetector>();

        builder.Services.AddSingleton<IDiarizationEngine, SherpaDiarizationEngine>();
        builder.Services.AddSingleton<ISpeechEnhancer, GtcrnSpeechEnhancer>();
        builder.Services.AddSingleton<AudioClipService>();
        builder.Services.AddSingleton<ProfileMergeService>();

        // Use MongoDB-backed store when a MongoDB connection is available; fall back to in-memory.
        // Wrap with Redis cache for fast enrolled profile lookups during diarization.
        var hasMongoDb = builder.Configuration.GetConnectionString("luciaconfig") is not null
            || builder.Configuration.GetConnectionString("luciatraces") is not null
            || builder.Configuration.GetConnectionString("mongodb") is not null;
        var hasRedis = builder.Configuration.GetConnectionString("redis") is not null;

        if (hasMongoDb)
        {
            builder.Services.AddSingleton<MongoSpeakerProfileStore>();
            if (hasRedis)
            {
                builder.Services.AddSingleton<ISpeakerProfileStore>(sp =>
                    new CachedSpeakerProfileStore(
                        sp.GetRequiredService<MongoSpeakerProfileStore>(),
                        sp.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>(),
                        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CachedSpeakerProfileStore>>()));
            }
            else
            {
                builder.Services.AddSingleton<ISpeakerProfileStore>(sp =>
                    sp.GetRequiredService<MongoSpeakerProfileStore>());
            }
            builder.Services.AddSingleton<ITranscriptStore, MongoTranscriptStore>();
        }
        else
        {
            builder.Services.AddSingleton<ISpeakerProfileStore, InMemorySpeakerProfileStore>();
            builder.Services.AddSingleton<ITranscriptStore, InMemoryTranscriptStore>();
        }

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

        builder.Services.AddSingleton<SessionEventBus>();
        builder.Services.AddSingleton<WyomingServiceInfo>();
        builder.Services.AddHostedService<WyomingServer>();
        builder.Services.AddHostedService<ZeroconfAdvertiser>();

        return builder;
    }
}
