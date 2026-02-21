using lucia.Agents.Abstractions;
using System.Reflection;
using System.Runtime.Loader;

namespace lucia.A2AHost.Extensions
{
    public static class PluginLoader
    {
        public static void LoadAgentPlugins(
                IHostApplicationBuilder builder,
                string pluginDirectory
            )
        {
            var config = builder.Configuration;

            if (!Directory.Exists(pluginDirectory))
            {
                return;
            }

            foreach (var dllPath in Directory.EnumerateFiles(pluginDirectory, "*.dll"))
            {
                var assembly = LoadAssembly(dllPath);

                if (assembly is null) continue;

                // Find all IAgentPlugin implementations
                var pluginTypes = assembly
                    .GetTypes()
                    .Where(t =>
                            !t.IsAbstract &&
                            typeof(IAgentPlugin).IsAssignableFrom(t)
                        );

                foreach (var type in pluginTypes)
                {
                    var plugin = (IAgentPlugin)Activator.CreateInstance(type)!;

                    // let the plugin register its services and agent into DI / Agent Framework
                    plugin.ConfigureAgentHost(builder);
                    builder.Services.AddSingleton<IAgentPlugin>(plugin);
                }
            }
        }

        private static Assembly? LoadAssembly(string path)
        {
            try
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[lucia] PluginLoader: Failed to load assembly '{path}' — {ex.Message}");
                return null;
            }
        }
    }
}
