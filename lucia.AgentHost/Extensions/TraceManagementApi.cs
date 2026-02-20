using lucia.Agents.Training;
using lucia.Agents.Training.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// Minimal API endpoints for managing conversation traces.
/// </summary>
public static class TraceManagementApi
{
    public static IEndpointRouteBuilder MapTraceManagementApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/traces")
            .WithTags("Traces")
            .RequireAuthorization();

        group.MapGet("/", ListTracesAsync);

        group.MapGet("/stats", GetStatsAsync);

        group.MapGet("/{id}", GetTraceAsync);

        group.MapPut("/{id}/label", UpdateLabelAsync);

        group.MapDelete("/{id}", DeleteTraceAsync);

        return endpoints;
    }

    private static async Task<Ok<PagedResult<ConversationTrace>>> ListTracesAsync(
        [FromServices] ITraceRepository repository,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] string? agent,
        [FromQuery] string? model,
        [FromQuery] LabelStatus? label,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filter = new TraceFilterCriteria
        {
            FromDate = fromDate,
            ToDate = toDate,
            AgentFilter = agent,
            ModelFilter = model,
            LabelFilter = label,
            SearchText = search,
            Page = page ?? 1,
            PageSize = pageSize ?? 25
        };

        var result = await repository.ListTracesAsync(filter, ct);
        return TypedResults.Ok(result);
    }

    private static async Task<Ok<TraceStats>> GetStatsAsync(
        [FromServices] ITraceRepository repository,
        CancellationToken ct)
    {
        var stats = await repository.GetStatsAsync(ct);
        return TypedResults.Ok(stats);
    }

    private static async Task<Results<Ok<ConversationTrace>, NotFound>> GetTraceAsync(
        [FromServices] ITraceRepository repository,
        [FromRoute] string id,
        CancellationToken ct)
    {
        var trace = await repository.GetTraceAsync(id, ct);

        if (trace is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(trace);
    }

    private static async Task<NoContent> UpdateLabelAsync(
        [FromServices] ITraceRepository repository,
        [FromRoute] string id,
        [FromBody] LabelRequest request,
        CancellationToken ct)
    {
        var label = new TraceLabel
        {
            Status = request.Status,
            ReviewerNotes = request.ReviewerNotes,
            CorrectionText = request.CorrectionText,
            LabeledAt = DateTime.UtcNow
        };

        await repository.UpdateLabelAsync(id, label, ct);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> DeleteTraceAsync(
        [FromServices] ITraceRepository repository,
        [FromRoute] string id,
        CancellationToken ct)
    {
        await repository.DeleteTraceAsync(id, ct);
        return TypedResults.NoContent();
    }
}


