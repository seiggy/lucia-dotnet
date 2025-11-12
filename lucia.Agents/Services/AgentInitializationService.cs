using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using lucia.Agents.Registry;
using lucia.Agents.Agents;
using Microsoft.Agents.AI.Hosting;

namespace lucia.Agents.Extensions;

/// <summary>
/// Background service that initializes and registers all agents when the application starts
/// </summary>
public class AgentInitializationService : BackgroundService
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly LightAgent _lightAgent;
    private readonly OrchestratorAgent _orchestratorAgent;
    private readonly GeneralAgent _generalAgent;
    private readonly ILogger<AgentInitializationService> _logger;

    public AgentInitializationService(
        IAgentRegistry agentRegistry,
        LightAgent lightAgent,
        OrchestratorAgent orchestratorAgent,
        GeneralAgent generalAgent,
        ILogger<AgentInitializationService> logger)
    {
        _agentRegistry = agentRegistry;
        _lightAgent = lightAgent;
        _orchestratorAgent = orchestratorAgent;
        _generalAgent = generalAgent;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Starting agent initialization...");

            // Initialize agents (cache and warm-up)
            await _lightAgent.InitializeAsync(stoppingToken).ConfigureAwait(false);
            await _orchestratorAgent.InitializeAsync(stoppingToken).ConfigureAwait(false);
            await _generalAgent.InitializeAsync(stoppingToken).ConfigureAwait(false);

            // Register available agents with the registry
            await _agentRegistry.RegisterAgentAsync(_lightAgent.GetAgentCard(), stoppingToken).ConfigureAwait(false);
            await _agentRegistry.RegisterAgentAsync(_orchestratorAgent.GetAgentCard(), stoppingToken).ConfigureAwait(false);
            await _agentRegistry.RegisterAgentAsync(_generalAgent.GetAgentCard(), stoppingToken).ConfigureAwait(false);
            
            _logger.LogInformation("Agent initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize agents");
            throw;
        }
    }
}
