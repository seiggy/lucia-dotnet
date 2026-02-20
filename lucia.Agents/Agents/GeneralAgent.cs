using System.Diagnostics;
using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Orchestration;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Agents;

public class GeneralAgent : ILuciaAgent
{
    private static readonly ActivitySource ActivitySource = new("Lucia.Agents.General", "1.0.0");

    private readonly AgentCard _agent;
    private readonly ILogger<GeneralAgent> _logger;
    private readonly TaskManager _taskManager;
    private readonly AIAgent _aiAgent;

    /// <summary>
    /// The system instructions used by this agent.
    /// </summary>
    public string Instructions { get; }

    /// <summary>
    /// The AI tools available to this agent (empty for GeneralAgent).
    /// </summary>
    public IList<AITool> Tools { get; }

    public GeneralAgent(
        [FromKeyedServices(OrchestratorServiceKeys.GeneralModel)] IChatClient chatClient,
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<GeneralAgent>();

        // Create the agent card for registration
        _agent = new AgentCard
        {
            Url = "/a2a/general-assistant",
            Name = "general-assistant",
            Description = "Agent for handling #general-knowledge questions in Home Assistant",
            Capabilities = new AgentCapabilities
            {
                PushNotifications = false,
                StateTransitionHistory = true,
                Streaming = true,
            },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = [],
            Version = "1.0.0",
        };

        var instructions = """
                You are a specialized general knowledge agent for a Home Assistant platform.

                Your responsibilities:
                - Be informative and friendly.
                - Answer questions to the best of your ability, but don't invent facts or make up knowledge
                - If you do not know the answer, simply state in your response "I do not know."
                - Try to answer the user's request to the best of your ability. Keep your response short
                    enough to be about 6-10 seconds of audio. Roughly about 2 sentences at most.

                ## IMPORTANT
                * Keep your responses short. Aim for about 2 sentences max.
                * Do not offer to provide other assistance.
                """;

        Instructions = instructions;
        Tools = new List<AITool>();

        var agentOptions = new ChatClientAgentOptions
        {
            Id = "general-assistant",
            Name = "general-assistant",
            Description = "Agent for answering general knowledge questions in Home Assistant",
            ChatOptions = new()
            {
                Instructions = Instructions
            }
        };

        _aiAgent = new ChatClientAgent(chatClient, agentOptions, loggerFactory);
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
    /// Initialize the agent
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("GeneralAgent.Initialize", ActivityKind.Internal);
        _logger.LogInformation("Initializing General Knowledge Agent...");
        
        activity?.SetTag("agent.id", "general-assistant");
        activity?.SetStatus(ActivityStatusCode.Ok);
        _logger.LogInformation("General Knowledge initialized successfully");
    }
}