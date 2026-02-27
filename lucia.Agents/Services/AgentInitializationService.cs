using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using lucia.Agents.Auth;
using lucia.Agents.Registry;
using lucia.Agents.Abstractions;
using lucia.Agents.Mcp;
using lucia.Agents.Services;
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
    private readonly IAgentDefinitionRepository _definitionRepository;
    private readonly IModelProviderRepository _providerRepository;
    private readonly IEntityLocationService _entityLocationService;
    private readonly IPresenceDetectionService _presenceDetectionService;
    private readonly IApiKeyService _apiKeyService;
    private readonly ConfigStoreWriter _configStore;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentInitializationService> _logger;
    private readonly IOptionsMonitor<HomeAssistantOptions> _haOptions;
    private readonly AgentInitializationStatus _initStatus;

    public AgentInitializationService(
        IAgentRegistry agentRegistry,
        IEnumerable<ILuciaAgent> agents,
        IAgentDefinitionRepository definitionRepository,
        IModelProviderRepository providerRepository,
        IEntityLocationService entityLocationService,
        IPresenceDetectionService presenceDetectionService,
        IApiKeyService apiKeyService,
        ConfigStoreWriter configStore,
        IConfiguration configuration,
        ILogger<AgentInitializationService> logger,
        IOptionsMonitor<HomeAssistantOptions> haOptions,
        AgentInitializationStatus initStatus)
    {
        _agentRegistry = agentRegistry;
        _agents = agents;
        _definitionRepository = definitionRepository;
        _providerRepository = providerRepository;
        _entityLocationService = entityLocationService;
        _presenceDetectionService = presenceDetectionService;
        _apiKeyService = apiKeyService;
        _configStore = configStore;
        _configuration = configuration;
        _logger = logger;
        _haOptions = haOptions;
        _initStatus = initStatus;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Seed setup from env (dashboard key, HA config, Lucia HA key) for headless deployments
        await _apiKeyService.SeedSetupFromEnvAsync(_configStore, _configuration, _logger, stoppingToken).ConfigureAwait(false);

        await WaitForHomeAssistantConfigurationAsync(stoppingToken).ConfigureAwait(false);

        // Seed default model providers from connection strings if upgrading
        await _providerRepository.SeedDefaultModelProvidersAsync(_configuration, _logger, stoppingToken).ConfigureAwait(false);

        // Seed MetaMCP from METAMCP_URL when configured (headless/Docker)
        await _definitionRepository.SeedMetaMcpFromConfigAsync(_configuration, _logger, stoppingToken).ConfigureAwait(false);

        // Wait for at least one chat provider to be configured (may come from wizard or seed)
        await WaitForChatProviderAsync(stoppingToken).ConfigureAwait(false);

        // Seed AgentDefinition documents for any built-in agents missing from MongoDB
        await _definitionRepository.SeedBuiltInAgentDefinitionsAsync(_agents, _logger, stoppingToken).ConfigureAwait(false);

        // Initialize entity location cache before agents (skills depend on it)
        try
        {
            await _entityLocationService.InitializeAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Entity location service initialization failed — skills will operate without location cache");
        }

        // Auto-discover presence sensors now that entity locations are loaded
        try
        {
            await _presenceDetectionService.RefreshSensorMappingsAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Presence sensor scan failed — presence detection will be unavailable until manually refreshed");
        }

        _logger.LogInformation("Starting agent initialization...");

        var succeeded = 0;
        var failed = 0;

        foreach (var agent in _agents)
        {
            var agentName = agent.GetAgentCard().Name;

            if (await TryInitializeAgentAsync(agent, agentName, stoppingToken).ConfigureAwait(false))
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

        // Signal readiness so health checks (and Aspire WaitFor) can proceed
        _initStatus.MarkReady();
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

    private async Task WaitForChatProviderAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var providers = await _providerRepository.GetEnabledProvidersAsync(stoppingToken).ConfigureAwait(false);
            if (providers.Any(p => p.Purpose == Configuration.ModelPurpose.Chat))
            {
                _logger.LogInformation("Chat model provider detected. Proceeding with agent initialization.");
                return;
            }

            _logger.LogInformation(
                "Waiting for a chat model provider... (Configure one in the dashboard at /model-providers to continue.)");
            await Task.Delay(ConfigPollInterval, stoppingToken).ConfigureAwait(false);
        }
    }
}
