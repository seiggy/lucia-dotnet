using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
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
                    .AddMeter("Lucia.TraceCapture")
                    .AddMeter("Lucia.Skills.LightControl")
                    .AddMeter("Lucia.Skills.MusicPlayback");
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddSource("Lucia.Orchestration")
                    .AddSource("Lucia.TraceCapture")
                    .AddSource("Lucia.Agents.General")
                    .AddSource("Lucia.Agents.Music")
                    .AddSource("Lucia.Skills.LightControl")
                    .AddSource("Lucia.Skills.MusicPlayback")
                    .AddSource("Lucia.Services.EntityLocation")
                    .AddSource("Microsoft.Extensions.AI")
                    .AddSource("Microsoft.Agents.AI*")
                    .AddSource("A2A*")
                    .AddSource("Microsoft.Agents.AI.Hosting*")
                    .AddSource("Microsoft.Agents.AI.Workflows*")
                    .AddSource("Microsoft.Agents.AI.Runtime.InProcess")
                    .AddSource("Microsoft.Agents.AI.Runtime.Abstractions.InMemoryActorStateStorage")
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation(options =>
                    {
                        // Filter out Azure IMDS credential probe requests (noisy locally)
                        options.FilterHttpRequestMessage = request =>
                            request.RequestUri?.Host != "169.254.169.254";

                        options.EnrichWithHttpRequestMessage = (activity, request) =>
                        {
                            if (!IsRecorded(activity))
                            {
                                return;
                            }

                            AddHeaders(activity, "http.request.header.", request.Headers);

                            if (request.Content is null)
                            {
                                return;
                            }

                            AddHeaders(activity, "http.request.content_header.", request.Content.Headers);
                            TrySetBodyTag(activity, "http.request.body", request.Content);
                        };

                        options.EnrichWithHttpResponseMessage = (activity, response) =>
                        {
                            if (!IsRecorded(activity))
                            {
                                return;
                            }

                            AddHeaders(activity, "http.response.header.", response.Headers);

                            // Skip body capture for WebSocket upgrade responses — their
                            // content stream is a duplex network stream that must remain
                            // writeable for the WebSocket to function.
                            if (response.Content is null
                                || response.StatusCode == System.Net.HttpStatusCode.SwitchingProtocols)
                            {
                                return;
                            }

                            AddHeaders(activity, "http.response.content_header.", response.Content.Headers);
                            TrySetBodyTag(activity, "http.response.body", response.Content);
                        };
                    });
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

    private static void TrySetBodyTag(Activity activity, string tagName, HttpContent content)
    {
        try
        {
            // Buffer the content first so downstream code can still read it.
            // LoadIntoBufferAsync is a no-op if already buffered.
            content.LoadIntoBufferAsync().ConfigureAwait(false).GetAwaiter().GetResult();

            // Do NOT dispose the stream — it is owned by HttpContent and will be
            // read again by downstream code (e.g. GetFromJsonAsync).
            var stream = content.ReadAsStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            var payload = reader.ReadToEnd();

            // Reset the stream position so downstream callers can re-read
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

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