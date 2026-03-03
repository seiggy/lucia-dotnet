using System.Reflection;
using System.Runtime.Loader;
using lucia.Agents.Abstractions;
using lucia.Agents.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.PluginFramework;

/// <summary>
/// Discovers and loads plugins from both Roslyn script folders (<c>plugins/{name}/plugin.cs</c>)
/// and legacy DLL assemblies (<c>lucia.Plugins.*.dll</c>).
/// All plugin folders present in the directory are loaded — install/uninstall is managed
/// by <see cref="PluginManagementService"/>.
/// </summary>
public static class PluginLoader
{
    /// <summary>
    /// Loads agent plugins (<see cref="IAgentPlugin"/>) from a directory.
    /// </summary>
    public static void LoadAgentPlugins(
        IHostApplicationBuilder builder,
        string pluginDirectory)
    {
        if (!Directory.Exists(pluginDirectory))
            return;

        foreach (var dllPath in Directory.EnumerateFiles(pluginDirectory, "*.dll"))
        {
            var assembly = LoadAssembly(dllPath);
            if (assembly is null) continue;

            var pluginTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(IAgentPlugin).IsAssignableFrom(t));

            foreach (var type in pluginTypes)
            {
                var plugin = (IAgentPlugin)Activator.CreateInstance(type)!;
                plugin.ConfigureAgentHost(builder);
                builder.Services.AddSingleton<IAgentPlugin>(plugin);
            }
        }
    }

    /// <summary>
    /// Loads general plugins (<see cref="ILuciaPlugin"/>) from script folders.
    /// Every subfolder of <paramref name="pluginDirectory"/> that contains a
    /// <c>plugin.cs</c> is evaluated and loaded.
    /// </summary>
    public static async Task<List<ILuciaPlugin>> LoadPluginsAsync(
        string pluginDirectory,
        ILogger logger)
    {
        var loaded = new List<ILuciaPlugin>();

        if (!Directory.Exists(pluginDirectory))
            return loaded;

        foreach (var folder in Directory.EnumerateDirectories(pluginDirectory))
        {
            var scriptPath = Path.Combine(folder, "plugin.cs");
            if (!File.Exists(scriptPath))
                continue;

            var plugin = await PluginScriptHost
                .EvaluatePluginAsync(folder, logger)
                .ConfigureAwait(false);
            if (plugin is null) continue;

            loaded.Add(plugin);
        }

        return loaded;
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
