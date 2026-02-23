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
/// Specialized agent for controlling HVAC systems and fans in Home Assistant.
/// Handles climate entities (thermostats, AC units) and fan entities (ceiling fans, portable fans).
/// </summary>
public sealed class ClimateAgent : ILuciaAgent
{
    private const string AgentId = "climate-agent";

    private readonly AgentCard _agent;
    private readonly ClimateControlSkill _climateSkill;
    private readonly FanControlSkill _fanSkill;
    private readonly IChatClientResolver _clientResolver;
    private readonly IAgentDefinitionRepository _definitionRepository;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ClimateAgent> _logger;
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

    public ClimateAgent(
        IChatClient defaultChatClient,
        IChatClientResolver clientResolver,
        IAgentDefinitionRepository definitionRepository,
        ClimateControlSkill climateSkill,
        FanControlSkill fanSkill,
        ILoggerFactory loggerFactory)
    {
        _climateSkill = climateSkill;
        _fanSkill = fanSkill;
        _clientResolver = clientResolver;
        _definitionRepository = definitionRepository;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ClimateAgent>();

        var climateControlSkillCard = new AgentSkill()
        {
            Id = "id_climate_agent",
            Name = "ClimateControl",
            Description = "Skill for controlling HVAC systems, thermostats, and fans in Home Assistant",
            Tags = ["climate", "hvac", "thermostat", "fan", "temperature", "heating", "cooling", "home automation"],
            Examples = [
                "Set the thermostat to 72 degrees",
                "I'm cold",
                "It's too hot in here",
                "Turn on the bedroom fan",
                "What's the temperature downstairs?",
                "Set the fan to 50% speed",
                "Turn off all the fans",
                "Switch the heat to cool mode",
                "Set the humidity to 45%"
            ],
        };

        _agent = new AgentCard
        {
            Url = "/a2a/climate-agent",
            Name = AgentId,
            Description = "Agent for controlling #climate, #HVAC, #thermostats, #temperature, and #fans in Home Assistant",
            Capabilities = new AgentCapabilities
            {
                PushNotifications = false,
                StateTransitionHistory = true,
                Streaming = true,
            },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = [climateControlSkillCard],
            Version = "1.0.0",
        };

        var instructions = """
                You are a specialized Climate Control Agent for a home automation system.

                Your responsibilities:
                - Control HVAC systems (set temperature, change modes like heat/cool/auto/off)
                - Control fans (turn on/off, adjust speed, set direction)
                - Monitor temperature, humidity, and comfort levels
                - Respond to comfort-related requests using natural language

                ## Available Tools
                ### Climate/HVAC Tools:
                - FindClimateDevice: Find an HVAC/thermostat by name or description
                - FindClimateDevicesByArea: Find all climate devices in a specific area
                - GetClimateState: Get current state (temperature, mode, humidity, etc.)
                - SetClimateTemperature: Set the target temperature
                - SetClimateHvacMode: Set the HVAC mode (heat, cool, auto, off, heat_cool, dry, fan_only)
                - SetClimateFanMode: Set the HVAC fan mode (auto, low, medium, high, on)
                - SetClimateHumidity: Set the target humidity percentage
                - SetClimatePresetMode: Set the HVAC preset mode (e.g., none, sleep, eco, away)
                - SetClimateSwingMode: Set the HVAC swing/vane mode (e.g., on, off, Auto, Position 1-5)
                - GetComfortAdjustment: Get the configured comfort adjustment value in °F

                ### Fan Tools:
                - FindFan: Find a fan by name or description
                - FindFansByArea: Find all fans in a specific area
                - GetFanState: Get current fan state (on/off, speed, direction, oscillation)
                - SetFanState: Turn a fan on or off
                - SetFanSpeed: Set fan speed as a percentage (0-100)
                - SetFanDirection: Set fan direction (forward/reverse)
                - SetFanOscillation: Toggle fan oscillation on or off
                - SetFanPresetMode: Set a fan's preset mode (e.g., auto, nature, sleep)

                ## MANDATORY RULES — NEVER SKIP THESE
                1. You MUST call at least one tool function for EVERY request. NEVER respond based on assumptions.
                2. You do NOT know the current state of any device. You MUST call a tool to check.
                3. NEVER say a device "is already off/on" without first calling the appropriate Get state tool.
                4. For control requests: call Find FIRST to get entity IDs, then call the appropriate control tool.
                5. For status questions: call Find FIRST, then call the appropriate Get state tool.

                ## Handling Comfort Requests
                When users express comfort with natural language:
                - "I'm cold" or "it's chilly": Call GetComfortAdjustment to get the adjustment value,
                  then find the climate device in the user's area, check its current state,
                  and INCREASE the target temperature by the comfort adjustment value.
                  Also consider turning on heat mode if it's in cool/off mode.
                - "I'm hot" or "it's warm": Call GetComfortAdjustment to get the adjustment value,
                  then find the climate device in the user's area, check its current state,
                  and DECREASE the target temperature by the comfort adjustment value.
                  Also consider turning on fans in the area.
                - "I'm comfortable" or "it's perfect": Acknowledge and take no action.

                ## Understanding User Context
                The orchestrator provides context about where the user is located. Use this
                to determine which area's devices to control when the user doesn't specify.
                For example, if the user says "I'm cold" and they're in the living room,
                find and adjust the climate device that serves the living room.

                ## Fan Direction Guidance
                - Forward (counter-clockwise): Creates a wind-chill effect, good for summer cooling
                - Reverse (clockwise): Pushes warm air down from ceiling, good for winter heating

                ## Response Format
                * Keep responses short and informative. Examples: "I've set the thermostat to 72°F.",
                  "The living room is currently 68°F with heat mode active."
                * Do not offer additional assistance.
                * If you need clarification, end your response with '?'.
                * Focus only on climate and fan control — if asked about other features,
                  politely indicate that another agent handles those functions.
                """;

        Instructions = instructions;

        // Combine tools from both skills
        var allTools = new List<AITool>();
        allTools.AddRange(_climateSkill.GetTools());
        allTools.AddRange(_fanSkill.GetTools());
        Tools = allTools;

        // Build initial agent with the default client; InitializeAsync may replace it
        _aiAgent = BuildAgent(defaultChatClient);
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
    /// Initialize the agent by pre-loading climate and fan caches.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing ClimateAgent...");
        await Task.WhenAll(
            _climateSkill.InitializeAsync(cancellationToken),
            _fanSkill.InitializeAsync(cancellationToken)).ConfigureAwait(false);

        await ApplyDefinitionAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("ClimateAgent initialized successfully");
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

        if (!string.Equals(_lastModelConnectionName, newConnectionName, StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(newConnectionName))
            {
                var client = await _clientResolver.ResolveAsync(newConnectionName, cancellationToken).ConfigureAwait(false);
                _aiAgent = BuildAgent(client);
                _logger.LogInformation("ClimateAgent: using model provider '{Provider}'", newConnectionName);
            }

            _lastModelConnectionName = newConnectionName;
        }

        if (!string.Equals(_lastEmbeddingProviderName, newEmbeddingName, StringComparison.Ordinal))
        {
            await Task.WhenAll(
                _climateSkill.UpdateEmbeddingProviderAsync(newEmbeddingName, cancellationToken),
                _fanSkill.UpdateEmbeddingProviderAsync(newEmbeddingName, cancellationToken)).ConfigureAwait(false);
            _lastEmbeddingProviderName = newEmbeddingName;
        }
    }

    private ChatClientAgent BuildAgent(IChatClient chatClient)
    {
        var agentOptions = new ChatClientAgentOptions
        {
            Id = AgentId,
            Name = AgentId,
            Description = "Agent for controlling HVAC systems and fans in Home Assistant",
            ChatOptions = new ChatOptions
            {
                Instructions = Instructions,
                Tools = Tools,
                ToolMode = ChatToolMode.RequireAny
            }
        };

        return new ChatClientAgent(chatClient, agentOptions, _loggerFactory);
    }
}
