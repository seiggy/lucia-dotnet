using lucia.Agents.Abstractions;
using lucia.Agents.Agents;
using lucia.Agents.Mcp;
using lucia.Agents.Providers;
using lucia.Agents.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Services;

/// <summary>
/// Background service that connects MCP tool servers and registers dynamic agents
/// on startup. Runs after the built-in agents are initialized.
/// </summary>
public sealed class DynamicAgentLoader : BackgroundService
{
    private readonly IAgentDefinitionRepository _repository;
    private readonly IMcpToolRegistry _toolRegistry;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IDynamicAgentProvider _dynamicAgentProvider;
    private readonly IChatClientResolver _clientResolver;
    private readonly IModelProviderResolver _providerResolver;
    private readonly IModelProviderRepository _providerRepository;
    private readonly TracingChatClientFactory _tracingFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DynamicAgentLoader> _logger;

    public DynamicAgentLoader(
        IAgentDefinitionRepository repository,
        IMcpToolRegistry toolRegistry,
        IAgentRegistry agentRegistry,
        IDynamicAgentProvider dynamicAgentProvider,
        IChatClientResolver clientResolver,
        IModelProviderResolver providerResolver,
        IModelProviderRepository providerRepository,
        TracingChatClientFactory tracingFactory,
        ILoggerFactory loggerFactory)
    {
        _repository = repository;
        _toolRegistry = toolRegistry;
        _agentRegistry = agentRegistry;
        _dynamicAgentProvider = dynamicAgentProvider;
        _clientResolver = clientResolver;
        _providerResolver = providerResolver;
        _providerRepository = providerRepository;
        _tracingFactory = tracingFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<DynamicAgentLoader>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give built-in agents a head start
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken).ConfigureAwait(false);

        _logger.LogInformation("Starting MCP tool server connections...");

        try
        {
            await _toolRegistry.InitializeAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize MCP tool registry. Dynamic agents may have limited tools.");
        }

        _logger.LogInformation("Loading dynamic agent definitions...");

        try
        {
            await LoadAndRegisterAgentsAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dynamic agent definitions");
        }
    }

    /// <summary>
    /// Reloads all dynamic agent definitions from MongoDB and re-registers them.
    /// Called on startup and on-demand via the reload API.
    /// </summary>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Reloading dynamic agent definitions...");
        await LoadAndRegisterAgentsAsync(ct).ConfigureAwait(false);
    }

    private async Task LoadAndRegisterAgentsAsync(CancellationToken ct)
    {
        var definitions = await _repository.GetEnabledAgentDefinitionsAsync(ct).ConfigureAwait(false);
        var registered = 0;
        var failed = 0;

        // Unregister previously loaded dynamic agents
        _dynamicAgentProvider.Clear();

        foreach (var definition in definitions)
        {
            // Skip built-in, remote, and orchestrator agents — they are managed separately
            if (definition.IsBuiltIn || definition.IsRemote || definition.IsOrchestrator)
                continue;

            try
            {
                var agent = new DynamicAgent(
                    definition.Id,
                    definition,
                    _repository,
                    _toolRegistry,
                    _clientResolver,
                    _providerResolver,
                    _providerRepository,
                    _tracingFactory,
                    _loggerFactory);

                await agent.InitializeAsync(ct).ConfigureAwait(false);
                await _agentRegistry.RegisterAgentAsync(agent.GetAgentCard(), ct).ConfigureAwait(false);
                _dynamicAgentProvider.Register(agent);

                registered++;
                _logger.LogInformation(
                    "Registered dynamic agent: {AgentName} ({ToolCount} tools)",
                    definition.Name, definition.Tools.Count);
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "Failed to register dynamic agent: {AgentName}", definition.Name);
            }
        }

        _logger.LogInformation(
            "Dynamic agent loading complete — {Registered} registered, {Failed} failed",
            registered, failed);
    }
}
