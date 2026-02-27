using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Configuration;
using lucia.Agents.Services;
using lucia.Agents.Skills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Agents;

/// <summary>
/// Agent for managing shopping list and todo list items in Home Assistant.
/// </summary>
public sealed class ListsAgent : ILuciaAgent
{
    private const string AgentId = "lists-agent";

    private readonly AgentCard _agent;
    private readonly IChatClientResolver _clientResolver;
    private readonly IAgentDefinitionRepository _definitionRepository;
    private readonly ListSkill _listSkill;
    private readonly TracingChatClientFactory _tracingFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ListsAgent> _logger;
    private volatile AIAgent _aiAgent = null!;
    private string? _lastModelConnectionName;

    public string Instructions { get; }
    public IList<AITool> Tools { get; }

    public ListsAgent(
        IChatClientResolver clientResolver,
        IAgentDefinitionRepository definitionRepository,
        ListSkill listSkill,
        TracingChatClientFactory tracingFactory,
        ILoggerFactory loggerFactory)
    {
        _clientResolver = clientResolver;
        _definitionRepository = definitionRepository;
        _listSkill = listSkill;
        _tracingFactory = tracingFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ListsAgent>();

        Tools = _listSkill.GetTools();

        var listControlSkill = new AgentSkill
        {
            Id = "id_lists_agent",
            Name = "Lists",
            Description = "Add items to shopping and todo lists",
            Tags = ["shopping", "todo", "groceries", "tasks", "lists"],
            Examples =
            [
                "Add milk to the shopping list",
                "Add eggs to my grocery list",
                "Add 'call plumber' to my todo list",
                "What's on the shopping list?",
                "Add bread to shopping list"
            ]
        };

        _agent = new AgentCard
        {
            Url = "/a2a/lists-agent",
            Name = AgentId,
            Description = "Agent for adding items to shopping and todo lists in Home Assistant",
            Capabilities = new AgentCapabilities
            {
                PushNotifications = false,
                StateTransitionHistory = true,
                Streaming = true,
            },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = [listControlSkill],
            Version = "1.0.0",
        };

        Instructions = """
            You are a Lists Agent for a Home Assistant smart home.

            Your responsibilities:
            - Add items to the Home Assistant shopping list (groceries, items to buy)
            - Add items to todo lists (tasks, reminders)
            - List shopping list items or todo list items when asked

            Use AddToShoppingListAsync for "add X to shopping list" or "add X to groceries".
            Use AddToTodoListAsync when the user specifies a todo list (entity like todo.grocery or todo.personal_tasks). Call ListTodoEntitiesAsync first if the user asks to add to "todo" without specifying which list.
            Use ListShoppingItemsAsync when the user asks what's on the shopping list.
            Use ListTodoItemsAsync when the user asks what's on a specific todo list.

            Keep responses short and confirm what was added.
            """;
    }

    public AgentCard GetAgentCard() => _agent;

    public AIAgent GetAIAgent() => _aiAgent;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing ListsAgent...");
        await _listSkill.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await ApplyDefinitionAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("ListsAgent initialized successfully");
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
        _lastModelConnectionName = newConnectionName;
        _logger.LogInformation("ListsAgent: using model provider '{Provider}'", newConnectionName ?? "default-chat");
    }

    private AIAgent BuildAgent(IChatClient chatClient)
    {
        var traced = _tracingFactory.Wrap(chatClient, AgentId);
        var agentOptions = new ChatClientAgentOptions
        {
            Id = AgentId,
            Name = AgentId,
            Description = "Agent for adding items to shopping and todo lists",
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
