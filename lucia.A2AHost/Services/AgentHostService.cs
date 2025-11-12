
using A2A;
using lucia.A2AHost.AgentRegistry;
using lucia.Agents.Agents;

namespace lucia.A2AHost.Services
{
    public sealed class AgentHostService : IHostedLifecycleService
    {
        private readonly ILogger<AgentHostService> _logger;
        private readonly AgentRegistryClient _agentRegistryClient;
        private readonly IAgent _hostedAgent;

        public AgentHostService(
            ILogger<AgentHostService> logger,
            AgentRegistryClient agentRegistryClient,
            IAgent hostedAgent)
        {
            _logger = logger;
            _agentRegistryClient = agentRegistryClient;
            _hostedAgent = hostedAgent;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task StartedAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("AgentHostService has started.");
            // register agent with registry
            await _agentRegistryClient.RegisterAgentAsync(_hostedAgent.GetAgentCard(), cancellationToken);
        }

        public async Task StartingAsync(CancellationToken cancellationToken)
        {
            await _hostedAgent.InitializeAsync(cancellationToken);
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
            await _agentRegistryClient.UnregisterAgentAsync(_hostedAgent.GetAgentCard(), cancellationToken);
        }
    }
}
