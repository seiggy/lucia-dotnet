using lucia.Agents.Extensions;
using System.Text.Json;
using lucia.AgentHost.Conversation;
using lucia.AgentHost.Models;
using lucia.Agents.Abstractions;
using lucia.Agents.CommandTracing;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using lucia.Agents.Registry;
using lucia.Agents.Training;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace lucia.AgentHost.Apis;

/// <summary>
/// Minimal API endpoints for the live activity dashboard:
/// SSE stream, agent mesh topology, and aggregated activity summary.
/// </summary>
public static class ActivityApi
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static IEndpointRouteBuilder MapActivityApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/activity")
            .WithTags("Activity");

        group.MapGet("/live", StreamLiveEventsAsync)
            .RequireAuthorization();

        group.MapGet("/mesh", GetMeshTopologyAsync)
            .RequireAuthorization();

        group.MapGet("/summary", GetActivitySummaryAsync)
            .RequireAuthorization();

        group.MapGet("/agent-stats", GetAgentStatsAsync)
            .RequireAuthorization();

        return endpoints;
    }

    /// <summary>
    /// SSE endpoint streaming orchestration lifecycle events in real time.
    /// </summary>
    private static async Task StreamLiveEventsAsync(
        [FromServices] LiveActivityChannel channel,
        HttpContext ctx,
        CancellationToken ct)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection = "keep-alive";

        try
        {
            // Send an immediate ack so the browser triggers EventSource.onopen
            var ack = JsonSerializer.Serialize(new LiveEvent
            {
                Type = LiveEvent.Types.Connected,
                State = LiveEvent.States.Idle,
            }, JsonOptions);
            await ctx.Response.WriteAsync($"data: {ack}\n\n", ct).ConfigureAwait(false);
            await ctx.Response.Body.FlushAsync(ct).ConfigureAwait(false);

            await foreach (var evt in channel.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var json = JsonSerializer.Serialize(evt, JsonOptions);
                await ctx.Response.WriteAsync($"data: {json}\n\n", ct).ConfigureAwait(false);
                await ctx.Response.Body.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected — normal SSE lifecycle, not an error
        }
    }

    /// <summary>
    /// Returns the current agent mesh topology for initial graph layout.
    /// </summary>
    private static async Task<Ok<MeshTopology>> GetMeshTopologyAsync(
        [FromServices] IAgentRegistry registry,
        CancellationToken ct)
    {
        var agents = await registry.GetAllAgentsAsync(ct).ConfigureAwait(false);
        var nodes = new List<MeshNode>();
        var edges = new List<MeshEdge>();

        // Orchestrator is always the central node
        nodes.Add(new MeshNode
        {
            Id = "orchestrator",
            Label = "Orchestrator",
            NodeType = "orchestrator",
        });

        foreach (var agent in agents)
        {
            var agentId = agent.Name ?? agent.GetUrl()?.ToString() ?? "unknown";
            var isRemote = !string.IsNullOrEmpty(agent.GetUrl()) &&
                           Uri.TryCreate(agent.GetUrl(), UriKind.Absolute, out var agentUri) &&
                           !agentUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

            nodes.Add(new MeshNode
            {
                Id = agentId,
                Label = agent.Name ?? agentId,
                NodeType = "agent",
                IsRemote = isRemote,
            });

            edges.Add(new MeshEdge
            {
                Source = "orchestrator",
                Target = agentId,
            });

            // Tool nodes are added dynamically by the frontend when
            // toolCall SSE events arrive — A2A skills are capability
            // metadata, not actual tool definitions.
        }

        return TypedResults.Ok(new MeshTopology { Nodes = nodes, Edges = edges });
    }

    /// <summary>
    /// Aggregated activity summary from trace, task, and cache stats.
    /// </summary>
    private static async Task<Ok<ActivitySummary>> GetActivitySummaryAsync(
        [FromServices] ITraceRepository traceRepo,
        [FromServices] ITaskArchiveStore taskArchive,
        [FromServices] IPromptCacheService cacheService,
        [FromServices] ICommandTraceRepository commandTraceRepo,
        CancellationToken ct)
    {
        // All five calls hit independent data sources — run in parallel
        var traceStatsTask = traceRepo.GetStatsAsync(ct);
        var taskStatsTask = taskArchive.GetTaskStatsAsync(ct);
        var cacheStatsTask = cacheService.GetStatsAsync(ct);
        var chatCacheStatsTask = cacheService.GetChatCacheStatsAsync(ct);
        var commandTraceStatsTask = commandTraceRepo.GetStatsAsync(ct);

        await Task.WhenAll(traceStatsTask, taskStatsTask, cacheStatsTask, chatCacheStatsTask, commandTraceStatsTask)
            .ConfigureAwait(false);

        var cmdStats = await commandTraceStatsTask.ConfigureAwait(false);
        var total = cmdStats.CommandHandledCount + cmdStats.LlmFallbackCount;

        return TypedResults.Ok(new ActivitySummary
        {
            Traces = await traceStatsTask.ConfigureAwait(false),
            Tasks = await taskStatsTask.ConfigureAwait(false),
            Cache = await cacheStatsTask.ConfigureAwait(false),
            ChatCache = await chatCacheStatsTask.ConfigureAwait(false),
            Conversation = new ConversationStats
            {
                CommandParsed = cmdStats.CommandHandledCount,
                LlmFallback = cmdStats.LlmFallbackCount,
                Errors = cmdStats.ErrorCount,
                Total = total,
                CommandRate = total > 0 ? (double)cmdStats.CommandHandledCount / total : 0,
            },
        });
    }

    /// <summary>
    /// Per-agent breakdown stats derived from trace data.
    /// </summary>
    private static async Task<Ok<Dictionary<string, AgentActivityStats>>> GetAgentStatsAsync(
        [FromServices] ITraceRepository traceRepo,
        CancellationToken ct)
    {
        var stats = await traceRepo.GetStatsAsync(ct).ConfigureAwait(false);
        var result = new Dictionary<string, AgentActivityStats>();

        foreach (var (agentId, count) in stats.ByAgent)
        {
            if (agentId == "orchestration") continue;

            var agentErrors = stats.ErrorsByAgent.GetValueOrDefault(agentId, 0);
            result[agentId] = new AgentActivityStats
            {
                RequestCount = count,
                ErrorRate = agentErrors > 0 && count > 0
                    ? Math.Round((double)agentErrors / count * 100, 1)
                    : 0,
            };
        }

        return TypedResults.Ok(result);
    }
}
