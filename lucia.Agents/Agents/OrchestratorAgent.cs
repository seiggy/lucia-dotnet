using A2A;
using lucia.Agents.Integration;
using lucia.Agents.Orchestration;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.A2A;
using Microsoft.Extensions.Logging;

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

    public OrchestratorAgent(
        LuciaOrchestrator orchestrator,
        IAgentThreadFactory threadFactory,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<OrchestratorAgent>();

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

        _agent = new AgentCard
        {
            Url = "/a2a/orchestrator",
            Name = "orchestrator",
            Description = "Intelligent orchestrator that routes requests to specialized agents based on intent and capabilities",
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

        // Create the custom AIAgent implementation that delegates to orchestrator
        _aiAgent = new OrchestratorAIAgent(
            orchestrator,
            threadFactory,
            name: "orchestrator",
            description: "Intelligent orchestrator for multi-agent coordination");

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
        _logger.LogInformation("OrchestratorAgent initialized successfully");
        return Task.CompletedTask;
    }
}
