using A2A;
using A2A.AspNetCore;
using lucia.Agents.Abstractions;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Builder;
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

            builder.Services.AddSingleton<MusicPlaybackSkill>();

            builder.Services.AddSingleton<IAgent, MusicAgent>();
        }

        public void MapAgentEndpoints(WebApplication app)
        {
            var musicAgent = app.Services.GetRequiredService<IAgent>();
            var taskManager = new TaskManager();
            app.MapA2A(musicAgent.GetAIAgent(), path: "/", agentCard: musicAgent.GetAgentCard(), taskManager => app.MapWellKnownAgentCard(taskManager, "/"));
        }
    }
}
