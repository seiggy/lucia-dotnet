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
/// Specialized agent for Home Assistant security controls such as alarms, locks, and cameras.
/// </summary>
public sealed class SecurityAgent : ILuciaAgent, ISkillConfigProvider
{
    private const string AgentId = "security-agent";

    private readonly AgentCard _agent;
    private readonly SecurityControlSkill _securitySkill;
    private readonly IChatClientResolver _clientResolver;
    private readonly IAgentDefinitionRepository _definitionRepository;
    private readonly TracingChatClientFactory _tracingFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SecurityAgent> _logger;
    private volatile AIAgent _aiAgent;
    private DateTime? _lastConfigUpdate;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);

    /// <summary>
    /// The system instructions used by this agent.
    /// </summary>
    public string Instructions { get; set; }

    /// <summary>
    /// The AI tools available to this agent.
    /// </summary>
    public IList<AITool> Tools { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityAgent"/> class.
    /// </summary>
    public SecurityAgent(
        IChatClientResolver clientResolver,
        IAgentDefinitionRepository definitionRepository,
        SecurityControlSkill securitySkill,
        TracingChatClientFactory tracingFactory,
        ILoggerFactory loggerFactory)
    {
        _securitySkill = securitySkill;
        _clientResolver = clientResolver;
        _definitionRepository = definitionRepository;
        _tracingFactory = tracingFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<SecurityAgent>();

        var securitySkillCard = new AgentSkill()
        {
            Id = "id_security_agent",
            Name = "SecurityControl",
            Description = "Skill for controlling alarms, locks, and security status in Home Assistant",
            Tags = ["security", "alarm", "lock", "camera", "home automation"],
            Examples =
            [
                "Arm the alarm",
                "Lock the front door",
                "What's the security status?"
            ],
        };

        _agent = new AgentCard
        {
            SupportedInterfaces = [new AgentInterface { Url = "/a2a/security-agent" }],
            Name = AgentId,
            Description = "Agent for controlling #alarms, #locks, and #security status in Home Assistant",
            Capabilities = new AgentCapabilities
            {
                PushNotifications = false,
                Streaming = true,
            },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = [securitySkillCard],
            Version = "1.0.0",
        };

        Instructions = """
            You are a specialized Security Control Agent for a home automation system.

            Your responsibilities:
            - Arm and disarm alarm panels
            - Lock and unlock doors
            - Report the current security status for alarms, locks, and configured cameras

            ## Available Tools
            - ArmAlarm: Arm alarm panels by area. Use the caller-provided code when available.
            - DisarmAlarm: Disarm alarm panels by area. Use the caller-provided code when available.
            - LockDoor: Lock one or more doors by area or name.
            - UnlockDoor: Unlock one or more doors by area or name. Use the caller-provided code when available.
            - GetSecurityStatus: Query the current state of security devices.

            ## MANDATORY RULES
            1. You MUST call a tool for every request. Never assume current alarm, lock, or camera state.
            2. For arm, disarm, lock, and unlock requests, call the matching control tool directly.
            3. For status questions, call GetSecurityStatus.
            4. Use the user's own area names and device names. Do not invent entity IDs.
            5. If a request is ambiguous, ask a short clarifying question ending with '?'.
            6. Treat security actions carefully. Never claim a change succeeded unless the tool confirms it.

            ## Response Format
            - Keep responses short and factual.
            - Do not offer additional help or suggestions.
            - If nothing matches, say so and ask for clarification ending with '?'.
            - Focus only on security devices. Politely redirect unrelated home automation requests.
            """;

        _securitySkill.AgentId = AgentId;
        Tools = _securitySkill.GetTools();
        _aiAgent = null!;
    }

    /// <summary>
    /// Gets the agent card used for registration.
    /// </summary>
    public AgentCard GetAgentCard() => _agent;

    /// <inheritdoc/>
    public IReadOnlyList<SkillConfigSection> GetSkillConfigSections() =>
    [
        new()
        {
            SectionName = SecurityControlSkillOptions.SectionName,
            DisplayName = "Security Control",
            OptionsType = typeof(SecurityControlSkillOptions)
        }
    ];

    /// <summary>
    /// Gets the underlying AI agent.
    /// </summary>
    public AIAgent GetAIAgent() => _aiAgent;

    /// <summary>
    /// Initializes the security agent.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing SecurityAgent...");
        await _securitySkill.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await ApplyDefinitionAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("SecurityAgent initialized successfully");
    }

    /// <inheritdoc />
    public async Task RefreshConfigAsync(CancellationToken cancellationToken = default)
    {
        await ApplyDefinitionAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyDefinitionAsync(CancellationToken cancellationToken)
    {
        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var definition = await _definitionRepository.GetAgentDefinitionAsync(AgentId, cancellationToken).ConfigureAwait(false);
            var newConnectionName = definition?.ModelConnectionName;
            if (!string.IsNullOrEmpty(definition?.Instructions))
            {
                Instructions = definition.Instructions;
            }

            if (_aiAgent is null
                || definition is not null && (_lastConfigUpdate is null || _lastConfigUpdate < definition.UpdatedAt))
            {
                var copilotAgent = await _clientResolver.ResolveAIAgentAsync(newConnectionName, cancellationToken).ConfigureAwait(false);
                _aiAgent = copilotAgent ?? BuildAgent(
                    await _clientResolver.ResolveAsync(newConnectionName, cancellationToken).ConfigureAwait(false))
                    .AsBuilder()
                    .UseOpenTelemetry()
                    .Build();
                _logger.LogInformation("SecurityAgent: using model provider '{Provider}'", newConnectionName ?? "default-chat");
                _lastConfigUpdate = definition?.UpdatedAt;
            }
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private AIAgent BuildAgent(IChatClient chatClient)
    {
        var traced = _tracingFactory.Wrap(chatClient, AgentId);
        var agentOptions = new ChatClientAgentOptions
        {
            Id = AgentId,
            Name = AgentId,
            Description = "Agent for controlling Home Assistant security devices",
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
