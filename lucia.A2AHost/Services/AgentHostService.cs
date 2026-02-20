
using A2A;
using lucia.A2AHost.AgentRegistry;
using lucia.Agents.Abstractions;

namespace lucia.A2AHost.Services
{
    public sealed class AgentHostService : IHostedLifecycleService
    {
        private readonly ILogger<AgentHostService> _logger;
        private readonly AgentRegistryClient _agentRegistryClient;
        private readonly List<ILuciaAgent> _hostedAgents;

        public AgentHostService(
            ILogger<AgentHostService> logger,
            AgentRegistryClient agentRegistryClient,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _agentRegistryClient = agentRegistryClient;
            
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
            // register agents with registry — continue on individual failures
            foreach (var agent in _hostedAgents) 
            {
                try
                {
                    await agent.InitializeAsync(cancellationToken);
                    await _agentRegistryClient.RegisterAgentAsync(agent.GetAgentCard(), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize or register agent {AgentName}. Continuing with remaining agents.",
                        agent.GetType().Name);
                }
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
            // unregister agent
            foreach (var agent in _hostedAgents) 
            {
                await _agentRegistryClient.UnregisterAgentAsync(agent.GetAgentCard(), cancellationToken);
            }
        }
    }
}
