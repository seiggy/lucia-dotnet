using A2A;
using lucia.Agents.Integration;
using lucia.Agents.Orchestration;
using lucia.Agents.Registry;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.A2A;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Agents;

/// <summary>
/// Agent wrapper that exposes the LuciaOrchestrator through the AIAgent interface for Home Assistant integration.
/// This agent handles intelligent routing of user requests to appropriate specialized agents.
/// </summary>
public class OrchestratorAgent
{
    private readonly AgentCard _agent;
    private readonly ILogger<OrchestratorAgent> _logger;
    private readonly AIAgent _aiAgent;
    private readonly TaskManager _taskManager;
    private IServer _server;

    public OrchestratorAgent(
        IChatClient chatClient,
        IAgentRegistry agentRegistry,
        IOptions<RouterExecutorOptions> routerExecutorOptions,
        IAgentThreadFactory threadFactory,
        IServer server,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<OrchestratorAgent>();
        _server = server;

        var orchestrationSkill = new AgentSkill
        {
            Id = "id_orchestrator",
            Name = "Orchestration",
            Description = "Intelligent routing and coordination of requests across multiple specialized agents",
            Tags = ["orchestration", "routing", "multi-agent", "coordination"],
            Examples =
            [
                "Turn on the kitchen lights",
                "Play some music in the living room",
                "What's the status of the bedroom light?",
                "Dim the office lights and play relaxing music"
            ]
        };

        var agents = agentRegistry.GetAllAgentsAsync()
            .GetAwaiter()
            .GetResult();

        var tools = agents
            .Select(agent => (AITool)agent.GetAIAgent().AsAIFunction()).ToList();
        
        var serverAddressesFeature = _server?.Features?.Get<IServerAddressesFeature>();
        string agentUrl;
        if (serverAddressesFeature?.Addresses != null && serverAddressesFeature.Addresses.Any())
        {
            agentUrl = serverAddressesFeature.Addresses.First();
        }
        else
        {
            agentUrl = "unknown";
        }

        _agent = new AgentCard
        {
            Url = agentUrl + "/agent",
            Name = "orchestrator",
            Description = "Intelligent #orchestrator that #routes requests to specialized agents based on intent and capabilities",
            Capabilities = new AgentCapabilities
            {
                PushNotifications = true,
                StateTransitionHistory = true,
                Streaming = true
            },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = [orchestrationSkill],
            Version = "1.0.0"
        };

        var agentOptions = new ChatClientAgentOptions(routerExecutorOptions.Value.SystemPrompt)
        {
            Id = "orchestrator",
            Name = "Orchestrator",
            Description = "Orchestrator for Lucia",
            ChatOptions = new()
            {
                Tools = tools
            }
        };

        // Create the custom AIAgent implementation that delegates to orchestrator
        _aiAgent = new ChatClientAgent(
            chatClient,
            agentOptions,
            loggerFactory);

        _taskManager = new TaskManager();
    }

    /// <summary>
    /// Get the agent card for registration with the registry and A2A endpoints.
    /// </summary>
    public AgentCard GetAgentCard() => _agent;

    /// <summary>
    /// Get the underlying AI agent for processing requests.
    /// </summary>
    public AIAgent GetAIAgent() => _aiAgent;

    /// <summary>
    /// Initialize the agent.
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing OrchestratorAgent...");
        var serverAddressesFeature = _server?.Features?.Get<IServerAddressesFeature>();
        string agentUrl;
        if (serverAddressesFeature?.Addresses != null && serverAddressesFeature.Addresses.Any())
        {
            agentUrl = serverAddressesFeature.Addresses.First();
        }
        else
        {
            agentUrl = "unknown";
        }
        _agent.Url = agentUrl + "/agent";
        _logger.LogInformation("OrchestratorAgent initialized successfully");
        return Task.CompletedTask;
    }
}
