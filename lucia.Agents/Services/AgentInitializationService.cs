using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using lucia.Agents.Registry;
using lucia.Agents.Abstractions;

namespace lucia.Agents.Extensions;

/// <summary>
/// Background service that initializes and registers all agents when the application starts
/// </summary>
public class AgentInitializationService : BackgroundService
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly IEnumerable<ILuciaAgent> _agents;
    private readonly ILogger<AgentInitializationService> _logger;

    public AgentInitializationService(
        IAgentRegistry agentRegistry,
        IEnumerable<ILuciaAgent> agents,
        ILogger<AgentInitializationService> logger)
    {
        _agentRegistry = agentRegistry;
        _agents = agents;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Starting agent initialization...");

            foreach (var agent in _agents)
            {
                await agent.InitializeAsync(stoppingToken).ConfigureAwait(false);
                await _agentRegistry.RegisterAgentAsync(agent.GetAgentCard(), stoppingToken).ConfigureAwait(false);
                _logger.LogInformation("Initialized and registered agent: {AgentName}", agent.GetAgentCard().Name);
            }

            _logger.LogInformation("Agent initialization completed successfully — {Count} agent(s) registered", _agents.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize agents");
            throw;
        }
    }
}
