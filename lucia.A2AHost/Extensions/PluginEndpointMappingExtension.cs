using lucia.Agents.Abstractions;

namespace lucia.A2AHost.Extensions
{
    public static class PluginEndpointMappingExtension
    {
        public static void MapAgentPlugins(this WebApplication app)
        {
            var plugins = app.Services.GetServices<IAgentPlugin>();

            foreach (var plugin in plugins)
            {
                plugin.MapAgentEndpoints(app);
            }
        }
    }
}
