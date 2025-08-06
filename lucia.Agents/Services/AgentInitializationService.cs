using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using lucia.Agents.Registry;
using lucia.Agents.Agents;

namespace lucia.Agents.Extensions;

/// <summary>
/// Background service that initializes and registers all agents when the application starts
/// </summary>
public class AgentInitializationService : BackgroundService
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly LightAgent _lightAgent;
    private readonly ILogger<AgentInitializationService> _logger;

    public AgentInitializationService(
        IAgentRegistry agentRegistry,
        LightAgent lightAgent,
        ILogger<AgentInitializationService> logger)
    {
        _agentRegistry = agentRegistry;
        _lightAgent = lightAgent;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Starting agent initialization...");

            // Initialize the LightAgent (this will cache light entities)
            await _lightAgent.InitializeAsync(stoppingToken);

            // Register the LightAgent with the registry
            await _agentRegistry.RegisterAgentAsync(_lightAgent.GetAgent(), stoppingToken);

            _logger.LogInformation("Agent initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize agents");
            throw;
        }
    }
}