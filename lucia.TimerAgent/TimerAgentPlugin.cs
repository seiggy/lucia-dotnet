using A2A.AspNetCore;
using lucia.Agents.Abstractions;
using lucia.Agents.Extensions;
using lucia.Agents.Orchestration;
using lucia.TimerAgent.ScheduledTasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace lucia.TimerAgent;

/// <summary>
/// Plugin that registers the Timer Agent into the A2A host.
/// </summary>
public sealed class TimerAgentPlugin : IAgentPlugin
{
    public string AgentId => "timer-agent";

    public void ConfigureAgentHost(IHostApplicationBuilder builder)
    {
        // Register default keyed forwarding for the timer model.
        // Falls back to the unkeyed IChatClient (default model) if no override configured.
        builder.Services.AddKeyedSingleton<IChatClient>(
            OrchestratorServiceKeys.TimerModel,
            (sp, _) => sp.GetRequiredService<IChatClient>());

        // Existing timer infrastructure (will be migrated to ScheduledTaskStore later)
        builder.Services.AddSingleton<ActiveTimerStore>();
        builder.Services.AddSingleton<TimerSkill>();
        builder.Services.AddSingleton<ILuciaAgent, TimerAgent>();
        builder.Services.AddHostedService<TimerExecutionService>();
        builder.Services.AddHostedService<TimerRecoveryService>();

        // Scheduled task infrastructure
        builder.Services.AddSingleton<ScheduledTaskStore>();
        builder.Services.AddSingleton<IScheduledTaskRepository, MongoScheduledTaskRepository>();
        builder.Services.AddHostedService<ScheduledTaskService>();
        builder.Services.AddHostedService<ScheduledTaskRecoveryService>();
    }

    public void MapAgentEndpoints(WebApplication app)
    {
        var timerAgent = app.Services.GetServices<ILuciaAgent>()
            .First(a => a.GetAgentCard().Name == "timer-agent");
        app.MapA2ALazy(() => timerAgent.GetAIAgent(), path: "/timers", agentCard: timerAgent.GetAgentCard(), taskManager => app.MapWellKnownAgentCard(taskManager, "/timers"));
    }
}
