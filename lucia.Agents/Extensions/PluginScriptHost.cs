using System.Reflection;
using lucia.Agents.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Extensions;

/// <summary>
/// Evaluates <c>plugin.cs</c> script files using Roslyn's CSharpScript API,
/// returning an <see cref="ILuciaPlugin"/> instance from each script.
/// The last expression in the script must be an <c>ILuciaPlugin</c> instance.
/// </summary>
public static class PluginScriptHost
{
    private static readonly Lazy<ScriptOptions> DefaultOptions = new(CreateDefaultOptions);

    /// <summary>
    /// Evaluates a <c>plugin.cs</c> file and returns the <see cref="ILuciaPlugin"/> it produces.
    /// </summary>
    /// <param name="pluginFolder">Absolute path to the plugin folder containing <c>plugin.cs</c>.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>The plugin instance, or <c>null</c> if evaluation fails.</returns>
    public static async Task<ILuciaPlugin?> EvaluatePluginAsync(string pluginFolder, ILogger logger)
    {
        var scriptPath = Path.Combine(pluginFolder, "plugin.cs");
        if (!File.Exists(scriptPath))
        {
            logger.LogWarning("Plugin folder '{Folder}' has no plugin.cs — skipping.", pluginFolder);
            return null;
        }

        try
        {
            var code = await File.ReadAllTextAsync(scriptPath)
                .ConfigureAwait(false);

            var options = DefaultOptions.Value
                .WithFilePath(scriptPath)
                .WithSourceResolver(new SourceFileResolver(
                    searchPaths: [pluginFolder],
                    baseDirectory: pluginFolder));

            var result = await CSharpScript.EvaluateAsync<ILuciaPlugin>(
                code,
                options).ConfigureAwait(false);

            if (result is null)
            {
                logger.LogWarning(
                    "Plugin script '{Path}' returned null — the last expression must be an ILuciaPlugin instance.",
                    scriptPath);
                return null;
            }

            logger.LogInformation("Loaded plugin '{Id}' from script '{Path}'.", result.PluginId, scriptPath);
            return result;
        }
        catch (CompilationErrorException ex)
        {
            logger.LogError(
                "Compilation error in plugin script '{Path}': {Errors}",
                scriptPath,
                string.Join(Environment.NewLine, ex.Diagnostics));
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to evaluate plugin script '{Path}'.", scriptPath);
            return null;
        }
    }

    private static ScriptOptions CreateDefaultOptions()
    {
        // Assemblies available to plugin scripts
        var references = new List<Assembly>
        {
            typeof(ILuciaPlugin).Assembly,                                                                  // lucia.Agents
            typeof(BackgroundService).Assembly,                                                             // Microsoft.Extensions.Hosting 
            typeof(Microsoft.Extensions.Hosting.IHostApplicationBuilder).Assembly,                          // Microsoft.Extensions.Hosting.Abstractions
            typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions).Assembly,   // Microsoft.Extensions.DependencyInjection
            typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly,                   // Microsoft.Extensions.DependencyInjection.Abstractions
            typeof(Microsoft.Extensions.Logging.ILogger).Assembly,                                          // Microsoft.Extensions.Logging.Abstractions
            typeof(Microsoft.Extensions.Configuration.IConfiguration).Assembly,                             // Microsoft.Extensions.Configuration.Abstractions
            typeof(Microsoft.AspNetCore.Builder.WebApplication).Assembly,                                   // Microsoft.AspNetCore
            typeof(Microsoft.Extensions.AI.AITool).Assembly,                                                // Microsoft.Extensions.AI.Abstractions
            typeof(Microsoft.Extensions.AI.AIFunctionFactory).Assembly,                                     // Microsoft.Extensions.AI
            typeof(System.Net.Http.IHttpClientFactory).Assembly,                                            // Microsoft.Extensions.Http
            typeof(System.Net.Http.HttpClient).Assembly,                                                    // System.Net.Http
            typeof(System.Text.Json.JsonSerializer).Assembly,                                               // System.Text.Json
            typeof(System.ComponentModel.DescriptionAttribute).Assembly,                                    // System.ComponentModel.Primitives
            typeof(System.Diagnostics.ActivitySource).Assembly,                                             // System.Diagnostics.DiagnosticSource
            typeof(System.Diagnostics.Metrics.Meter).Assembly,                                              // System.Diagnostics.DiagnosticSource (Meters)
        };

        return ScriptOptions.Default
            .AddReferences(references)
            .AddImports(
                "System",
                "System.Collections.Generic",
                "System.Linq",
                "System.Threading",
                "System.Threading.Tasks",
                "lucia.Agents.Abstractions",
                "Microsoft.Extensions.DependencyInjection",
                "Microsoft.Extensions.Hosting",
                "Microsoft.Extensions.Logging",
                "Microsoft.Extensions.Configuration",
                "Microsoft.Extensions.AI",
                "System.Net.Http");
    }
}
