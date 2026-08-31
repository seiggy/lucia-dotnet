using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace lucia.Tests.Integration;

public sealed class TelemetryModeTests
{
    [Fact]
    public void Off_DoesNotRegisterTelemetryProviders()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Observability:Mode"] = "Off";

        builder.ConfigureOpenTelemetry();

        Assert.DoesNotContain(builder.Services, service => service.ServiceType == typeof(MeterProvider));
        Assert.DoesNotContain(builder.Services, service => service.ServiceType == typeof(TracerProvider));
    }

    [Fact]
    public void Metrics_RegistersOnlyMeterProvider()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Observability:Mode"] = "Metrics";

        builder.ConfigureOpenTelemetry();

        Assert.Contains(builder.Services, service => service.ServiceType == typeof(MeterProvider));
        Assert.DoesNotContain(builder.Services, service => service.ServiceType == typeof(TracerProvider));
    }

    [Theory]
    [InlineData("Trace")]
    [InlineData("Profile")]
    public void TraceModes_RegisterBothProviders(string mode)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Observability:Mode"] = mode;

        builder.ConfigureOpenTelemetry();

        Assert.Contains(builder.Services, service => service.ServiceType == typeof(MeterProvider));
        Assert.Contains(builder.Services, service => service.ServiceType == typeof(TracerProvider));
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void LegacyEnabledSetting_RemainsCompatible(string enabled, bool expectsProviders)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Observability:Enabled"] = enabled;

        builder.ConfigureOpenTelemetry();

        Assert.Equal(
            expectsProviders,
            builder.Services.Any(service => service.ServiceType == typeof(MeterProvider)));
        Assert.Equal(
            expectsProviders,
            builder.Services.Any(service => service.ServiceType == typeof(TracerProvider)));
    }

    [Fact]
    public void ConflictingSettings_ProduceMigrationError()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Observability:Mode"] = "Metrics";
        builder.Configuration["Observability:Enabled"] = "true";

        var exception = Assert.Throws<InvalidOperationException>(builder.ConfigureOpenTelemetry);

        Assert.Contains("not both", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidMode_ProducesConfigurationError()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Observability:Mode"] = "Verbose";

        var exception = Assert.Throws<InvalidOperationException>(builder.ConfigureOpenTelemetry);

        Assert.Contains("Off, Metrics, Trace, Profile", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreachableCollector_DoesNotDelaySpanProductionOrShutdown()
    {
        using var collector = new TcpListener(IPAddress.Loopback, 0);
        collector.Start();
        var endpoint = (IPEndPoint)collector.LocalEndpoint;

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Observability:Mode"] = "Profile";
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = $"http://127.0.0.1:{endpoint.Port}";
        builder.ConfigureOpenTelemetry();

        var host = builder.Build();
        await host.StartAsync();
        _ = host.Services.GetRequiredService<TracerProvider>();

        using var source = new ActivitySource("lucia.Wyoming.Session");
    var hadListeners = source.HasListeners();
        var productionTimer = Stopwatch.StartNew();
        for (var index = 0; index < 4_096; index++)
        {
            using var activity = source.StartActivity("speech.test");
        }
        productionTimer.Stop();

        var shutdownTimer = Stopwatch.StartNew();
        await host.StopAsync();
        host.Dispose();
        shutdownTimer.Stop();

        Assert.True(hadListeners);
        Assert.True(
            productionTimer.Elapsed < TimeSpan.FromSeconds(2),
            $"Span production took {productionTimer.Elapsed}.");
        Assert.True(
            shutdownTimer.Elapsed < TimeSpan.FromSeconds(3),
            $"Telemetry shutdown took {shutdownTimer.Elapsed}.");
    }

    [Fact]
    public async Task NestedOtlpEndpoint_ConnectsToCollector()
    {
        using var collector = new TcpListener(IPAddress.Loopback, 0);
        collector.Start();
        var endpoint = (IPEndPoint)collector.LocalEndpoint;
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Observability:Mode"] = "Metrics";
        builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] = "";
        builder.Configuration["Observability:OtlpEndpoint"] =
            $"http://127.0.0.1:{endpoint.Port}";
        builder.ConfigureOpenTelemetry();

        using var host = builder.Build();
        await host.StartAsync();
        var provider = host.Services.GetRequiredService<MeterProvider>();
        using var meter = new Meter("lucia.Wyoming.SpeechPipeline");
        meter.CreateCounter<long>("telemetry.connection.test").Add(1);
        var acceptTask = collector.AcceptTcpClientAsync();

        provider.ForceFlush();

        using var connection = await acceptTask.WaitAsync(TimeSpan.FromSeconds(2));
        await host.StopAsync();
    }

    [Fact]
    public void Profile_AddsCaptureCorrelationResourceMetadata()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Observability:Mode"] = "Profile";
        var exporter = new ResourceCapturingActivityExporter();

        builder.ConfigureOpenTelemetry();
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddProcessor(new SimpleActivityExportProcessor(exporter)));

        using var host = builder.Build();
        var provider = host.Services.GetRequiredService<TracerProvider>();
        using var source = new ActivitySource("lucia.Wyoming.Session");
        using (source.StartActivity("speech.profile"))
        {
        }
        provider.ForceFlush();

        Assert.Equal(builder.Environment.ApplicationName, exporter.ResourceAttributes["service.name"]);
        Assert.Equal("Profile", exporter.ResourceAttributes["lucia.telemetry.mode"]);
        Assert.Equal(
            exporter.ResourceAttributes["service.instance.id"],
            exporter.ResourceAttributes["lucia.profile.correlation.id"]);
    }

    [Fact]
    public void Metrics_ExportsSpeechPipelineMeterUnderApplicationResource()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["Observability:Mode"] = "Metrics";
        var exporter = new ResourceCapturingMetricExporter();

        builder.ConfigureOpenTelemetry();
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddReader(new PeriodicExportingMetricReader(exporter)));

        using var host = builder.Build();
        var provider = host.Services.GetRequiredService<MeterProvider>();
        using var meter = new Meter("lucia.Wyoming.SpeechPipeline");
        var duration = meter.CreateHistogram<double>("wyoming.speech.test.duration", "ms");
        duration.Record(12);
        provider.ForceFlush();

        Assert.Contains("wyoming.speech.test.duration", exporter.MetricNames);
        Assert.Equal(builder.Environment.ApplicationName, exporter.ResourceAttributes["service.name"]);
        Assert.Equal("Metrics", exporter.ResourceAttributes["lucia.telemetry.mode"]);
    }
}