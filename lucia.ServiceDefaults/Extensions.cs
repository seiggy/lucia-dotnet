using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common .NET Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";
    private const int ExportTimeoutMilliseconds = 250;
    private const int MaxExportBatchSize = 128;
    private const int MaxExportQueueSize = 512;
    private const int MetricExportIntervalMilliseconds = 30_000;
    private const int ScheduledExportDelayMilliseconds = 1_000;

    private enum TelemetryMode
    {
        Off,
        Metrics,
        Trace,
        Profile,
    }

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddOutputCache(options =>
        {
            // Named policy applied only to /health and /alive — not a base policy, so no other
            // eligible GET endpoints are cached. The default policy backing this named policy only
            // stores 200 responses; appending HealthCheckOutputCachePolicy also caches the 503 a
            // degraded/unhealthy health report returns, so degraded-state polling still hits the
            // cache instead of re-running expensive checks.
            options.AddPolicy("health-checks", policy =>
                policy.Expire(TimeSpan.FromSeconds(10)).AddPolicy<HealthCheckOutputCachePolicy>());
        });

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default with extended timeouts for LLM/agent workloads.
            // Ollama chat and agent proxy calls can exceed the standard 30s total timeout.
            http.AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3);
                // CircuitBreaker.SamplingDuration must be at least 2x AttemptTimeout
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
            });

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        builder.Services.AddHttpClient()
            .AddLogging();

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var mode = ResolveTelemetryMode(builder);
        if (mode is TelemetryMode.Off)
        {
            return builder;
        }

        var otlpEndpoint = ResolveOtlpEndpoint(builder);
        var otlpHeaders = builder.Configuration["OTEL_EXPORTER_OTLP_HEADERS"]
            ?? builder.Configuration["Observability:OtlpHeaders"];
        var useOtlpExporter = otlpEndpoint is not null;
        var telemetry = builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddMeter("lucia.TraceCapture")
                    .AddMeter("lucia.Skills.LightControl")
                    .AddMeter("lucia.Skills.MusicPlayback")
                    .AddMeter("lucia.Wyoming.SpeechPipeline")
                    .AddMeter("lucia.Wyoming.BackgroundTasks")
                    .AddMeter("Microsoft.Agents.AI");

                if (useOtlpExporter)
                {
                    metrics.AddOtlpExporter((exporter, reader) =>
                    {
                        ConfigureOtlpExporter(exporter, otlpEndpoint!, otlpHeaders);
                        exporter.TimeoutMilliseconds = ExportTimeoutMilliseconds;
                        reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds =
                            MetricExportIntervalMilliseconds;
                        reader.PeriodicExportingMetricReaderOptions.ExportTimeoutMilliseconds =
                            ExportTimeoutMilliseconds;
                    });
                }
            });

        var serviceInstanceId = $"{Environment.MachineName}:{Environment.ProcessId}";
        telemetry.ConfigureResource(resource =>
        {
            resource.AddService(
                builder.Configuration["OTEL_SERVICE_NAME"] ?? builder.Environment.ApplicationName,
                serviceInstanceId: serviceInstanceId);
            resource.AddAttributes(
            [
                new KeyValuePair<string, object>("lucia.telemetry.mode", mode.ToString()),
            ]);

            if (mode is TelemetryMode.Profile)
            {
                resource.AddAttributes(
                [
                    new KeyValuePair<string, object>("lucia.profile.correlation.id", serviceInstanceId),
                ]);
            }
        });

        if (mode is TelemetryMode.Trace or TelemetryMode.Profile)
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;

                if (useOtlpExporter)
                {
                    logging.AddOtlpExporter((exporter, processor) =>
                    {
                        ConfigureOtlpExporter(exporter, otlpEndpoint!, otlpHeaders);
                        exporter.TimeoutMilliseconds = ExportTimeoutMilliseconds;
                        processor.BatchExportProcessorOptions.ExporterTimeoutMilliseconds =
                            ExportTimeoutMilliseconds;
                        processor.BatchExportProcessorOptions.MaxExportBatchSize = MaxExportBatchSize;
                        processor.BatchExportProcessorOptions.MaxQueueSize = MaxExportQueueSize;
                        processor.BatchExportProcessorOptions.ScheduledDelayMilliseconds =
                            ScheduledExportDelayMilliseconds;
                    });
                }
            });

            telemetry.WithTracing(tracing =>
            {
                tracing.SetSampler(mode is TelemetryMode.Profile
                        ? new AlwaysOnSampler()
                        : new ParentBasedSampler(new TraceIdRatioBasedSampler(0.1)))
                    .AddSource(builder.Environment.ApplicationName)
                    .AddSource("lucia")
                    .AddSource("lucia.Agents")
                    .AddSource("lucia.Orchestration")
                    .AddSource("lucia.TraceCapture")
                    .AddSource("lucia.RouterCache")
                    .AddSource("lucia.ChatCache")
                    .AddSource("lucia.Services.PromptCache")
                    .AddSource("lucia.AgentInvoker")
                    .AddSource("lucia.AgentDispatch")
                    .AddSource("lucia.Agents.General")
                    .AddSource("lucia.Agents.Music")
                    .AddSource("lucia.Skills.LightControl")
                    .AddSource("lucia.Skills.MusicPlayback")
                    .AddSource("lucia.Services.EntityLocation")
                    .AddSource("Microsoft.Extensions.AI")
                    .AddSource("Microsoft.Extensions.Agents*")
                    .AddSource("Microsoft.Agents.AI*")
                    .AddSource("A2A*")
                    .AddSource("Microsoft.Agents.AI.Hosting*")
                    .AddSource("Microsoft.Agents.AI.Workflows*")
                    .AddSource("Microsoft.Agents.AI.Runtime.InProcess")
                    .AddSource("Microsoft.Agents.AI.Runtime.Abstractions.InMemoryActorStateStorage")
                    .AddSource("lucia.Wyoming.Session")
                    .AddSource("lucia.Wyoming.BackgroundTasks")
                    .AddSource("MongoDB.Driver.Core.Extensions.DiagnosticSources")
                    .AddRedisInstrumentation()
                    .AddAspNetCoreInstrumentation(tracing =>
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath));

                if (useOtlpExporter)
                {
                    tracing.AddOtlpExporter(exporter =>
                    {
                        ConfigureOtlpExporter(exporter, otlpEndpoint!, otlpHeaders);
                        exporter.TimeoutMilliseconds = ExportTimeoutMilliseconds;
                        exporter.ExportProcessorType = ExportProcessorType.Batch;
                        exporter.BatchExportProcessorOptions.ExporterTimeoutMilliseconds =
                            ExportTimeoutMilliseconds;
                        exporter.BatchExportProcessorOptions.MaxExportBatchSize = MaxExportBatchSize;
                        exporter.BatchExportProcessorOptions.MaxQueueSize = MaxExportQueueSize;
                        exporter.BatchExportProcessorOptions.ScheduledDelayMilliseconds =
                            ScheduledExportDelayMilliseconds;
                    });
                }
            });
        }

        return builder;
    }

    private static Uri? ResolveOtlpEndpoint(IHostApplicationBuilder builder)
    {
        var configuredEndpoint =
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(configuredEndpoint))
        {
            configuredEndpoint =
                builder.Configuration["Observability:OtlpEndpoint"];
        }
        if (string.IsNullOrWhiteSpace(configuredEndpoint))
        {
            return null;
        }

        if (!Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "OTLP endpoint must be an absolute HTTP or HTTPS URI.");
        }

        return endpoint;
    }

    private static void ConfigureOtlpExporter(
        OtlpExporterOptions exporter,
        Uri endpoint,
        string? headers)
    {
        exporter.Endpoint = endpoint;
        if (!string.IsNullOrWhiteSpace(headers))
        {
            exporter.Headers = headers;
        }
    }

    private static TelemetryMode ResolveTelemetryMode(IHostApplicationBuilder builder)
    {
        var configuredMode = builder.Configuration["Observability:Mode"];
        var legacyEnabled = builder.Configuration["Observability:Enabled"];

        if (!string.IsNullOrWhiteSpace(configuredMode) && !string.IsNullOrWhiteSpace(legacyEnabled))
        {
            throw new InvalidOperationException(
                "Configure either Observability:Mode or the legacy Observability:Enabled setting, not both.");
        }

        if (!string.IsNullOrWhiteSpace(configuredMode))
        {
            return Enum.TryParse<TelemetryMode>(configuredMode, ignoreCase: true, out var mode)
                ? mode
                : throw new InvalidOperationException(
                    $"Observability:Mode must be one of {string.Join(", ", Enum.GetNames<TelemetryMode>())}.");
        }

        if (!string.IsNullOrWhiteSpace(legacyEnabled))
        {
            return bool.TryParse(legacyEnabled, out var enabled)
                ? enabled ? TelemetryMode.Trace : TelemetryMode.Off
                : throw new InvalidOperationException("Observability:Enabled must be true or false.");
        }

        return TelemetryMode.Trace;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Apply output cache middleware before mapping health endpoints
        app.UseOutputCache();

        // All health checks must pass for app to be considered ready to accept traffic after starting
        app.MapHealthChecks(HealthEndpointPath)
            .CacheOutput("health-checks");

        // Only health checks tagged with the "live" tag must pass for app to be considered alive
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        }).CacheOutput("health-checks");

        return app;
    }

    private static bool IsRecorded(Activity activity)
        => (activity.ActivityTraceFlags & ActivityTraceFlags.Recorded) != 0;

    private static void AddHeaders(Activity activity, string prefix, HttpHeaders headers)
    {
        foreach (var header in headers)
        {
            var values = string.Join(",", header.Value);
            if (values.Length == 0)
            {
                continue;
            }

            activity.SetTag(prefix + header.Key.ToLowerInvariant(), values);
        }
    }

    private const long MaxBodyCaptureBytes = 256 * 1024; // 256 KB

    private static void TrySetBodyTag(Activity activity, string tagName, HttpContent content)
    {
        try
        {
            // Skip body capture for large or streaming responses (e.g. model downloads).
            var contentLength = content.Headers.ContentLength;
            if (contentLength is null or > MaxBodyCaptureBytes)
            {
                return;
            }

            // ReadAsStream() on unbuffered HttpConnectionResponseContent consumes the
            // network stream, making it unreadable by downstream code. Catch that case
            // and skip body capture — this is best-effort telemetry, not critical.
            Stream stream;
            try
            {
                stream = content.ReadAsStream();
            }
            catch (InvalidOperationException)
            {
                // Stream already consumed by a prior reader — nothing to capture
                return;
            }

            if (!stream.CanSeek)
            {
                return;
            }

            var position = stream.Position;
            using var reader = new StreamReader(stream, leaveOpen: true);
            var payload = reader.ReadToEnd();

            // Reset the stream position so downstream callers can re-read
            stream.Position = position;

            if (!string.IsNullOrWhiteSpace(payload))
            {
                activity.SetTag(tagName, payload);
            }
        }
        catch (Exception ex)
        {
            activity.SetTag($"{tagName}.error", ex.Message);
        }
    }
}