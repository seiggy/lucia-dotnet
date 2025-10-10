using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using A2A;
using lucia.Agents.Registry;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Main orchestrator for the Lucia multi-agent system using MagenticOne pattern
/// </summary>
public class LuciaOrchestrator
{
    private readonly IChatClient _chatClient;
    private readonly AgentRegistry _agentRegistry;
    private readonly AgentCatalog _agentCatalog;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LuciaOrchestrator> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOptions<RouterExecutorOptions> _routerOptions;
    private readonly IOptions<AgentExecutorWrapperOptions> _wrapperOptions;
    private readonly IOptions<ResultAggregatorOptions> _aggregatorOptions;
    private readonly TimeProvider _timeProvider;
    private readonly TaskManager _taskManager;

    public LuciaOrchestrator(
        IChatClient chatClient,
        AgentRegistry agentRegistry,
        AgentCatalog agentCatalog,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<LuciaOrchestrator> logger,
        ILoggerFactory loggerFactory,
        IOptions<RouterExecutorOptions> routerOptions,
        IOptions<AgentExecutorWrapperOptions> wrapperOptions,
        IOptions<ResultAggregatorOptions> aggregatorOptions,
        TimeProvider timeProvider)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _agentRegistry = agentRegistry;
        _agentCatalog = agentCatalog ?? throw new ArgumentNullException(nameof(agentCatalog));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _routerOptions = routerOptions ?? throw new ArgumentNullException(nameof(routerOptions));
        _wrapperOptions = wrapperOptions ?? throw new ArgumentNullException(nameof(wrapperOptions));
        _aggregatorOptions = aggregatorOptions ?? throw new ArgumentNullException(nameof(aggregatorOptions));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _taskManager = new TaskManager();

        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Process a user request through the MagenticOne multi-agent system
    /// </summary>
    public async Task<string> ProcessRequestAsync(string userRequest, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing user request: {Request}", userRequest);

        try
        {
            if (string.IsNullOrWhiteSpace(userRequest))
            {
                throw new ArgumentException("User request cannot be empty.", nameof(userRequest));
            }

            var availableAgentCards = await _agentRegistry
                .GetAgentsAsync(cancellationToken)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (availableAgentCards.Count == 0)
            {
                _logger.LogWarning("No agents available to process request");
                return "I don't have any specialized agents available right now. Please try again later.";
            }

            var aiAgents = await _agentCatalog
                .GetAgentsAsync(cancellationToken)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var wrappers = CreateWrappers(availableAgentCards, aiAgents);
            if (wrappers.Count == 0)
            {
                _logger.LogWarning("Unable to build any agent executor wrappers. Falling back to aggregator message.");
                return _aggregatorOptions.Value.DefaultFallbackMessage;
            }

            var routerLogger = _loggerFactory.CreateLogger<RouterExecutor>();
            var dispatchLogger = _loggerFactory.CreateLogger<AgentDispatchExecutor>();
            var aggregatorLogger = _loggerFactory.CreateLogger<ResultAggregatorExecutor>();
            var router = new RouterExecutor(_chatClient, _agentRegistry, routerLogger, _routerOptions);
            var dispatch = new AgentDispatchExecutor(wrappers, dispatchLogger);
            var aggregator = new ResultAggregatorExecutor(aggregatorLogger, _aggregatorOptions);

            var chatMessage = new ChatMessage(ChatRole.User, userRequest);
            dispatch.SetUserMessage(chatMessage);

            var builder = new WorkflowBuilder(router)
                .WithName("LuciaOrchestratorWorkflow")
                .WithDescription("Routes Lucia user requests to specialized agents and aggregates responses.")
                .AddEdge(router, dispatch)
                .AddEdge(dispatch, aggregator)
                .WithOutputFrom(aggregator);

            var workflow = builder.Build();

            var result = await ExecuteWorkflowAsync(workflow, chatMessage, cancellationToken).ConfigureAwait(false);

            return result ?? _aggregatorOptions.Value.DefaultFallbackMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing user request: {Request}", userRequest);
            return "I encountered an error while processing your request. Please try again.";
        }
    }

