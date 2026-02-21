
using A2A;
using lucia.A2AHost.AgentRegistry;
using lucia.Agents.Abstractions;
using lucia.HomeAssistant.Configuration;
using Microsoft.Extensions.Options;

namespace lucia.A2AHost.Services
{
    public sealed class AgentHostService : IHostedLifecycleService
    {
        private const int MaxRetries = 3;
        private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ConfigPollInterval = TimeSpan.FromSeconds(10);

        private readonly ILogger<AgentHostService> _logger;
        private readonly AgentRegistryClient _agentRegistryClient;
        private readonly List<ILuciaAgent> _hostedAgents;
        private readonly IOptionsMonitor<HomeAssistantOptions> _haOptions;

        public AgentHostService(
            ILogger<AgentHostService> logger,
            AgentRegistryClient agentRegistryClient,
            IServiceProvider serviceProvider,
            IOptionsMonitor<HomeAssistantOptions> haOptions)
        {
            _logger = logger;
            _agentRegistryClient = agentRegistryClient;
            _haOptions = haOptions;

            _hostedAgents = serviceProvider.GetServices<ILuciaAgent>()
                .ToList();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task StartedAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("AgentHostService has started.");

            await WaitForHomeAssistantConfigurationAsync(cancellationToken);

            foreach (var agent in _hostedAgents)
            {
                var agentName = agent.GetType().Name;

                if (!await TryInitializeAgentAsync(agent, agentName, cancellationToken))
                    continue;

                await TryRegisterAgentAsync(agent, agentName, cancellationToken);
            }
        }

        public Task StartingAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task StoppedAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task StoppingAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("AgentHostService is stopping.");
            foreach (var agent in _hostedAgents)
            {
                await _agentRegistryClient.UnregisterAgentAsync(agent.GetAgentCard(), cancellationToken);
            }
        }

        private async Task WaitForHomeAssistantConfigurationAsync(CancellationToken cancellationToken)
        {
            while (!IsHomeAssistantConfigured())
            {
                _logger.LogInformation(
                    "Waiting for Home Assistant configuration... (BaseUrl and AccessToken must be set. Complete the setup wizard to continue.)");
                await Task.Delay(ConfigPollInterval, cancellationToken);
            }

            _logger.LogInformation("Home Assistant configuration detected. Proceeding with agent initialization.");
        }

        private bool IsHomeAssistantConfigured()
        {
            var options = _haOptions.CurrentValue;
            return !string.IsNullOrWhiteSpace(options.BaseUrl)
                && !string.IsNullOrWhiteSpace(options.AccessToken);
        }

        private async Task<bool> TryInitializeAgentAsync(ILuciaAgent agent, string agentName, CancellationToken cancellationToken)
        {
            for (var attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    await agent.InitializeAsync(cancellationToken);
                    _logger.LogInformation("Initialized agent: {AgentName}", agentName);
                    return true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
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
                        await Task.Delay(delay, cancellationToken);
                    }
                    else
                    {
                        _logger.LogError(ex,
                            "Failed to initialize agent {AgentName} after {MaxRetries} attempts. Skipping registration.",
                            agentName, MaxRetries);
                    }
                }
            }

            return false;
        }

        private async Task TryRegisterAgentAsync(ILuciaAgent agent, string agentName, CancellationToken cancellationToken)
        {
            for (var attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    await _agentRegistryClient.RegisterAgentAsync(agent.GetAgentCard(), cancellationToken);
                    _logger.LogInformation("Registered agent: {AgentName}", agentName);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt < MaxRetries)
                    {
                        var delay = InitialRetryDelay * Math.Pow(2, attempt - 1);
                        _logger.LogWarning(ex,
                            "Failed to register agent {AgentName} (attempt {Attempt}/{MaxRetries}). Retrying in {Delay}s...",
                            agentName, attempt, MaxRetries, delay.TotalSeconds);
                        await Task.Delay(delay, cancellationToken);
                    }
                    else
                    {
                        _logger.LogError(ex,
                            "Failed to register agent {AgentName} after {MaxRetries} attempts. Continuing with remaining agents.",
                            agentName, MaxRetries);
                    }
                }
            }
        }
    }
}
