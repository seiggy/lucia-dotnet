using System.Diagnostics;
using lucia.TimerAgent.ScheduledTasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.TimerAgent;

/// <summary>
/// Skill that schedules deferred orchestrator requests (agent tasks) for future execution.
/// Allows users to say "do X in Y minutes" or "do X at Z time" and have the action
/// replayed through the LuciaEngine at the specified time.
/// </summary>
public sealed class SchedulerSkill
{
    private static readonly ActivitySource ActivitySource = new("Lucia.Skills.Scheduler", "1.0.0");

    private readonly ScheduledTaskStore _taskStore;
    private readonly IScheduledTaskRepository _taskRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SchedulerSkill> _logger;

    public SchedulerSkill(
        ScheduledTaskStore taskStore,
        IScheduledTaskRepository taskRepository,
        TimeProvider timeProvider,
        ILogger<SchedulerSkill> logger)
    {
        _taskStore = taskStore ?? throw new ArgumentNullException(nameof(taskStore));
        _taskRepository = taskRepository ?? throw new ArgumentNullException(nameof(taskRepository));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns the AI tool definitions for the scheduler skill.
    /// </summary>
    public IList<AITool> GetTools()
    {
        return
        [
            AIFunctionFactory.Create(ScheduleActionAsync, new AIFunctionFactoryOptions
            {
                Name = "ScheduleAction",
                Description = """
                    Schedules a deferred action to be executed at a future time through the agent system.
                    Use this when the user asks to do something later, e.g. "turn off the lights in 30 minutes",
                    "lock the doors at 11 PM", "play music in the living room at 6 PM".
                    
                    The prompt parameter is the action description exactly as the user would say it (e.g. "turn off the living room lights").
                    The delaySeconds parameter is the number of seconds from now until the action should execute.
                    Use delaySeconds for relative times ("in 30 minutes" = 1800).
                    The label parameter is a short human-readable description for display (e.g. "Turn off living room lights").
                    The entityContext parameter is optional — include current state info if relevant to the action
                    (e.g. "living room lights are currently on at 80% brightness").
                    """
            }),
            AIFunctionFactory.Create(ScheduleActionAtAsync, new AIFunctionFactoryOptions
            {
                Name = "ScheduleActionAt",
                Description = """
                    Schedules a deferred action to be executed at a specific wall-clock time through the agent system.
                    Use this when the user specifies an exact time, e.g. "lock the doors at 11 PM", "play jazz at 6:30 PM".
                    
                    The prompt parameter is the action description exactly as the user would say it.
                    The timeOfDay parameter is the target time in HH:mm (24-hour) format, e.g. "23:00" for 11 PM.
                    The label parameter is a short human-readable description for display.
                    The entityContext parameter is optional current state info.
                    """
            }),
            AIFunctionFactory.Create(CancelScheduledActionAsync, new AIFunctionFactoryOptions
            {
                Name = "CancelScheduledAction",
                Description = "Cancels a pending scheduled action by its ID. Returns whether the cancellation was successful."
            }),
            AIFunctionFactory.Create(ListScheduledActions, new AIFunctionFactoryOptions
            {
                Name = "ListScheduledActions",
                Description = "Lists all pending scheduled actions with their scheduled time, label, and prompt."
            })
        ];
    }

    /// <summary>
    /// Schedules an agent action to execute after a delay.
    /// </summary>
    public async Task<string> ScheduleActionAsync(string prompt, int delaySeconds, string label, string? entityContext = null)
    {
        using var activity = ActivitySource.StartActivity("SchedulerSkill.ScheduleAction", ActivityKind.Internal);

        if (string.IsNullOrWhiteSpace(prompt))
            return "Action prompt cannot be empty.";

        if (delaySeconds <= 0)
            return "Delay must be greater than zero seconds.";

        var fireAt = _timeProvider.GetUtcNow().AddSeconds(delaySeconds);
        return await CreateAndStoreTaskAsync(prompt, label, fireAt, entityContext, activity).ConfigureAwait(false);
    }

    /// <summary>
    /// Schedules an agent action to execute at a specific time of day.
    /// </summary>
    public async Task<string> ScheduleActionAtAsync(string prompt, string timeOfDay, string label, string? entityContext = null)
    {
        using var activity = ActivitySource.StartActivity("SchedulerSkill.ScheduleActionAt", ActivityKind.Internal);

        if (string.IsNullOrWhiteSpace(prompt))
            return "Action prompt cannot be empty.";

        if (!TimeOnly.TryParse(timeOfDay, out var targetTime))
            return $"Invalid time format '{timeOfDay}'. Use HH:mm format (e.g., '23:00' for 11 PM).";

        var now = _timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.DateTime);
        var fireAt = new DateTimeOffset(today.ToDateTime(targetTime), TimeSpan.Zero);

        // If the time has already passed today, schedule for tomorrow
        if (fireAt <= now)
            fireAt = fireAt.AddDays(1);

        return await CreateAndStoreTaskAsync(prompt, label, fireAt, entityContext, activity).ConfigureAwait(false);
    }

