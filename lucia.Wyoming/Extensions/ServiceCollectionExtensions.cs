using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using lucia.Wyoming.Audio;
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

        builder.Services.AddSingleton<ModelCatalogService>();
        builder.Services.AddSingleton<ModelManager>();
        builder.Services.AddSingleton<IModelChangeNotifier>(sp => sp.GetRequiredService<ModelManager>());
        builder.Services.AddSingleton<ModelDownloader>();

        builder.Services.AddSingleton<ISttEngine, SherpaSttEngine>();
        builder.Services.AddSingleton<IVadEngine, SherpaVadEngine>();
        builder.Services.AddSingleton<IWakeWordDetector, SherpaWakeWordDetector>();

        builder.Services.AddSingleton<WyomingServiceInfo>();
        builder.Services.AddHostedService<WyomingServer>();
        builder.Services.AddHostedService<ZeroconfAdvertiser>();

        return builder;
    }
}
