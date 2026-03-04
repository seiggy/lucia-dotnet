using System.Diagnostics;
using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Integration;
using lucia.Agents.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Agents;

public sealed class GeneralAgent : ILuciaAgent
{
    private const string AgentId = "general-assistant";
    private static readonly ActivitySource ActivitySource = new("Lucia.Agents.General", "1.0.0");

    private readonly AgentCard _agent;
    private readonly IChatClientResolver _clientResolver;
    private readonly IAgentDefinitionRepository _definitionRepository;
    private readonly IWebSearchSkill? _webSearchSkill;
    private readonly TracingChatClientFactory _tracingFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<GeneralAgent> _logger;
    private volatile AIAgent _aiAgent;
    private DateTime? _lastConfigUpdate;

    /// <summary>
    /// The system instructions used by this agent.
    /// </summary>
    public string Instructions { get; set; }

    /// <summary>
    /// The AI tools available to this agent (web search when a provider is registered).
    /// </summary>
    public IList<AITool> Tools { get; }

    public GeneralAgent(
        IChatClientResolver clientResolver,
        IAgentDefinitionRepository definitionRepository,
        TracingChatClientFactory tracingFactory,
        ILoggerFactory loggerFactory,
        IWebSearchSkill? webSearchSkill = null)
    {
        _clientResolver = clientResolver;
        _definitionRepository = definitionRepository;
        _webSearchSkill = webSearchSkill;
        _tracingFactory = tracingFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<GeneralAgent>();

        Tools = _webSearchSkill?.GetTools() ?? [];

        // Create the agent card for registration
        var skills = new List<AgentSkill>();
        if (Tools.Count > 0)
        {
            skills.Add(new AgentSkill
            {
                Id = "id_web_search",
                Name = "Web Search",
                Description = "Search the web for current events, news, and facts via SearXNG",
            });
        }

        _agent = new AgentCard
        {
            Url = "/a2a/general-assistant",
            Name = AgentId,
            Description = Tools.Count > 0
                ? "Agent for general knowledge and web search in Home Assistant"
                : "Agent for handling #general-knowledge questions in Home Assistant",
            Capabilities = new AgentCapabilities
            {
                PushNotifications = false,
                StateTransitionHistory = true,
                Streaming = true,
            },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = [.. skills],
            Version = "1.0.0",
        };

        var webSearchHint = Tools.Count > 0
            ? "\n- When asked about recent events, news, or facts that may change, use the web_search tool to find current information before answering."
            : "";

        var instructions = """
                You are a specialized general knowledge agent for a Home Assistant platform.

                Your responsibilities:
                - Be informative and friendly.
                - Answer questions to the best of your ability, but don't invent facts or make up knowledge
                - If you do not know the answer, simply state in your response "I do not know."
                - Try to answer the user's request to the best of your ability. Keep your response short
                    enough to be about 6-10 seconds of audio. Roughly about 2 sentences at most.
                """ + webSearchHint + """

                ## IMPORTANT
                * Keep your responses short. Aim for about 2 sentences max.
                * Do not offer to provide other assistance.
                """;

        Instructions = instructions;

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
        using var activity = ActivitySource.StartActivity();
        _logger.LogInformation("Initializing General Knowledge Agent...");

        if (_webSearchSkill is not null)
            await _webSearchSkill.InitializeAsync(cancellationToken).ConfigureAwait(false);

        await ApplyDefinitionAsync(cancellationToken).ConfigureAwait(false);

        activity?.SetTag("agent.id", AgentId);
        activity?.SetStatus(ActivityStatusCode.Ok);
        _logger.LogInformation("General Knowledge initialized successfully");
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

            var copilotAgent = await _clientResolver.ResolveAIAgentAsync(newConnectionName, cancellationToken)
                .ConfigureAwait(false);
            _aiAgent = copilotAgent ?? BuildAgent(
                    await _clientResolver.ResolveAsync(newConnectionName, cancellationToken).ConfigureAwait(false))
                .AsBuilder()
                .UseOpenTelemetry()
                .Build();
            _logger.LogInformation("GeneralAgent: using model provider '{Provider}'",
                newConnectionName ?? "default-chat");
            _lastConfigUpdate = DateTime.Now;
        }
    }

    private AIAgent BuildAgent(IChatClient chatClient)
    {
        var traced = _tracingFactory.Wrap(chatClient, AgentId);
        var chatOptions = new ChatOptions { Instructions = Instructions };
        if (Tools.Count > 0)
            chatOptions.Tools = [..Tools];

        var agentOptions = new ChatClientAgentOptions
        {
            Id = AgentId,
            Name = AgentId,
            Description = "Agent for answering general knowledge questions in Home Assistant",
            ChatOptions = chatOptions
        };

        return new ChatClientAgent(traced, agentOptions, _loggerFactory)
            .AsBuilder()
            .UseOpenTelemetry()
            .Build();
    }
}