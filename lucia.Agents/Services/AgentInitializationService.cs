using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using lucia.Agents.Registry;
using lucia.Agents.Abstractions;
using lucia.HomeAssistant.Configuration;

namespace lucia.Agents.Extensions;

/// <summary>
/// Background service that initializes and registers all agents when the application starts.
/// Individual agent failures are logged but do not prevent other agents from initializing.
/// </summary>
public class AgentInitializationService : BackgroundService
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ConfigPollInterval = TimeSpan.FromSeconds(10);

    private readonly IAgentRegistry _agentRegistry;
    private readonly IEnumerable<ILuciaAgent> _agents;
    private readonly ILogger<AgentInitializationService> _logger;
    private readonly IOptionsMonitor<HomeAssistantOptions> _haOptions;

    public AgentInitializationService(
        IAgentRegistry agentRegistry,
        IEnumerable<ILuciaAgent> agents,
        ILogger<AgentInitializationService> logger,
        IOptionsMonitor<HomeAssistantOptions> haOptions)
    {
        _agentRegistry = agentRegistry;
        _agents = agents;
        _logger = logger;
        _haOptions = haOptions;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForHomeAssistantConfigurationAsync(stoppingToken);

        _logger.LogInformation("Starting agent initialization...");

        var succeeded = 0;
        var failed = 0;

        foreach (var agent in _agents)
        {
            var agentName = agent.GetAgentCard().Name;

            if (await TryInitializeAgentAsync(agent, agentName, stoppingToken))
            {
                succeeded++;
            }
            else
            {
                failed++;
            }
        }

        if (failed > 0)
        {
            _logger.LogWarning(
                "Agent initialization completed with errors — {Succeeded} agent(s) registered, {Failed} agent(s) failed. " +
                "Failed agents may recover on their next cache refresh cycle.",
                succeeded, failed);
        }
        else
        {
            _logger.LogInformation(
                "Agent initialization completed successfully — {Count} agent(s) registered",
                succeeded);
        }
    }

    private async Task<bool> TryInitializeAgentAsync(ILuciaAgent agent, string agentName, CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await agent.InitializeAsync(stoppingToken).ConfigureAwait(false);
                await _agentRegistry.RegisterAgentAsync(agent.GetAgentCard(), stoppingToken).ConfigureAwait(false);
                _logger.LogInformation("Initialized and registered agent: {AgentName}", agentName);
                return true;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Agent initialization cancelled for {AgentName}", agentName);
                return false;
            }
            catch (Exception ex)
            {
                if (attempt < MaxRetries)
                {
                    var delay = InitialRetryDelay * Math.Pow(2, attempt - 1);
                    _logger.LogWarning(ex,
                        "Failed to initialize agent {AgentName} (attempt {Attempt}/{MaxRetries}). Retrying in {Delay}s...",
                        agentName, attempt, MaxRetries, delay.TotalSeconds);
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogError(ex,
                        "Failed to initialize agent {AgentName} after {MaxRetries} attempts. " +
                        "Agent will not be registered but host will continue.",
                        agentName, MaxRetries);
                }
            }
        }

        return false;
    }

    private async Task WaitForHomeAssistantConfigurationAsync(CancellationToken stoppingToken)
    {
        while (!IsHomeAssistantConfigured())
        {
            _logger.LogInformation(
                "Waiting for Home Assistant configuration... (BaseUrl and AccessToken must be set. Complete the setup wizard to continue.)");
            await Task.Delay(ConfigPollInterval, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Home Assistant configuration detected. Proceeding with agent initialization.");
    }

    private bool IsHomeAssistantConfigured()
    {
        var options = _haOptions.CurrentValue;
        return !string.IsNullOrWhiteSpace(options.BaseUrl)
            && !string.IsNullOrWhiteSpace(options.AccessToken);
    }
}
