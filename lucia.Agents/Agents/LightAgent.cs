using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Integration;
using lucia.Agents.Services;
using lucia.Agents.Skills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Agents;

/// <summary>
/// Specialized agent for controlling lights in Home Assistant
/// </summary>
public sealed class LightAgent : ILuciaAgent, ISkillConfigProvider
{
    private const string AgentId = "light-agent";

    private readonly AgentCard _agent;
    private readonly LightControlSkill _lightPlugin;
    private readonly IChatClientResolver _clientResolver;
    private readonly IAgentDefinitionRepository _definitionRepository;
    private readonly TracingChatClientFactory _tracingFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<LightAgent> _logger;
    private volatile AIAgent _aiAgent;
    private DateTime? _lastConfigUpdate;

    /// <summary>
    /// The system instructions used by this agent.
    /// </summary>
    public string Instructions { get; set; }

    /// <summary>
    /// The AI tools available to this agent.
    /// </summary>
    public IList<AITool> Tools { get; }

    public LightAgent(
        IChatClientResolver clientResolver,
        IAgentDefinitionRepository definitionRepository,
        LightControlSkill lightPlugin,
        TracingChatClientFactory tracingFactory,
        ILoggerFactory loggerFactory)
    {
        _lightPlugin = lightPlugin;
        _clientResolver = clientResolver;
        _definitionRepository = definitionRepository;
        _tracingFactory = tracingFactory;
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
                - Report on light status when asked

                You have two tools:
                - GetLightsState: Find lights by name, area, or floor and return their current state
                - ControlLights: Find lights by name, area, or floor and set them to a new state (on/off, brightness, color)

                Both tools accept natural language search terms — you can use room names ("kitchen"),
                floor names ("upstairs"), or specific light names ("bedroom lamp"). You can pass
                multiple search terms at once to target lights across different locations.

                ## MANDATORY RULES
                1. You MUST call a tool for EVERY request. NEVER assume the state of any light.
                2. For control requests (turn on/off, dim, color): call ControlLights directly.
                   Do NOT call GetLightsState first — just send the desired state.
                3. For status questions ("are the lights on?"): call GetLightsState.
                4. Use the user's own words as search terms. Don't try to guess entity IDs.

                ## Response format
                * Keep responses short and informative. Examples: "Done — kitchen lights turned on at 50%.", "The living room light is on at 80% brightness."
                * Do not offer additional assistance or suggestions.
                * If no lights match, say so and ask for clarification ending with '?'.
                * Focus only on lighting — politely redirect other home automation requests.
                """;

        Instructions = instructions;
        _lightPlugin.AgentId = AgentId;
        Tools = _lightPlugin.GetTools();

        // _aiAgent is built during InitializeAsync via ApplyDefinitionAsync
        _aiAgent = null!;
    }

    /// <summary>
    /// Get the agent card for registration with the registry and A2A endpoints.
    /// </summary>
    public AgentCard GetAgentCard() => _agent;

    /// <inheritdoc/>
    public IReadOnlyList<SkillConfigSection> GetSkillConfigSections() =>
    [
        new()
        {
            SectionName = Configuration.LightControlSkillOptions.SectionName,
            DisplayName = "Light Control",
            OptionsType = typeof(Configuration.LightControlSkillOptions)
        }
    ];

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
        _lastConfigUpdate = DateTime.Now;
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
        if (!string.IsNullOrEmpty(definition?.Instructions))
            Instructions = definition.Instructions;
        
        if (_lastConfigUpdate == null || _lastConfigUpdate < definition?.UpdatedAt)
        {
            // Copilot providers produce an AIAgent directly; others go through IChatClient
            var copilotAgent = await _clientResolver.ResolveAIAgentAsync(newConnectionName, cancellationToken).ConfigureAwait(false);
            _aiAgent = copilotAgent ?? BuildAgent(
                await _clientResolver.ResolveAsync(newConnectionName, cancellationToken)
                    .ConfigureAwait(false))
                .AsBuilder()
                .UseOpenTelemetry()
                .Build();
            _logger.LogInformation("LightAgent: using model provider '{Provider}'", newConnectionName ?? "default-chat");
            _lastConfigUpdate = DateTime.Now;
        }
    }

    private AIAgent BuildAgent(IChatClient chatClient)
    {
        var traced = _tracingFactory.Wrap(chatClient, AgentId);
        var agentOptions = new ChatClientAgentOptions
        {
            Id = AgentId,
            Name = AgentId,
            Description = "Agent for controlling lights in Home Assistant",
            ChatOptions = new ChatOptions
            {
                Instructions = Instructions,
                Tools = Tools
            }
        };

        return new ChatClientAgent(traced, agentOptions, _loggerFactory)
            .AsBuilder()
            .UseOpenTelemetry()
            .Build();
    }
}
