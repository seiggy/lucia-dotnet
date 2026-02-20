using A2A;
using A2A.AspNetCore;
using lucia.Agents.Abstractions;
using lucia.Agents.Extensions;
using lucia.Agents.Orchestration;
using Microsoft.Agents.AI.Hosting;
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

        builder.Services.AddSingleton<TimerSkill>();
        builder.Services.AddSingleton<ILuciaAgent, TimerAgent>();
    }

    public void MapAgentEndpoints(WebApplication app)
    {
        var timerAgent = app.Services.GetServices<ILuciaAgent>()
            .First(a => a.GetAgentCard().Name == "timer-agent");
        app.MapA2A(timerAgent.GetAIAgent(), path: "/", agentCard: timerAgent.GetAgentCard(), taskManager => app.MapWellKnownAgentCard(taskManager, "/"));
    }
}
