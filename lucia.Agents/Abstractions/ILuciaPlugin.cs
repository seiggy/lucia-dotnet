using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace lucia.Agents.Abstractions;

/// <summary>
/// Defines a discoverable plugin loaded from a <c>plugin.cs</c> script.
/// Scripts are evaluated by Roslyn and the last expression must be an
/// <c>ILuciaPlugin</c> instance. Plugins run after the host is built,
/// with full access to the DI container.
/// </summary>
public interface ILuciaPlugin
{
    /// <summary>
    /// Unique identifier for this plugin (e.g. "metamcp", "searxng").
    /// </summary>
    string PluginId { get; }

    /// <summary>
    /// Registers services into the DI container <b>before</b> the host is built.
    /// Use this to add interfaces (e.g. <c>IWebSearchSkill</c>) that core agents
    /// resolve via optional injection.
    /// </summary>
    void ConfigureServices(IHostApplicationBuilder builder) { }

    /// <summary>
    /// Runs the plugin's startup logic with access to the built DI container.
    /// Called after the host is built but before it starts accepting requests.
    /// Agents and Home Assistant are <b>not</b> yet initialized at this point.
    /// </summary>
    Task ExecuteAsync(IServiceProvider services, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    /// <summary>
    /// Optionally maps HTTP endpoints on the application.
    /// Default implementation is a no-op.
    /// </summary>
    void MapEndpoints(WebApplication app) { }

    /// <summary>
    /// Runs after all agents, Home Assistant, and entity location services are
    /// fully initialized. Use this for logic that depends on the system being
    /// fully operational (e.g. querying agents, reading HA entity state).
    /// </summary>
    Task OnSystemReadyAsync(IServiceProvider services, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
