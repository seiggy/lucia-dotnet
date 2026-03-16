using System.Text.Json;
using lucia.Wyoming.Wyoming;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Apis;

/// <summary>
/// REST + SSE endpoints for real-time Wyoming session monitoring.
/// </summary>
public static class WyomingSessionApi
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void MapWyomingSessionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/wyoming/sessions")
            .WithTags("Wyoming Sessions");

        group.MapGet("/", ([FromServices] WyomingServer server) =>
        {
            return Results.Ok(new
            {
                ActiveSessions = server.ActiveSessionCount,
            });
        }).WithName("GetWyomingSessions");

        group.MapGet("/live", StreamSessionEventsAsync)
            .WithName("StreamWyomingSessions");
    }

    /// <summary>
    /// SSE endpoint streaming Wyoming session lifecycle events in real time.
    /// </summary>
    private static async Task StreamSessionEventsAsync(
        SessionEventBus eventBus,
        HttpContext context,
        CancellationToken ct)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        try
        {
            await context.Response.WriteAsync("event: connected\ndata: {}\n\n", ct).ConfigureAwait(false);
            await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);

            await foreach (var evt in eventBus.SubscribeAsync(ct).ConfigureAwait(false))
            {
                var eventType = evt switch
                {
                    SessionConnectedEvent => "session_connected",
                    SessionDisconnectedEvent => "session_disconnected",
                    SessionStateChangedEvent => "state_changed",
                    SessionTranscriptEvent => "transcript",
                    SpeakerDetectedEvent => "speaker_detected",
                    AudioLevelEvent => "audio_level",
                    _ => "unknown",
                };

                var json = JsonSerializer.Serialize(evt, evt.GetType(), JsonOptions);
                await context.Response.WriteAsync($"event: {eventType}\ndata: {json}\n\n", ct).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected — normal SSE lifecycle
        }
    }
}
