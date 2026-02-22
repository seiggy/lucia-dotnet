using System.Diagnostics;
using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Mcp;

/// <summary>
/// A user-defined agent that is lazily constructed from its MongoDB definition
/// and resolved MCP tools. Each invocation loads the latest definition, so edits
/// take effect immediately without restart.
/// </summary>
public sealed class DynamicAgent : ILuciaAgent
{
    private static readonly ActivitySource ActivitySource = new("Lucia.Agents.Dynamic", "1.0.0");

    private readonly string _agentId;
    private readonly IAgentDefinitionRepository _repository;
    private readonly IMcpToolRegistry _toolRegistry;
    private readonly IChatClient _defaultChatClient;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<DynamicAgent> _logger;

    private AgentCard _agentCard;
    private AgentDefinition? _lastDefinition;

    public DynamicAgent(
        string agentId,
        AgentDefinition initialDefinition,
        IAgentDefinitionRepository repository,
        IMcpToolRegistry toolRegistry,
        IChatClient defaultChatClient,
        ILoggerFactory loggerFactory)
    {
        _agentId = agentId;
        _repository = repository;
        _toolRegistry = toolRegistry;
        _defaultChatClient = defaultChatClient;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<DynamicAgent>();
        _lastDefinition = initialDefinition;

        _agentCard = BuildAgentCard(initialDefinition);
    }

    public AgentCard GetAgentCard() => _agentCard;

    /// <summary>
    /// Lazily constructs a ChatClientAgent from the latest MongoDB definition.
    /// Resolves MCP tools from the registry on each call for hot-reload support.
    /// </summary>
    public AIAgent GetAIAgent()
    {
        using var activity = ActivitySource.StartActivity("DynamicAgent.GetAIAgent", ActivityKind.Internal);
        activity?.SetTag("agent.id", _agentId);

        // Load latest definition synchronously (hot path, cached by MongoDB driver)
        var definition = _repository.GetAgentDefinitionAsync(_agentId).GetAwaiter().GetResult();
        if (definition is null)
        {
            _logger.LogWarning("Agent definition {AgentId} not found, using last known definition", _agentId);
            definition = _lastDefinition ?? throw new InvalidOperationException(
                $"No definition available for dynamic agent '{_agentId}'");
        }

        _lastDefinition = definition;

        // Refresh agent card if definition changed
        _agentCard = BuildAgentCard(definition);

        // Resolve MCP tools
        var tools = definition.Tools.Count > 0
            ? _toolRegistry.ResolveToolsAsync(definition.Tools).GetAwaiter().GetResult()
            : [];

        activity?.SetTag("agent.tool_count", tools.Count);

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

        return new ChatClientAgent(_defaultChatClient, agentOptions, _loggerFactory);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Dynamic agent {AgentId} initialized", _agentId);
        return Task.CompletedTask;
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
