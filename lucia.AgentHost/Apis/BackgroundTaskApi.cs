using System.Text.Json;
using System.Text.Json.Serialization;
using lucia.Wyoming.Models;

namespace lucia.AgentHost.Apis;

public static class BackgroundTaskApi
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void MapBackgroundTaskEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/tasks/background")
            .WithTags("Background Tasks");

        group.MapGet("/", (BackgroundTaskService taskService) =>
            Results.Ok(taskService.GetAllTasks()))
            .RequireAuthorization();

        group.MapGet("/{taskId}", (string taskId, BackgroundTaskService taskService) =>
        {
            var task = taskService.GetTask(taskId);
            return task is not null
                ? Results.Ok(task)
                : Results.NotFound();
        }).RequireAuthorization();

        // SSE stream — no auth (EventSource cannot send headers, matches ActivityApi pattern)
        // Event-driven: wakes instantly when task state changes via WaitForChangeAsync.
        group.MapGet("/stream", async (BackgroundTaskService taskService, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            // Send initial snapshot
            await WriteSseAsync(ctx.Response, "snapshot", taskService.GetAllTasks(), ct).ConfigureAwait(false);
            var lastVersion = taskService.Version;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Wait for a state change or 2-second heartbeat (keeps connection alive)
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(2_000);

                    try
                    {
                        await taskService.WaitForChangeAsync(lastVersion, timeoutCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // Heartbeat timeout — send current state anyway to keep connection alive
                    }

                    var currentVersion = taskService.Version;
                    if (currentVersion != lastVersion)
                    {
                        lastVersion = currentVersion;
                        await WriteSseAsync(ctx.Response, "snapshot", taskService.GetAllTasks(), ct).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });
    }

    private static async Task WriteSseAsync(
        HttpResponse response,
        string eventType,
        object data,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await response.WriteAsync($"event: {eventType}\ndata: {json}\n\n", ct).ConfigureAwait(false);
        await response.Body.FlushAsync(ct).ConfigureAwait(false);
    }
}
