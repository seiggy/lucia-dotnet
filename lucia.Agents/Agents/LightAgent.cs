using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Mcp;
using lucia.Agents.Orchestration;
using lucia.Agents.Services;
using lucia.Agents.Skills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Agents;

/// <summary>
/// Specialized agent for controlling lights in Home Assistant
/// </summary>
public sealed class LightAgent : ILuciaAgent
{
    private const string AgentId = "light-agent";

    private readonly AgentCard _agent;
    private readonly LightControlSkill _lightPlugin;
    private readonly IChatClientResolver _clientResolver;
    private readonly IAgentDefinitionRepository _definitionRepository;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<LightAgent> _logger;
    private volatile AIAgent _aiAgent;
    private string? _lastModelConnectionName;
    private string? _lastEmbeddingProviderName;

    /// <summary>
    /// The system instructions used by this agent.
    /// </summary>
    public string Instructions { get; }

    /// <summary>
    /// The AI tools available to this agent.
    /// </summary>
    public IList<AITool> Tools { get; }

    public LightAgent(
        IChatClientResolver clientResolver,
        IAgentDefinitionRepository definitionRepository,
        LightControlSkill lightPlugin,
        ILoggerFactory loggerFactory)
    {
        _lightPlugin = lightPlugin;
        _clientResolver = clientResolver;
        _definitionRepository = definitionRepository;
        _loggerFactory = loggerFactory;
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
            Url = "/a2a/light-agent",
            Name = AgentId,
            Description = "Agent for controlling #lights and #lighting in Home Assistant",
            Capabilities = new AgentCapabilities
            {
                PushNotifications = false,
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
                - FindLight: Find a light entity by name or description using natural language
                - FindLightsByArea: Find a collection of lights by the area they exist in
                - GetLightState: Get the current state of a specific light
                - SetLightState: Control a light (on/off, brightness, color)

                ## MANDATORY RULES — NEVER SKIP THESE
                1. You MUST call at least one tool function for EVERY request. NEVER respond based on assumptions.
                2. You do NOT know the current state of any light. You MUST call a tool to check.
                3. NEVER say a light "is already off" or "is already on" without first calling GetLightState.
                4. For turn on/off requests: call FindLight or FindLightsByArea FIRST, then call SetLightState.
                5. For status questions: call FindLight or FindLightsByArea FIRST, then call GetLightState.

                ## How to find lights
                - When users refer to lights by common names like "living room light", "kitchen lights",
                    or "bedroom lamp", ALWAYS use the FindLight function first to get the correct entity ID,
                    then use that entity ID for GetLightState or SetLightState operations.
                - When users refer to an area, such as "living room" without specifying a light, then
                    use the FindLightsByArea function first to get all the lights in that area. If they reference
                    an area with the plurality of 'lights', they likely want all lights in that area, so you
                    should use FindLightsByArea instead of FindLight, as there may be more than one light
                    in the area the user wants controlled.

                ## Response format
                * Keep your responses short and informative only. Examples: "I've turned on the kitchen lights.", "I've set the office lights to red."
                * Do not offer to provide other assistance.
                * If you need to ask for user feedback, ensure your response ends in a '?'. Examples: "Did you mean the kitchen light?", "I'm sorry, I couldn't find the living room light; Is it known by another name?"
                * Focus only on lighting - if asked about other home automation features,
                  politely indicate that another agent handles those functions.
                """;

        Instructions = instructions;
        Tools = _lightPlugin.GetTools();

        // _aiAgent is built during InitializeAsync via ApplyDefinitionAsync
        _aiAgent = null!;
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
        await _lightPlugin.InitializeAsync(cancellationToken).ConfigureAwait(false);

        // Resolve per-agent model from AgentDefinition if configured
        await ApplyDefinitionAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("LightAgent initialized successfully");
    }

    /// <inheritdoc />
    public async Task RefreshConfigAsync(CancellationToken cancellationToken = default)
    {
        await ApplyDefinitionAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyDefinitionAsync(CancellationToken cancellationToken)
    {
        var definition = await _definitionRepository.GetAgentDefinitionAsync(AgentId, cancellationToken).ConfigureAwait(false);
        var newConnectionName = definition?.ModelConnectionName;
        var newEmbeddingName = definition?.EmbeddingProviderName;

        if (_aiAgent is null || !string.Equals(_lastModelConnectionName, newConnectionName, StringComparison.Ordinal))
        {
            // Copilot providers produce an AIAgent directly; others go through IChatClient
            var copilotAgent = await _clientResolver.ResolveAIAgentAsync(newConnectionName, cancellationToken).ConfigureAwait(false);
            _aiAgent = copilotAgent ?? BuildAgent(
                await _clientResolver.ResolveAsync(newConnectionName, cancellationToken).ConfigureAwait(false));
            _logger.LogInformation("LightAgent: using model provider '{Provider}'", newConnectionName ?? "default-chat");
            _lastModelConnectionName = newConnectionName;
        }

        if (!string.Equals(_lastEmbeddingProviderName, newEmbeddingName, StringComparison.Ordinal))
        {
            await _lightPlugin.UpdateEmbeddingProviderAsync(newEmbeddingName, cancellationToken).ConfigureAwait(false);
            _lastEmbeddingProviderName = newEmbeddingName;
        }
    }

    private ChatClientAgent BuildAgent(IChatClient chatClient)
    {
        var agentOptions = new ChatClientAgentOptions
        {
            Id = AgentId,
            Name = AgentId,
            Description = "Agent for controlling lights in Home Assistant",
            ChatOptions = new()
            {
                Instructions = Instructions,
                Tools = Tools,
                ToolMode = ChatToolMode.RequireAny
            }
        };

        return new ChatClientAgent(chatClient, agentOptions, _loggerFactory);
    }
}
