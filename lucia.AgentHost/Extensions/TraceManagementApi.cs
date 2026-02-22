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

        group.MapGet("/{id}/related", GetRelatedTracesAsync);

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

    private static async Task<Ok<List<RelatedTraceSummary>>> GetRelatedTracesAsync(
        [FromServices] ITraceRepository repository,
        [FromRoute] string id,
        CancellationToken ct)
    {
        var trace = await repository.GetTraceAsync(id, ct);
        if (trace is null)
        {
            return TypedResults.Ok(new List<RelatedTraceSummary>());
        }

        var sessionTraces = await repository.GetTracesBySessionIdAsync(trace.SessionId, ct);

        var related = sessionTraces
            .Where(t => t.Id != id)
            .Select(t => new RelatedTraceSummary
            {
                Id = t.Id,
                Timestamp = t.Timestamp,
                TraceType = t.Metadata.GetValueOrDefault("traceType", "unknown"),
                AgentId = t.Metadata.GetValueOrDefault("agentId"),
                UserInput = t.UserInput.Length > 120 ? t.UserInput[..120] + "…" : t.UserInput,
                IsErrored = t.IsErrored,
                TotalDurationMs = t.TotalDurationMs,
            })
            .ToList();

        return TypedResults.Ok(related);
    }

    /// <summary>
    /// Lightweight projection of a related trace for cross-navigation.
    /// </summary>
    private sealed class RelatedTraceSummary
    {
        public required string Id { get; init; }
        public DateTime Timestamp { get; init; }
        public required string TraceType { get; init; }
        public string? AgentId { get; init; }
        public required string UserInput { get; init; }
        public bool IsErrored { get; init; }
        public double TotalDurationMs { get; init; }
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


