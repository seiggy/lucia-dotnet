using lucia.AgentHost.Models;
using lucia.AgentHost.Services;
using lucia.Agents.Abstractions;
using lucia.Agents.Training;
using lucia.Agents.Training.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Apis;

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
    /// Uses the skill's <see cref="IOptimizableSkill.AgentId"/> to filter traces
    /// and <see cref="IOptimizableSkill.SearchToolNames"/> to identify relevant tool calls.
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

        var toolNames = skill.SearchToolNames;
        if (toolNames.Count == 0)
            return TypedResults.Ok(new List<TraceSearchTerm>());

        var filter = new TraceFilterCriteria
        {
            Page = 1,
            PageSize = limit ?? 100,
            AgentFilter = string.IsNullOrEmpty(skill.AgentId) ? null : skill.AgentId
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

                    // Extract all search terms (handles both singular and array parameters)
                    foreach (var term in ExtractSearchTerms(toolCall.Arguments))
                    {
                        if (!searchTerms.ContainsKey(term))
                        {
                            searchTerms[term] = new TraceSearchTerm
                            {
                                SearchTerm = term,
                                OccurrenceCount = 1,
                                LastSeen = trace.Timestamp,
                                TraceId = trace.Id
                            };
                        }
                        else
                        {
                            var existing = searchTerms[term];
                            searchTerms[term] = existing with
                            {
                                OccurrenceCount = existing.OccurrenceCount + 1,
                                LastSeen = trace.Timestamp > existing.LastSeen ? trace.Timestamp : existing.LastSeen
                            };
                        }
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
    /// Extracts search term values from a JSON arguments string.
    /// Handles both singular "searchTerm" (string) and plural "searchTerms" (string array).
    /// </summary>
    private static List<string> ExtractSearchTerms(string? arguments)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(arguments))
            return results;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(arguments);

            // Try plural "searchTerms" array first (used by LightControlSkill)
            if (TryGetStringArray(doc.RootElement, "searchTerms", results) ||
                TryGetStringArray(doc.RootElement, "SearchTerms", results))
                return results;

            // Try singular "searchTerm" string (used by Climate/Fan skills)
            if (TryGetString(doc.RootElement, "searchTerm", out var term) ||
                TryGetString(doc.RootElement, "SearchTerm", out term))
            {
                results.Add(term!);
                return results;
            }

            // Try "area" parameter (used by FindLightsByAreaAsync, FindFansByAreaAsync, etc.)
            if (TryGetString(doc.RootElement, "area", out var area) ||
                TryGetString(doc.RootElement, "Area", out area))
            {
                results.Add(area!);
                return results;
            }

            // Fall back to first string property
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        results.Add(value);
                    return results;
                }

                if (property.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    TryGetStringArray(doc.RootElement, property.Name, results);
                    if (results.Count > 0)
                        return results;
                }
            }
        }
        catch
        {
            // Not valid JSON — ignore
        }

        return results;
    }

    private static bool TryGetString(System.Text.Json.JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != System.Text.Json.JsonValueKind.String)
            return false;
        value = prop.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetStringArray(System.Text.Json.JsonElement element, string propertyName, List<string> results)
    {
        if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != System.Text.Json.JsonValueKind.Array)
            return false;

        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    results.Add(value);
            }
        }

        return results.Count > 0;
    }
}
