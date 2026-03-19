using System.Text.Json;
using A2A;
using lucia.AgentHost.Extensions;
using lucia.AgentHost.Models;
using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Training.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Apis;

/// <summary>
/// Minimal API endpoints for managing agent tasks.
/// </summary>
public static class TaskManagementApi
{
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static IEndpointRouteBuilder MapTaskManagementApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/tasks")
            .WithTags("Tasks")
            .RequireAuthorization();

        group.MapGet("/active", ListActiveTasksAsync);
        group.MapGet("/archived", ListArchivedTasksAsync);
        group.MapGet("/stats", GetStatsAsync);
        group.MapGet("/{id}", GetTaskAsync);
        group.MapPost("/{id}/cancel", CancelTaskAsync);

        return endpoints;
    }

    /// <summary>
    /// List active tasks from Redis.
    /// </summary>
    private static async Task<Ok<List<ActiveTaskSummary>>> ListActiveTasksAsync(
        [FromServices] ITaskIdIndex taskIdIndex,
        [FromServices] ITaskStore taskStore,
        CancellationToken ct)
    {
        var taskIds = await taskIdIndex.GetAllTrackedTaskIdsAsync(ct).ConfigureAwait(false);

        var tasks = new List<ActiveTaskSummary>();
        foreach (var taskId in taskIds)
        {
            var task = await taskStore.GetTaskAsync(taskId, ct).ConfigureAwait(false);
            if (task is null)
            {
                await taskIdIndex.RemoveTaskIdAsync(taskId, ct).ConfigureAwait(false);
                continue;
            }

            tasks.Add(MapToActiveSummary(task));
        }

        // Most recent first
        tasks.Sort((a, b) => b.LastUpdated.CompareTo(a.LastUpdated));
        return TypedResults.Ok(tasks);
    }

    /// <summary>
    /// List archived tasks from MongoDB with filtering.
    /// </summary>
    private static async Task<Ok<PagedResult<ArchivedTask>>> ListArchivedTasksAsync(
        [FromServices] ITaskArchiveStore archive,
        [FromQuery] string? status,
        [FromQuery] string? agentId,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var filter = new TaskFilterCriteria
        {
            Status = status,
            AgentId = agentId,
            FromDate = fromDate,
            ToDate = toDate,
            Search = search,
            Page = page ?? 1,
            PageSize = pageSize ?? 25,
        };

        var result = await archive.ListArchivedTasksAsync(filter, ct).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    /// <summary>
    /// Get a single task by ID. Checks active (Redis) first, then archive (MongoDB).
    /// </summary>
    private static async Task<Results<Ok<object>, NotFound>> GetTaskAsync(
        [FromServices] ITaskStore taskStore,
        [FromServices] ITaskArchiveStore archive,
        [FromRoute] string id,
        CancellationToken ct)
    {
        // Check active store first
        var active = await taskStore.GetTaskAsync(id, ct).ConfigureAwait(false);
        if (active is not null)
        {
            return TypedResults.Ok<object>(MapToActiveSummary(active));
        }

        // Fall back to archive
        var archived = await archive.GetArchivedTaskAsync(id, ct).ConfigureAwait(false);
        if (archived is not null)
        {
            return TypedResults.Ok<object>(archived);
        }

        return TypedResults.NotFound();
    }

    /// <summary>
    /// Get aggregate stats from both active and archived tasks.
    /// </summary>
    private static async Task<Ok<CombinedTaskStats>> GetStatsAsync(
        [FromServices] ITaskIdIndex taskIdIndex,
        [FromServices] ITaskStore taskStore,
        [FromServices] ITaskArchiveStore archive,
        CancellationToken ct)
    {
        var taskIds = await taskIdIndex.GetAllTrackedTaskIdsAsync(ct).ConfigureAwait(false);
        var activeCount = taskIds.Count;

        // Get archived stats
        var archivedStats = await archive.GetTaskStatsAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok(new CombinedTaskStats
        {
            ActiveCount = (int)activeCount,
            Archived = archivedStats,
        });
    }

    /// <summary>
    /// Cancel an active task.
    /// </summary>
    private static async Task<Results<Ok, NotFound>> CancelTaskAsync(
        [FromServices] ITaskStore taskStore,
        [FromRoute] string id,
        CancellationToken ct)
    {
        var task = await taskStore.GetTaskAsync(id, ct).ConfigureAwait(false);
        if (task is null)
        {
            return TypedResults.NotFound();
        }

        await taskStore.UpdateStatusAsync(id, TaskState.Canceled, cancellationToken: ct).ConfigureAwait(false);
        return TypedResults.Ok();
    }

    private static ActiveTaskSummary MapToActiveSummary(AgentTask task)
    {
        var history = task.History ?? [];
        var userInput = history
            .Where(m => m.Role == MessageRole.User)
            .SelectMany(m => m.Parts?.OfType<TextPart>() ?? [])
            .FirstOrDefault()?.Text;

        return new ActiveTaskSummary
        {
            Id = task.Id,
            ContextId = task.ContextId,
            Status = task.Status.State.ToString(),
            MessageCount = history.Count,
            UserInput = userInput is { Length: > 200 } ? userInput[..200] + "…" : userInput,
            LastUpdated = task.Status.Timestamp.UtcDateTime,
        };
    }
}

// ActiveTaskSummary and CombinedTaskStats are in separate files per project convention.
