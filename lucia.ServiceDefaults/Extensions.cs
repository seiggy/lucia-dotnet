using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common .NET Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

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
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("lucia.TraceCapture")
                    .AddMeter("lucia.Skills.LightControl")
                    .AddMeter("lucia.Skills.MusicPlayback")
                    .AddMeter("lucia.Wyoming.BackgroundTasks")
                    .AddMeter("Microsoft.Agents.AI");
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
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
                    .AddSource("lucia.Wyoming.BackgroundTasks")
                    .AddSource("MongoDB.Driver.Core.Extensions.DiagnosticSources")
                    .AddRedisInstrumentation()
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    );
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
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
        // All health checks must pass for app to be considered ready to accept traffic after starting
        app.MapHealthChecks(HealthEndpointPath);

        // Only health checks tagged with the "live" tag must pass for app to be considered alive
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

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