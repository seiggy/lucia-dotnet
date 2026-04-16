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
/// Specialized agent for querying sensor and binary_sensor entities in Home Assistant.
/// Provides read-only access to sensor data — temperature, humidity, motion, doors, battery, etc.
/// </summary>
public sealed class SensorAgent : ILuciaAgent, ISkillConfigProvider
{
    private const string AgentId = "sensor-agent";

    private readonly AgentCard _agent;
    private readonly SensorControlSkill _sensorSkill;
    private readonly IChatClientResolver _clientResolver;
    private readonly IAgentDefinitionRepository _definitionRepository;
    private readonly TracingChatClientFactory _tracingFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SensorAgent> _logger;
    private volatile AIAgent _aiAgent;
    private string? _lastEmbeddingProviderName;
    private DateTime? _lastConfigUpdate;

    /// <summary>
    /// The system instructions used by this agent.
    /// </summary>
    public string Instructions { get; set; }

    /// <summary>
    /// The AI tools available to this agent.
    /// </summary>
    public IList<AITool> Tools { get; }

    public SensorAgent(
        IChatClientResolver clientResolver,
        IAgentDefinitionRepository definitionRepository,
        SensorControlSkill sensorSkill,
        TracingChatClientFactory tracingFactory,
        ILoggerFactory loggerFactory)
    {
        _sensorSkill = sensorSkill;
        _clientResolver = clientResolver;
        _definitionRepository = definitionRepository;
        _tracingFactory = tracingFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<SensorAgent>();

        var sensorSkillCard = new AgentSkill()
        {
            Id = "id_sensor_agent",
            Name = "SensorControl",
            Description = "Skill for querying sensors and binary sensors in Home Assistant",
            Tags = ["sensor", "binary_sensor", "temperature", "humidity", "motion", "door", "window", "battery", "power", "illuminance", "home automation"],
            Examples = [
                "What's the temperature in the living room?",
                "Is the front door open?",
                "What's the humidity in the bedroom?",
                "Are there any motion sensors triggered?",
                "What's the battery level on the thermostat?",
                "Show me all sensors in the kitchen",
                "Is the garage door open?",
                "What's the power consumption right now?"
            ],
        };

        _agent = new AgentCard
        {
            SupportedInterfaces = [new AgentInterface { Url = "/a2a/sensor-agent" }],
            Name = AgentId,
            Description = "Agent for querying #sensors, #binary_sensors, #temperature, #humidity, #motion, #door, #window, #battery, and #power readings in Home Assistant",
            Capabilities = new AgentCapabilities
            {
                PushNotifications = false,
                Streaming = true,
            },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = [sensorSkillCard],
            Version = "1.0.0",
        };

        var instructions = """
                You are a specialized Sensor Agent for a home automation system.

                Your responsibilities:
                - Query sensor values (temperature, humidity, battery, power, illuminance, etc.)
                - Query binary sensor states (motion detected, door/window open/closed, presence, etc.)
                - Find sensors by name, area, or device type
                - Report current readings and states

                ## Available Tools
                - FindSensor: Find a sensor by name or description using natural language
                - FindSensorsByArea: Find all sensors in a specific area/room
                - GetSensorState: Get the current reading of a specific sensor entity
                - GetBinarySensorState: Get the current state of a binary sensor (on/off)
                - GetAreaSensors: Get sensors of a specific type in an area (optional device class filter)

                ## MANDATORY RULES — NEVER SKIP THESE
                1. You MUST call at least one tool function for EVERY request. NEVER respond based on assumptions.
                2. You do NOT know the current state or reading of any sensor. You MUST call a tool to check.
                3. NEVER say a sensor "is already on/off" or "reads X" without first calling the appropriate Get state tool.
                4. For queries about specific sensors: call FindSensor FIRST, then call the appropriate Get state tool.
                5. For area-based queries: use FindSensorsByArea or GetAreaSensors.

                ## Understanding Sensor vs Binary Sensor
                - Regular sensors (sensor.*) report numeric or text values (temperature, humidity, battery %, etc.)
                - Binary sensors (binary_sensor.*) report on/off states (motion, door open/closed, window, etc.)
                - Use GetSensorState for regular sensors and GetBinarySensorState for binary sensors.
                - When in doubt, check the entity ID prefix.

                ## Understanding User Context
                The orchestrator provides context about where the user is located. Use this
                to determine which area's sensors to query when the user doesn't specify.

                ## Response Format
                * Keep responses short and informative. Examples: "The living room is 72°F.", "The front door is closed.", "Kitchen humidity is 45%."
                * Do not offer additional assistance.
                * If you need clarification, end your response with '?'.
                * Focus only on sensor queries — if asked about controlling devices,
                  politely indicate that another agent handles those functions.
                """;

        Instructions = instructions;

        // Propagate agent ID to skill for trace filtering
        _sensorSkill.AgentId = AgentId;
        Tools = _sensorSkill.GetTools();

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
            SectionName = Configuration.UserConfiguration.SensorControlSkillOptions.SectionName,
            DisplayName = "Sensor Control",
            OptionsType = typeof(Configuration.UserConfiguration.SensorControlSkillOptions)
        }
    ];

    /// <summary>
    /// Get the underlying AI agent for processing requests.
    /// </summary>
    public AIAgent GetAIAgent() => _aiAgent;

    /// <summary>
    /// Initialize the agent by pre-loading sensor caches.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing SensorAgent...");
        await _sensorSkill.InitializeAsync(cancellationToken).ConfigureAwait(false);

        await ApplyDefinitionAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("SensorAgent initialized successfully");
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
        var newEmbeddingName = definition?.EmbeddingProviderName;

        if (!string.IsNullOrEmpty(definition?.Instructions))
            Instructions = definition.Instructions;

        if (_lastConfigUpdate == null || _lastConfigUpdate < definition?.UpdatedAt)
        {
            var copilotAgent = await _clientResolver.ResolveAIAgentAsync(newConnectionName, cancellationToken).ConfigureAwait(false);
            _aiAgent = copilotAgent ?? BuildAgent(
                await _clientResolver.ResolveAsync(newConnectionName, cancellationToken).ConfigureAwait(false))
                .AsBuilder()
                .UseOpenTelemetry()
                .Build();
            _logger.LogInformation("SensorAgent: using model provider '{Provider}'", newConnectionName ?? "default-chat");
            _lastConfigUpdate = DateTime.Now;
        }

        if (!string.Equals(_lastEmbeddingProviderName, newEmbeddingName, StringComparison.Ordinal))
        {
            await _sensorSkill.UpdateEmbeddingProviderAsync(newEmbeddingName, cancellationToken).ConfigureAwait(false);
            _lastEmbeddingProviderName = newEmbeddingName;
        }
    }

    private AIAgent BuildAgent(IChatClient chatClient)
    {
        var traced = _tracingFactory.Wrap(chatClient, AgentId);
        var agentOptions = new ChatClientAgentOptions
        {
            Id = AgentId,
            Name = AgentId,
            Description = "Agent for querying sensors and binary sensors in Home Assistant",
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