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
        // Polls task state every second for responsive updates.
        group.MapGet("/stream", async (BackgroundTaskService taskService, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            var lastSnapshot = "";
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var tasks = taskService.GetAllTasks();
                    var json = JsonSerializer.Serialize(tasks, JsonOptions);

                    // Only send if state changed
                    if (json != lastSnapshot)
                    {
                        lastSnapshot = json;
                        await WriteSseAsync(ctx.Response, "snapshot", tasks, ct).ConfigureAwait(false);
                    }

                    await Task.Delay(1_000, ct).ConfigureAwait(false);
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
