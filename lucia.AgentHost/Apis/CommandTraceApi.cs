using System.Text.Json;
using System.Text.Json.Serialization;

using lucia.AgentHost.Conversation.Tracing;
using lucia.Agents.CommandTracing;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Apis;

/// <summary>
/// Minimal API endpoints for browsing command trace records.
/// </summary>
public static class CommandTraceApi
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static IEndpointRouteBuilder MapCommandTraceApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/command-traces")
            .WithTags("CommandTraces")
            .RequireAuthorization();

        group.MapGet("/", ListCommandTracesAsync);
        group.MapGet("/stats", GetStatsAsync);
        group.MapGet("/live", StreamLiveAsync);
        group.MapGet("/{id}", GetCommandTraceAsync);

        return endpoints;
    }

    private static async Task<IResult> ListCommandTracesAsync(
        [FromServices] ICommandTraceRepository repository,
        [FromQuery] string? search,
        [FromQuery] string? outcome,
        [FromQuery] string? skillId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        CommandTraceOutcome? parsedOutcome = null;
        if (!string.IsNullOrEmpty(outcome) && Enum.TryParse<CommandTraceOutcome>(outcome, ignoreCase: true, out var o))
        {
            parsedOutcome = o;
        }

        var filter = new CommandTraceFilter
        {
            Search = search,
            Outcome = parsedOutcome,
            SkillId = skillId,
            FromDate = fromDate,
            ToDate = toDate,
            Page = page ?? 1,
            PageSize = pageSize ?? 20,
        };

        var result = await repository.ListAsync(filter, ct).ConfigureAwait(false);
        return Results.Json(result, JsonOptions);
    }

    private static async Task<IResult> GetStatsAsync(
        [FromServices] ICommandTraceRepository repository,
        CancellationToken ct)
    {
        var stats = await repository.GetStatsAsync(ct).ConfigureAwait(false);
        return Results.Json(stats, JsonOptions);
    }

    private static async Task<IResult> GetCommandTraceAsync(
        string id,
        [FromServices] ICommandTraceRepository repository,
        CancellationToken ct)
    {
        var trace = await repository.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (trace is null)
        {
            return Results.NotFound();
        }

        return Results.Json(trace, JsonOptions);
    }

    /// <summary>
    /// SSE endpoint streaming new command traces in real time.
    /// </summary>
    private static async Task StreamLiveAsync(
        [FromServices] CommandTraceChannel channel,
        HttpContext ctx,
        CancellationToken ct)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection = "keep-alive";

        try
        {
            // Send an immediate ack
            await ctx.Response.WriteAsync("data: {\"type\":\"connected\"}\n\n", ct).ConfigureAwait(false);
            await ctx.Response.Body.FlushAsync(ct).ConfigureAwait(false);

            await foreach (var trace in channel.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var json = JsonSerializer.Serialize(trace, JsonOptions);
                await ctx.Response.WriteAsync($"data: {json}\n\n", ct).ConfigureAwait(false);
                await ctx.Response.Body.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected
        }
    }
}
