using lucia.Agents.Services;
using lucia.Agents.Training;
using lucia.Agents.Training.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Extensions;

/// <summary>
/// Minimal API endpoints for the skill parameter optimizer.
/// Provides skill listing, device discovery, trace search term extraction,
/// and background optimization job management.
/// </summary>
public static class SkillOptimizerApi
{
    public static IEndpointRouteBuilder MapSkillOptimizerApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/skill-optimizer")
            .WithTags("Skill Optimizer")
            .RequireAuthorization();

        group.MapGet("/skills", ListSkillsAsync);
        group.MapGet("/skills/{skillId}/devices", GetSkillDevicesAsync);
        group.MapGet("/skills/{skillId}/traces", GetSkillTracesAsync);
        group.MapPost("/skills/{skillId}/optimize", StartOptimizationAsync);
        group.MapGet("/jobs/{jobId}", GetJobStatusAsync);
        group.MapPost("/jobs/{jobId}/cancel", CancelJobAsync);

        return endpoints;
    }

    /// <summary>
    /// Lists all optimizable skills with their current parameters.
    /// </summary>
    private static Ok<List<OptimizableSkillInfo>> ListSkillsAsync(
        [FromServices] SkillOptimizerJobManager jobManager)
    {
        var skills = jobManager.GetSkills().Select(s => new OptimizableSkillInfo
        {
            SkillId = s.SkillId,
            DisplayName = s.SkillDisplayName,
            ConfigSection = s.ConfigSectionName,
            CurrentParams = s.GetCurrentMatchOptions()
        }).ToList();

        return TypedResults.Ok(skills);
    }

    /// <summary>
    /// Returns the entity list for a skill's domain(s) from the shared entity location cache.
    /// Uses the same <see cref="IEntityLocationService"/> as the Entity Locations page
    /// so devices are available as soon as the location cache is loaded.
    /// </summary>
    private static async Task<Results<Ok<List<SkillDeviceInfo>>, NotFound<string>>> GetSkillDevicesAsync(
        [FromRoute] string skillId,
        [FromServices] SkillOptimizerJobManager jobManager,
        [FromServices] IEntityLocationService locationService,
        CancellationToken ct)
    {
        var skill = jobManager.GetSkill(skillId);
        if (skill is null)
            return TypedResults.NotFound($"Skill '{skillId}' not found");

        var allEntities = await locationService.GetEntitiesAsync(ct).ConfigureAwait(false);

        var domains = skill.EntityDomains;
        var filtered = allEntities
            .Where(e => domains.Contains(e.Domain, StringComparer.OrdinalIgnoreCase))
            .Select(e => new SkillDeviceInfo
            {
                EntityId = e.EntityId,
                FriendlyName = e.FriendlyName
            })
            .OrderBy(d => d.FriendlyName)
            .ToList();

        return TypedResults.Ok(filtered);
    }

    /// <summary>
    /// Extracts unique search terms from trace tool calls for a skill.
    /// Looks for tool calls matching the skill's find methods and extracts
    /// the search term argument.
    /// </summary>
    private static async Task<Results<Ok<List<TraceSearchTerm>>, NotFound<string>>> GetSkillTracesAsync(
        [FromRoute] string skillId,
        [FromServices] SkillOptimizerJobManager jobManager,
        [FromServices] ITraceRepository traceRepository,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        var skill = jobManager.GetSkill(skillId);
        if (skill is null)
            return TypedResults.NotFound($"Skill '{skillId}' not found");

        // Map skill IDs to their tool function names
        var toolNames = skillId switch
        {
            "light-control" => new[] { "FindLightAsync", "FindLightsByAreaAsync" },
            _ => Array.Empty<string>()
        };

        if (toolNames.Length == 0)
            return TypedResults.Ok(new List<TraceSearchTerm>());

        // Fetch recent traces and extract search terms from tool calls
        var filter = new TraceFilterCriteria
        {
            Page = 1,
            PageSize = limit ?? 100
        };

        var traces = await traceRepository.ListTracesAsync(filter, ct).ConfigureAwait(false);

        var searchTerms = new Dictionary<string, TraceSearchTerm>(StringComparer.OrdinalIgnoreCase);

        foreach (var trace in traces.Items)
        {
            foreach (var execution in trace.AgentExecutions)
            {
                foreach (var toolCall in execution.ToolCalls)
                {
                    if (!toolNames.Contains(toolCall.ToolName, StringComparer.OrdinalIgnoreCase))
                        continue;

                    // Parse the search term from the JSON arguments
                    var searchTerm = ExtractSearchTerm(toolCall.Arguments);
                    if (string.IsNullOrWhiteSpace(searchTerm))
                        continue;

                    if (!searchTerms.ContainsKey(searchTerm))
                    {
                        searchTerms[searchTerm] = new TraceSearchTerm
                        {
                            SearchTerm = searchTerm,
                            OccurrenceCount = 1,
                            LastSeen = trace.Timestamp,
                            TraceId = trace.Id
                        };
                    }
                    else
                    {
                        var existing = searchTerms[searchTerm];
                        searchTerms[searchTerm] = existing with
                        {
                            OccurrenceCount = existing.OccurrenceCount + 1,
                            LastSeen = trace.Timestamp > existing.LastSeen ? trace.Timestamp : existing.LastSeen
                        };
                    }
                }
            }
        }

        var result = searchTerms.Values
            .OrderByDescending(t => t.OccurrenceCount)
            .ToList();

        return TypedResults.Ok(result);
    }

    /// <summary>
    /// Starts a background optimization job for a skill.
    /// </summary>
    private static Results<Ok<StartJobResponse>, NotFound<string>, BadRequest<string>> StartOptimizationAsync(
        [FromRoute] string skillId,
        [FromBody] StartOptimizationRequest request,
        [FromServices] SkillOptimizerJobManager jobManager)
    {
        var skill = jobManager.GetSkill(skillId);
        if (skill is null)
            return TypedResults.NotFound($"Skill '{skillId}' not found");

        if (request.TestCases is not { Count: > 0 })
            return TypedResults.BadRequest("At least one test case is required");

        if (string.IsNullOrWhiteSpace(request.EmbeddingModel))
            return TypedResults.BadRequest("Embedding model is required");

        var jobId = jobManager.StartJob(new StartOptimizerJobRequest
        {
            SkillId = skillId,
            EmbeddingModel = request.EmbeddingModel,
            TestCases = request.TestCases
        });

        return TypedResults.Ok(new StartJobResponse { JobId = jobId });
    }

    /// <summary>
    /// Returns the current status and progress of an optimization job.
    /// </summary>
    private static Results<Ok<JobStatusResponse>, NotFound<string>> GetJobStatusAsync(
        [FromRoute] string jobId,
        [FromServices] SkillOptimizerJobManager jobManager)
    {
        var state = jobManager.GetJob(jobId);
        if (state is null)
            return TypedResults.NotFound($"Job '{jobId}' not found");

        return TypedResults.Ok(new JobStatusResponse
        {
            JobId = state.JobId,
            SkillId = state.SkillId,
            EmbeddingModel = state.EmbeddingModel,
            TestCaseCount = state.TestCaseCount,
            Status = state.Status.ToString().ToLowerInvariant(),
            StartedAt = state.StartedAt,
            CompletedAt = state.CompletedAt,
            Progress = state.LatestProgress,
            Result = state.Result,
            Error = state.Error
        });
    }

    /// <summary>
    /// Cancels a running optimization job.
    /// </summary>
    private static Results<Ok, NotFound<string>, BadRequest<string>> CancelJobAsync(
        [FromRoute] string jobId,
        [FromServices] SkillOptimizerJobManager jobManager)
    {
        var state = jobManager.GetJob(jobId);
        if (state is null)
            return TypedResults.NotFound($"Job '{jobId}' not found");

        if (!jobManager.CancelJob(jobId))
            return TypedResults.BadRequest("Job is not running");

        return TypedResults.Ok();
    }

    /// <summary>
    /// Extracts the "searchTerm" value from a JSON arguments string.
    /// </summary>
    private static string? ExtractSearchTerm(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(arguments);
            if (doc.RootElement.TryGetProperty("searchTerm", out var prop))
                return prop.GetString();
            if (doc.RootElement.TryGetProperty("SearchTerm", out var prop2))
                return prop2.GetString();
            // Fall back to first string property
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    return property.Value.GetString();
            }
        }
        catch
        {
            // Not valid JSON — ignore
        }

        return null;
    }
}
