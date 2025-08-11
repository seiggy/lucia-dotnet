using A2A;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.Extensions.Logging;
using lucia.Agents.Plugins;

namespace lucia.Agents.Agents;

/// <summary>
/// Specialized agent for controlling lights in Home Assistant
/// </summary>
public class LightAgent
{
    private readonly AgentCard _agent;
    private readonly Agent _skAgent;
    private readonly LightControlPlugin _lightPlugin;
    private readonly ILogger<LightAgent> _logger;

    public LightAgent(
        Kernel kernel,
        LightControlPlugin lightPlugin,
        ILogger<LightAgent> logger)
    {
        _lightPlugin = lightPlugin;
        _logger = logger;

        // Create the agent card for registration
        _agent = new AgentCard
        {
            Url = "lucia://light-agent",
            Name = "LightAgent",
            Description = "Agent for controlling lights and lighting in Home Assistant",
            Capabilities = new AgentCapabilities
            {
                PushNotifications = false,
                StateTransitionHistory = false,
                Streaming = false,
            },
            Version = "1.0.0",
        };
        
        // Create the internal ChatCompletionAgent
        _skAgent = new ChatCompletionAgent
        {
            Id = "light-agent",
            Name = "LightAgent", 
            Description = "Specialized agent for controlling lights and lighting in Home Assistant",
            Instructions = """
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
                """,
            Kernel = kernel
        };

        // Register the light control plugin with the kernel
        var kernelPlugin = KernelPluginFactory.CreateFromObject(lightPlugin, "LightControl");
        kernel.Plugins.Add(kernelPlugin);
    }

    /// <summary>
    /// Get the underlying ChatCompletionAgent for registration
    /// </summary>
    public AgentCard GetAgent() => _agent;

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