using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace lucia.Agents.Abstractions
{
    public interface IAgentPlugin
    {
        /// <summary>
        /// Agent Id used for identifying the agent
        /// </summary>
        string AgentId { get; }

        /// <summary>
        /// Configures agent-related HTTP endpoints on the specified web application.
        /// </summary>
        /// <param name="app">The web application to which agent endpoints will be mapped. Must not be null.</param>
        void MapAgentEndpoints(WebApplication app);

        /// <summary>
        /// Called by the host to hook into Agent Framework.
        /// </summary>
        /// <param name="builder"></param>
        void ConfigureAgentHost(IHostApplicationBuilder builder);
    }
}
