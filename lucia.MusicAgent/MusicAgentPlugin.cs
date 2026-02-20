using A2A;
using A2A.AspNetCore;
using lucia.Agents.Abstractions;
using lucia.Agents.Orchestration;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using lucia.Agents.Extensions;

namespace lucia.MusicAgent
{
    public class MusicAgentPlugin : IAgentPlugin
    {
        public string AgentId => "music-agent";

        public void ConfigureAgentHost(IHostApplicationBuilder builder)
        {
            builder.Services.Configure<MusicAssistantConfig>(
                builder.Configuration.GetSection("MusicAssistant"));

            // Register default keyed forwarding for the music model.
            // If no per-agent model override is configured, this resolves
            // to the unkeyed IChatClient (the default model).
            builder.Services.AddKeyedSingleton<IChatClient>(
                OrchestratorServiceKeys.MusicModel,
                (sp, _) => sp.GetRequiredService<IChatClient>());

            builder.Services.AddSingleton<MusicPlaybackSkill>();

            builder.Services.AddSingleton<ILuciaAgent, MusicAgent>();
        }

        public void MapAgentEndpoints(WebApplication app)
        {
            var musicAgent = app.Services.GetRequiredService<ILuciaAgent>();
            var taskManager = new TaskManager();
            app.MapA2A(musicAgent.GetAIAgent(), path: "/", agentCard: musicAgent.GetAgentCard(), taskManager => app.MapWellKnownAgentCard(taskManager, "/"));
        }
    }
}