    /// <summary>
    /// Get the current status of the orchestrator
    /// </summary>
    public async Task<OrchestratorStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var agents = await _agentRegistry
            .GetAgentsAsync(cancellationToken)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new OrchestratorStatus
        {
            IsReady = agents.Count > 0,
            AvailableAgentCount = agents.Count,
            AvailableAgents = agents
        };
    }

    /// <summary>
    /// Clear the chat history
    /// </summary>
    public void ClearHistory()
    {
        _logger.LogInformation("Chat history cleared");
        throw new NotImplementedException("Chat history management not implemented yet");
    }

    private Dictionary<string, AgentExecutorWrapper> CreateWrappers(
        IReadOnlyList<AgentCard> agentCards,
        IReadOnlyList<AIAgent> aiAgents)
    {
        var wrappers = new Dictionary<string, AgentExecutorWrapper>(StringComparer.OrdinalIgnoreCase);
        var wrapperLogger = _loggerFactory.CreateLogger<AgentExecutorWrapper>();

        var agentsByKey = aiAgents
            .Select(agent => (Key: NormalizeAgentKey(agent), Agent: agent))
            .Where(tuple => tuple.Key is not null)
            .GroupBy(tuple => tuple.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Agent, StringComparer.OrdinalIgnoreCase);

        var cardsByKey = agentCards
            .Where(card => !string.IsNullOrWhiteSpace(card.Name))
            .GroupBy(card => card.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var allKeys = new HashSet<string>(agentsByKey.Keys, StringComparer.OrdinalIgnoreCase);
        allKeys.UnionWith(cardsByKey.Keys);

        foreach (var key in allKeys)
        {
            agentsByKey.TryGetValue(key, out var agent);
            cardsByKey.TryGetValue(key, out var card);

            var wrapper = new AgentExecutorWrapper(
                key,
                _serviceProvider,
                wrapperLogger,
                _wrapperOptions,
                agent,
                card,
                card is not null ? _taskManager : null,
                _timeProvider);

            wrappers[key] = wrapper;
        }

        return wrappers;
    }

    private static string? NormalizeAgentKey(AIAgent agent)
        => agent.Name ?? agent.Id;

    private static async Task<string?> ExecuteWorkflowAsync(Workflow workflow, ChatMessage input, CancellationToken cancellationToken)
    {
        await using var run = await InProcessExecution.RunAsync(workflow, input, cancellationToken: cancellationToken).ConfigureAwait(false);

        string? result = null;
        foreach (var evt in run.OutgoingEvents.OfType<WorkflowOutputEvent>())
        {
            if (evt.Data is string text)
            {
                result = text;
            }
        }

        return result;
    }

    private sealed class AgentDispatchExecutor : ReflectingExecutor<AgentDispatchExecutor>, IMessageHandler<AgentChoiceResult, AgentResponse>
    {
        public const string ExecutorId = "AgentDispatch";

        private readonly IReadOnlyDictionary<string, AgentExecutorWrapper> _wrappers;
        private readonly ILogger<AgentDispatchExecutor> _logger;
        private ChatMessage? _userMessage;

        public AgentDispatchExecutor(
            IReadOnlyDictionary<string, AgentExecutorWrapper> wrappers,
            ILogger<AgentDispatchExecutor> logger)
            : base(ExecutorId)
        {
            _wrappers = wrappers;
            _logger = logger;
        }

        public void SetUserMessage(ChatMessage message)
        {
            _userMessage = message;
        }

        public async ValueTask<AgentResponse> HandleAsync(AgentChoiceResult message, IWorkflowContext context, CancellationToken cancellationToken)
        {
            await context.AddEventAsync(new ExecutorInvokedEvent(this.Id, message), cancellationToken).ConfigureAwait(false);

            if (_userMessage is null)
            {
                _logger.LogWarning("User message unavailable when dispatching agent execution.");
                return CreateFailureResponse(message.AgentId, "Unable to locate the original user request.");
            }

            var executionOrder = BuildExecutionOrder(message);
            AgentResponse? primaryResponse = null;

            foreach (var agentId in executionOrder)
            {
                var response = await InvokeAgentAsync(agentId, _userMessage, context, cancellationToken).ConfigureAwait(false);
                if (primaryResponse is null)
                {
                    primaryResponse = response;
                }
                else
                {
                    await context.SendMessageAsync(response, cancellationToken).ConfigureAwait(false);
                }
            }

            return primaryResponse ?? CreateFailureResponse(message.AgentId, "No agents were dispatched.");
        }

        public ValueTask<AgentResponse> HandleAsync(AgentChoiceResult message, IWorkflowContext context)
            => HandleAsync(message, context, CancellationToken.None);

        private async ValueTask<AgentResponse> InvokeAgentAsync(string agentId, ChatMessage userMessage, IWorkflowContext context, CancellationToken cancellationToken)
        {
            if (!_wrappers.TryGetValue(agentId, out var wrapper))
            {
                _logger.LogWarning("No wrapper registered for agent {AgentId}.", agentId);
                return CreateFailureResponse(agentId, $"Agent '{agentId}' is not available.");
            }

            return await wrapper.HandleAsync(userMessage, context, cancellationToken).ConfigureAwait(false);
        }

        private static AgentResponse CreateFailureResponse(string agentId, string error)
            => new()
            {
                AgentId = agentId,
                Content = string.Empty,
                Success = false,
                ErrorMessage = error,
                ExecutionTimeMs = 0
            };

        private static IReadOnlyList<string> BuildExecutionOrder(AgentChoiceResult choice)
        {
            var ordered = new List<string>();
            if (!string.IsNullOrWhiteSpace(choice.AgentId))
            {
                ordered.Add(choice.AgentId);
            }

            if (choice.AdditionalAgents is { Count: > 0 })
            {
                foreach (var agentId in choice.AdditionalAgents)
                {
                    if (!string.IsNullOrWhiteSpace(agentId) && !ordered.Contains(agentId, StringComparer.OrdinalIgnoreCase))
                    {
                        ordered.Add(agentId);
                    }
                }
            }

            return ordered;
        }
    }
}