    /// <summary>
    /// Cancels a pending scheduled action.
    /// </summary>
    public async Task<string> CancelScheduledActionAsync(string actionId)
    {
        if (_taskStore.TryRemove(actionId, out _))
        {
            try
            {
                await _taskRepository.UpdateStatusAsync(actionId, ScheduledTaskStatus.Cancelled).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update task {TaskId} status to Cancelled in MongoDB", actionId);
            }

            _logger.LogInformation("Scheduled action {ActionId} cancelled", actionId);
            return $"Scheduled action '{actionId}' has been cancelled.";
        }

        return $"No pending scheduled action found with ID '{actionId}'.";
    }

    /// <summary>
    /// Lists all pending scheduled actions.
    /// </summary>
    public Task<string> ListScheduledActions()
    {
        var tasks = _taskStore.GetByType(ScheduledTaskType.AgentTask);
        if (tasks.Count == 0)
            return Task.FromResult("No pending scheduled actions.");

        var now = _timeProvider.GetUtcNow();
        var lines = tasks
            .OrderBy(t => t.FireAt)
            .Select(t =>
            {
                var remaining = t.FireAt - now;
                var friendlyRemaining = remaining.TotalSeconds > 0
                    ? FormatDuration(remaining)
                    : "executing now";
                var agentTask = t as AgentScheduledTask;
                var prompt = agentTask?.Prompt ?? t.Label;
                return $"- Action '{t.Id}': {friendlyRemaining} remaining — \"{prompt}\"";
            });

        return Task.FromResult($"Pending scheduled actions:\n{string.Join('\n', lines)}");
    }

    private async Task<string> CreateAndStoreTaskAsync(
        string prompt,
        string label,
        DateTimeOffset fireAt,
        string? entityContext,
        Activity? activity)
    {
        var taskInstanceId = Guid.NewGuid().ToString("N")[..8];
        var taskId = Guid.NewGuid().ToString("N");

        activity?.SetTag("agent_task.id", taskInstanceId);
        activity?.SetTag("agent_task.task_id", taskId);
        activity?.SetTag("agent_task.prompt", prompt);
        activity?.SetTag("agent_task.fire_at", fireAt.ToString("O"));

        var agentTask = new AgentScheduledTask
        {
            Id = taskInstanceId,
            TaskId = taskId,
            Label = string.IsNullOrWhiteSpace(label) ? $"Scheduled: {prompt}" : label,
            FireAt = fireAt,
            Prompt = prompt,
            EntityContext = entityContext
        };

        // Persist to MongoDB for crash recovery
        try
        {
            await _taskRepository.UpsertAsync(agentTask.ToDocument()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist agent task {TaskId} to MongoDB — task will still run in-memory", taskInstanceId);
        }

        _taskStore.Add(agentTask);

        var remaining = fireAt - _timeProvider.GetUtcNow();
        var friendlyTime = FormatDuration(remaining);

        _logger.LogInformation(
            "Agent task {TaskId} scheduled — fires in {Duration}: {Prompt}",
            taskInstanceId, friendlyTime, prompt);

        return $"Scheduled action '{taskInstanceId}' will execute in {friendlyTime}: \"{prompt}\"";
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
        {
            return ts.Minutes > 0
                ? $"{(int)ts.TotalHours} hour(s) and {ts.Minutes} minute(s)"
                : $"{(int)ts.TotalHours} hour(s)";
        }

        if (ts.TotalMinutes >= 1)
        {
            return ts.Seconds > 0
                ? $"{(int)ts.TotalMinutes} minute(s) and {ts.Seconds} second(s)"
                : $"{(int)ts.TotalMinutes} minute(s)";
        }

        return $"{(int)ts.TotalSeconds} second(s)";
    }
}
