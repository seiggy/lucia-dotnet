using System.Diagnostics;
using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Mcp;

/// <summary>
/// A user-defined agent that is constructed from its MongoDB definition
/// and resolved MCP tools. The agent is pre-built during initialization
/// and can be rebuilt on demand for hot-reload.
/// </summary>
public sealed class DynamicAgent : ILuciaAgent
{
    private static readonly ActivitySource ActivitySource = new("Lucia.Agents.Dynamic", "1.0.0");

    private readonly string _agentId;
    private readonly IAgentDefinitionRepository _repository;
    private readonly IMcpToolRegistry _toolRegistry;
    private readonly IChatClient _defaultChatClient;
    private readonly IModelProviderResolver _providerResolver;
    private readonly IModelProviderRepository _providerRepository;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DynamicAgent> _logger;

    private AgentCard _agentCard;
    private AgentDefinition? _lastDefinition;
    private volatile AIAgent _cachedAgent;

    public DynamicAgent(
        string agentId,
        AgentDefinition initialDefinition,
        IAgentDefinitionRepository repository,
        IMcpToolRegistry toolRegistry,
        IChatClient defaultChatClient,
        IModelProviderResolver providerResolver,
        IModelProviderRepository providerRepository,
        ILoggerFactory loggerFactory)
    {
        _agentId = agentId;
        _repository = repository;
        _toolRegistry = toolRegistry;
        _defaultChatClient = defaultChatClient;
        _providerResolver = providerResolver;
        _providerRepository = providerRepository;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<DynamicAgent>();
        _lastDefinition = initialDefinition;

        _agentCard = BuildAgentCard(initialDefinition);

        // Build an initial agent synchronously from the definition we already have
        _cachedAgent = BuildAgent(initialDefinition, [], _defaultChatClient);
    }

    public AgentCard GetAgentCard() => _agentCard;

    /// <summary>
    /// Returns the pre-built cached AIAgent. The agent is rebuilt asynchronously
    /// via <see cref="RebuildAsync"/> or during <see cref="InitializeAsync"/>.
    /// </summary>
    public AIAgent GetAIAgent() => _cachedAgent;

    /// <summary>
    /// Loads the latest definition from MongoDB, resolves MCP tools, and caches
    /// the constructed agent for subsequent <see cref="GetAIAgent"/> calls.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RebuildAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Dynamic agent {AgentId} initialized with {ToolCount} tools",
            _agentId, _lastDefinition?.Tools.Count ?? 0);
    }

    /// <summary>
    /// Reloads the agent definition from MongoDB and rebuilds the cached AIAgent
    /// with the latest MCP tools. Safe to call at runtime for hot-reload.
    /// </summary>
    public async Task RebuildAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("DynamicAgent.RebuildAsync", ActivityKind.Internal);
        activity?.SetTag("agent.id", _agentId);

        var definition = await _repository.GetAgentDefinitionAsync(_agentId, cancellationToken)
            .ConfigureAwait(false);

        if (definition is null)
        {
            _logger.LogWarning("Agent definition {AgentId} not found, keeping last known definition", _agentId);
            definition = _lastDefinition ?? throw new InvalidOperationException(
                $"No definition available for dynamic agent '{_agentId}'");
        }

        _lastDefinition = definition;
        _agentCard = BuildAgentCard(definition);

        // Resolve MCP tools asynchronously
        var tools = definition.Tools.Count > 0
            ? await _toolRegistry.ResolveToolsAsync(definition.Tools, cancellationToken).ConfigureAwait(false)
            : [];

        activity?.SetTag("agent.tool_count", tools.Count);

        // Resolve per-agent chat client from model provider, or fall back to default
        var chatClient = await ResolveClientAsync(definition, cancellationToken).ConfigureAwait(false);
        _cachedAgent = BuildAgent(definition, tools, chatClient);
    }

    private async Task<IChatClient> ResolveClientAsync(AgentDefinition definition, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(definition.ModelConnectionName))
            return _defaultChatClient;

        var provider = await _providerRepository.GetProviderAsync(definition.ModelConnectionName, ct)
            .ConfigureAwait(false);

        if (provider is null || !provider.Enabled)
        {
            _logger.LogWarning(
                "Model provider '{ProviderId}' not found or disabled for agent {AgentId}, using default",
                definition.ModelConnectionName, _agentId);
            return _defaultChatClient;
        }

        try
        {
            return _providerResolver.CreateClient(provider);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create client for provider '{ProviderId}', falling back to default", provider.Id);
            return _defaultChatClient;
        }
    }

    private AIAgent BuildAgent(AgentDefinition definition, IReadOnlyList<AITool> tools, IChatClient chatClient)
    {
        var chatOptions = new ChatOptions
        {
            Instructions = definition.Instructions ?? "You are a helpful assistant."
        };

        if (tools.Count > 0)
        {
            chatOptions.Tools = [..tools];
        }

        var agentOptions = new ChatClientAgentOptions
        {
            Id = definition.Name,
            Name = definition.DisplayName ?? definition.Name,
            Description = definition.Description ?? "",
            ChatOptions = chatOptions
        };

        return new ChatClientAgent(chatClient, agentOptions, _loggerFactory);
    }

    private static AgentCard BuildAgentCard(AgentDefinition definition)
    {
        // Auto-generate skills metadata from description + tool references
        var skills = new List<AgentSkill>();
        if (!string.IsNullOrWhiteSpace(definition.Description))
        {
            skills.Add(new AgentSkill
            {
                Id = definition.Name,
                Name = definition.DisplayName ?? definition.Name,
                Description = definition.Description
            });
        }

        return new AgentCard
        {
            Url = $"/a2a/{definition.Name}",
            Name = definition.Name,
            Description = definition.Description ?? $"User-defined agent: {definition.DisplayName ?? definition.Name}",
            Capabilities = new AgentCapabilities
            {
                PushNotifications = false,
                StateTransitionHistory = true,
                Streaming = true,
            },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = skills,
            Version = "1.0.0",
        };
    }
}
