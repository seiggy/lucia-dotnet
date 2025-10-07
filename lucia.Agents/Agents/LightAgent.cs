using A2A;
using Microsoft.Extensions.Logging;
using lucia.Agents.Skills;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;

namespace lucia.Agents.Agents;

/// <summary>
/// Specialized agent for controlling lights in Home Assistant
/// </summary>
public class LightAgent
{
    private readonly AgentCard _agent;
    private readonly LightControlSkill _lightPlugin;
    private readonly ILogger<LightAgent> _logger;
    private readonly TaskManager _taskManager;
    private readonly AIAgent _aiAgent;

    public LightAgent(
        IChatClient chatClient,
        LightControlSkill lightPlugin,
        ILoggerFactory loggerFactory)
    {
        _lightPlugin = lightPlugin;
        _logger = loggerFactory.CreateLogger<LightAgent>();

        var lightControlSkill = new AgentSkill()
        {
            Id = "id_light_agent",
            Name = "LightControl",
            Description = "Skill for controlling lights and lighting in Home Assistant",
            Tags = ["light", "lighting", "home automation", "semantic-kernel"],
            Examples = [
                "Turn on the living room light",
                "Dim the bedroom lamp to 50%",
                "Set the kitchen lights to blue",
                "What is the status of the hallway light?",
                "Find the light in the dining room and turn it off"
            ],
        };

        // Create the agent card for registration
        _agent = new AgentCard
        {
            Url = "/light-agent",
            Name = "LightAgent",
            Description = "Agent for controlling lights and lighting in Home Assistant",
            Capabilities = new AgentCapabilities
            {
                PushNotifications = true,
                StateTransitionHistory = true,
                Streaming = true,
            },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = [lightControlSkill],
            Version = "1.0.0",
        };

        var instructions = """
                You are a specialized Light Control Agent for a home automation system.

                Your responsibilities:
                - Control lights and light switches (turn on/off, dimming, color changes)
                - Monitor light status and state
                - Handle lighting scenes and automation
                - Respond to questions about light status

                You have access to these light control functions:
                - find_light: Find a light entity by name or description using natural language
                - get_light_state: Get the current state of a specific light
                - set_light_state: Control a light (on/off, brightness, color)

                IMPORTANT: When users refer to lights by common names like "living room light", "kitchen lights",
                or "bedroom lamp", ALWAYS use the find_light function first to get the correct entity ID,
                then use that entity ID for get_light_state or set_light_state operations.

                Always be helpful and provide clear feedback about light operations.
                When controlling lights, confirm the action was successful.

                Focus only on lighting - if asked about other home automation features,
                politely indicate that another agent handles those functions.
                """;

        var agentOptions = new ChatClientAgentOptions(instructions)
        {
            Id = "light-agent",
            Name = "Light Control Agent",
            Description = "Agent for controlling lights in Home Assistant"
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
        _logger.LogInformation("Initializing LightAgent...");
        await _lightPlugin.InitializeAsync(cancellationToken);
        _logger.LogInformation("LightAgent initialized successfully");
    }
}
