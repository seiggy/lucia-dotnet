using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Skills;
using lucia.Agents.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Agents;

/// <summary>
/// Specialized agent for activating Home Assistant scenes.
/// </summary>
public sealed class SceneAgent : ILuciaAgent
{
    private const string AgentId = "scene-agent";

    private readonly AgentCard _agent;
    private readonly SceneControlSkill _sceneSkill;
    private readonly IChatClientResolver _clientResolver;
    private readonly IAgentDefinitionRepository _definitionRepository;
    private readonly TracingChatClientFactory _tracingFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SceneAgent> _logger;
    private volatile AIAgent _aiAgent;
    private string? _lastModelConnectionName;

    public string Instructions { get; }
    public IList<AITool> Tools { get; }

    public SceneAgent(
        IChatClientResolver clientResolver,
        IAgentDefinitionRepository definitionRepository,
        SceneControlSkill sceneSkill,
        TracingChatClientFactory tracingFactory,
        ILoggerFactory loggerFactory)
    {
        _sceneSkill = sceneSkill;
        _clientResolver = clientResolver;
        _definitionRepository = definitionRepository;
        _tracingFactory = tracingFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<SceneAgent>();

        var sceneControlSkill = new AgentSkill()
        {
            Id = "id_scene_agent",
            Name = "SceneControl",
            Description = "Skill for activating Home Assistant scenes",
            Tags = ["scene", "scenes", "home automation", "activation"],
            Examples = [
                "Activate the movie scene",
                "Turn on the romantic scene",
                "Switch to night mode",
                "What scenes are available?",
                "Activate the living room scene"
            ],
        };

        _agent = new AgentCard
        {
            Url = "/a2a/scene-agent",
            Name = AgentId,
            Description = "Agent for activating #scenes in Home Assistant",
            Capabilities = new AgentCapabilities
            {
                PushNotifications = false,
                StateTransitionHistory = true,
                Streaming = true,
            },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = [sceneControlSkill],
            Version = "1.0.0",
        };

        var instructions = """
                You are a specialized Scene Control Agent for a home automation system.

                Your responsibilities:
                - Activate Home Assistant scenes by name or entity ID
                - List available scenes
                - Find scenes by area/room

                You have access to these scene control functions:
                - ListScenesAsync: List all available scenes
                - FindScenesByAreaAsync: Find scenes in a specific area (e.g., living room, bedroom)
                - ActivateSceneAsync: Activate a scene by entity ID (e.g., scene.movie_mode)

                ## MANDATORY RULES
                1. For "activate scene X" requests: use ListScenesAsync or FindScenesByAreaAsync first to find the scene entity ID, then call ActivateSceneAsync with that entity ID.
                2. When the user mentions an area (e.g., "living room scene"), use FindScenesByAreaAsync to find scenes in that area.
                3. Scene entity IDs use the format scene.NAME (e.g., scene.movie_mode, scene.romantic). Pass the full entity ID to ActivateSceneAsync.

                ## Response format
                * Keep responses short and informative (e.g., "I've activated the movie scene.", "Scene 'Movie Mode' activated.").
                * Do not offer to provide other assistance.
                * Focus only on scenes — if asked about lights, climate, or other domains, politely indicate another agent handles those.
                """;

        Instructions = instructions;
        Tools = _sceneSkill.GetTools();
        _aiAgent = null!;
    }

    public AgentCard GetAgentCard() => _agent;
    public AIAgent GetAIAgent() => _aiAgent;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing SceneAgent...");
        await _sceneSkill.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await ApplyDefinitionAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("SceneAgent initialized successfully");
    }

    public async Task RefreshConfigAsync(CancellationToken cancellationToken = default)
    {
        await ApplyDefinitionAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyDefinitionAsync(CancellationToken cancellationToken)
    {
        var definition = await _definitionRepository.GetAgentDefinitionAsync(AgentId, cancellationToken).ConfigureAwait(false);
        var newConnectionName = definition?.ModelConnectionName;

        if (_aiAgent is not null && string.Equals(_lastModelConnectionName, newConnectionName, StringComparison.Ordinal))
            return;

        var copilotAgent = await _clientResolver.ResolveAIAgentAsync(newConnectionName, cancellationToken).ConfigureAwait(false);
        _aiAgent = copilotAgent ?? BuildAgent(
            await _clientResolver.ResolveAsync(newConnectionName, cancellationToken).ConfigureAwait(false))
            .AsBuilder()
            .UseOpenTelemetry()
            .Build();
        _logger.LogInformation("SceneAgent: using model provider '{Provider}'", newConnectionName ?? "default-chat");
        _lastModelConnectionName = newConnectionName;
    }

    private AIAgent BuildAgent(IChatClient chatClient)
    {
        var traced = _tracingFactory.Wrap(chatClient, AgentId);
        var agentOptions = new ChatClientAgentOptions
        {
            Id = AgentId,
            Name = AgentId,
            Description = "Agent for activating scenes in Home Assistant",
            ChatOptions = new()
            {
                Instructions = Instructions,
                Tools = Tools,
                ToolMode = ChatToolMode.RequireAny
            }
        };

        return new ChatClientAgent(traced, agentOptions, _loggerFactory)
            .AsBuilder()
            .UseOpenTelemetry()
            .Build();
    }
}
