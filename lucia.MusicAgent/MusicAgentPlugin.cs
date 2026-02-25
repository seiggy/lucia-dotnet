using A2A;
using A2A.AspNetCore;
using lucia.Agents.Abstractions;
using lucia.Agents.Extensions;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

            builder.Services.AddSingleton<ILuciaAgent, MusicAgent>();
        }

        public void MapAgentEndpoints(WebApplication app)
        {
            var musicAgent = app.Services.GetServices<ILuciaAgent>()
                .First(a => a.GetAgentCard().Name == "music-agent");
            // Primary A2A endpoint — used by mesh mode (A2AHost) and standalone mode alike.
            // In standalone mode, AgentDiscoveryExtension separately maps the /a2a/music-agent
            // path from the agent card URL, so no mirror mapping is needed here.
            app.MapA2ALazy(() => musicAgent.GetAIAgent(), path: "/music", agentCard: musicAgent.GetAgentCard(), taskManager => app.MapWellKnownAgentCard(taskManager, "/music"));
        }
    }
}
