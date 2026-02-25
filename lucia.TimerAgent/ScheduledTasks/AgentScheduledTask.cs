using System.Diagnostics;
using lucia.Agents.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace lucia.TimerAgent.ScheduledTasks;

/// <summary>
/// A scheduled task that replays a user prompt through the LuciaEngine orchestrator
/// at a future time. Captures the original prompt and optional agent/entity context
/// so the request can execute autonomously without user presence.
/// </summary>
public sealed class AgentScheduledTask : IScheduledTask
{
    private static readonly ActivitySource ActivitySource = new("Lucia.ScheduledTasks.AgentTask", "1.0.0");

    public required string Id { get; init; }
    public required string TaskId { get; init; }
    public required string Label { get; init; }
    public required DateTimeOffset FireAt { get; init; }
    public ScheduledTaskType TaskType => ScheduledTaskType.AgentTask;

    /// <summary>
    /// The user prompt to replay through the orchestrator when this task fires.
    /// </summary>
    public required string Prompt { get; init; }

    /// <summary>
    /// Optional agent ID to route directly to, bypassing the router.
    /// When null, the orchestrator performs normal routing.
    /// </summary>
    public string? TargetAgentId { get; init; }

    /// <summary>
    /// Optional serialized entity context captured at schedule time.
    /// Provides environmental state (e.g., "living room lights are on") for the deferred request.
    /// </summary>
    public string? EntityContext { get; init; }

    public bool IsExpired(DateTimeOffset now) => FireAt <= now;

    public async Task ExecuteAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("AgentTask.Execute", ActivityKind.Internal);
        activity?.SetTag("scheduled_task.prompt", Prompt);
        activity?.SetTag("scheduled_task.target_agent", TargetAgentId ?? "auto-route");

        var logger = services.GetRequiredService<ILogger<AgentScheduledTask>>();

        logger.LogInformation(
            "AgentTask {TaskId} firing — replaying prompt through orchestrator: {Prompt}",
            Id, Prompt);

        await using var scope = services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<LuciaEngine>();

        // Build the effective prompt, prepending entity context if captured
        var effectivePrompt = BuildEffectivePrompt();

        var result = await engine.ProcessRequestAsync(
            effectivePrompt,
            taskId: TaskId,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "AgentTask {TaskId} completed — orchestrator response: {Response}",
            Id, result.Text?.Length > 200 ? result.Text[..200] + "..." : result.Text);
    }

    public ScheduledTaskDocument ToDocument() => new()
    {
        Id = Id,
        TaskId = TaskId,
        Label = Label,
        FireAt = FireAt,
        TaskType = ScheduledTaskType.AgentTask,
        Status = ScheduledTaskStatus.Pending,
        Prompt = Prompt,
        TargetAgentId = TargetAgentId,
        EntityContext = EntityContext
    };

    private string BuildEffectivePrompt()
    {
        if (string.IsNullOrWhiteSpace(EntityContext))
            return Prompt;

        return $"[Scheduled action context: {EntityContext}]\n\n{Prompt}";
    }
}
