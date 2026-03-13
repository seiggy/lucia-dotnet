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
            .WithTags("Background Tasks")
            .RequireAuthorization();

        group.MapGet("/", (BackgroundTaskService taskService) =>
            Results.Ok(taskService.GetAllTasks()));

        group.MapGet("/{taskId}", (string taskId, BackgroundTaskService taskService) =>
        {
            var task = taskService.GetTask(taskId);
            return task is not null
                ? Results.Ok(task)
                : Results.NotFound();
        });

        group.MapGet("/stream", async (BackgroundTaskService taskService, HttpContext ctx, CancellationToken ct) =>
        {
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            await WriteSseAsync(ctx.Response, "snapshot", taskService.GetAllTasks(), ct).ConfigureAwait(false);

            var reader = taskService.GetUpdateReader();
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var update = await reader.ReadAsync(ct).ConfigureAwait(false);
                    await WriteSseAsync(ctx.Response, "update", update, ct).ConfigureAwait(false);
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
