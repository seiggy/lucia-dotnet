using A2A;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Agents;

public class GeneralAgent
{
    private readonly AgentCard _agent;
    private readonly ILogger<GeneralAgent> _logger;
    private readonly TaskManager _taskManager;
    private readonly AIAgent _aiAgent;

    public GeneralAgent(
        IChatClient chatClient,
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
                PushNotifications = true,
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

        var agentOptions = new ChatClientAgentOptions
        {
            Id = "general-assistant",
            Name = "general-assistant",
            Description = "Agent for answering general knowledge questions in Home Assistant",
            ChatOptions = new()
            {
                Instructions = instructions
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
        _logger.LogInformation("Initializing General Knowledge Agent...");
        
        _logger.LogInformation("General Knowledge initialized successfully");
    }
}