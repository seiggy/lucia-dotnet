using System.Diagnostics;
using A2A;
using lucia.Agents.Abstractions;
using lucia.Agents.Mcp;
using lucia.Agents.Orchestration.Models;
using lucia.Agents.Providers;
using lucia.Agents.Registry;
using lucia.Agents.Services;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Builds and executes the Router → AgentDispatch → ResultAggregator workflow pipeline.
/// Handles agent resolution, invoker creation, and workflow execution with telemetry.
/// </summary>
public sealed class WorkflowFactory
{
    private readonly IChatClientResolver _clientResolver;
    private readonly IAgentDefinitionRepository _definitionRepository;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<WorkflowFactory> _logger;
    private readonly IOptions<RouterExecutorOptions> _routerOptions;
    private readonly IOptions<AgentInvokerOptions> _invokerOptions;
    private readonly IOptions<ResultAggregatorOptions> _aggregatorOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ITaskManager _taskManager;
    private readonly IOrchestratorObserver? _observer;
    private readonly IAgentProvider? _agentProvider;
    private readonly IPromptCacheService? _promptCache;
    private readonly IDynamicAgentProvider? _dynamicAgentProvider;

    public WorkflowFactory(
        IChatClientResolver clientResolver,
        IAgentDefinitionRepository definitionRepository,
        IAgentRegistry agentRegistry,
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory,
        IOptions<RouterExecutorOptions> routerOptions,
        IOptions<AgentInvokerOptions> invokerOptions,
        IOptions<ResultAggregatorOptions> aggregatorOptions,
        TimeProvider timeProvider,
        ITaskManager taskManager,
        IOrchestratorObserver? observer = null,
        IAgentProvider? agentProvider = null,
        IPromptCacheService? promptCache = null,
        IDynamicAgentProvider? dynamicAgentProvider = null)
    {
        _clientResolver = clientResolver ?? throw new ArgumentNullException(nameof(clientResolver));
        _definitionRepository = definitionRepository ?? throw new ArgumentNullException(nameof(definitionRepository));
        _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<WorkflowFactory>();
        _routerOptions = routerOptions ?? throw new ArgumentNullException(nameof(routerOptions));
        _invokerOptions = invokerOptions ?? throw new ArgumentNullException(nameof(invokerOptions));
        _aggregatorOptions = aggregatorOptions ?? throw new ArgumentNullException(nameof(aggregatorOptions));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _taskManager = taskManager ?? throw new ArgumentNullException(nameof(taskManager));
        _observer = observer;
        _agentProvider = agentProvider;
        _promptCache = promptCache;
        _dynamicAgentProvider = dynamicAgentProvider;
    }

    /// <summary>
    /// Resolves available AI agents from the agent provider or from DI-registered
    /// ILuciaAgent instances and remote agent cards.
    /// </summary>
    public async Task<IReadOnlyList<AIAgent>> ResolveAgentsAsync(
        IReadOnlyCollection<AgentCard> agentCards,
        CancellationToken cancellationToken)
    {
        if (_agentProvider is not null)
            return await _agentProvider.GetAgentsAsync(cancellationToken).ConfigureAwait(false);

        var resolved = new List<AIAgent>();

        // Resolve in-process agents from ILuciaAgent DI registrations
        var luciaAgents = _serviceProvider.GetServices<Abstractions.ILuciaAgent>();
        foreach (var luciaAgent in luciaAgents)
        {
            try
            {
                await luciaAgent.RefreshConfigAsync(cancellationToken).ConfigureAwait(false);
                var aiAgent = luciaAgent.GetAIAgent();
                if (aiAgent is not null)
                {
                    resolved.Add(aiAgent);
                    _logger.LogDebug("Resolved local AIAgent: {AgentName}", aiAgent.Name ?? aiAgent.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve AIAgent from ILuciaAgent {Type}", luciaAgent.GetType().Name);
            }
        }

        // Also resolve remote agents with absolute HTTP/HTTPS URIs
        foreach (var card in agentCards)
        {
            if (Uri.TryCreate(card.Url, UriKind.Absolute, out var cardUri)
                && (cardUri.Scheme == Uri.UriSchemeHttp || cardUri.Scheme == Uri.UriSchemeHttps))
            {
                try
                {
                    resolved.Add(card.AsAIAgent());
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to resolve AIAgent from card {AgentName} ({Url})", card.Name, card.Url);
                }
            }
        }

        // Resolve dynamic agents (user-defined via MCP) — lazily built from latest Mongo definition
        if (_dynamicAgentProvider is not null)
        {
            foreach (var dynamicAgent in _dynamicAgentProvider.GetAllAgents())
            {
                try
                {
                    var aiAgent = dynamicAgent.GetAIAgent();
                    if (aiAgent is not null)
                    {
                        resolved.Add(aiAgent);
                        _logger.LogDebug("Resolved dynamic AIAgent: {AgentName}", aiAgent.Name ?? aiAgent.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to resolve dynamic AIAgent {AgentId}", dynamicAgent.GetAgentCard().Name);
                }
            }
        }

        return resolved.AsReadOnly();
    }

    /// <summary>
    /// Creates agent invokers (local or remote) keyed by agent name.
    /// Local agents use <see cref="AIHostAgent"/> for in-process invocation with
    /// session persistence. Remote agents route through <see cref="ITaskManager"/>.
    /// </summary>
    public Dictionary<string, IAgentInvoker> CreateInvokers(
        IReadOnlyCollection<AgentCard> agentCards,
        IReadOnlyList<AIAgent> aiAgents)
    {
        var invokers = new Dictionary<string, IAgentInvoker>(StringComparer.OrdinalIgnoreCase);
        var invokerLogger = _loggerFactory.CreateLogger("lucia.Agents.Orchestration.AgentInvoker");
        var sessionStore = _serviceProvider.GetService<AgentSessionStore>() ?? new NoopAgentSessionStore();

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

            IAgentInvoker invoker;
            if (agent is not null)
            {
                // Local agent: invoke in-process via AIHostAgent with session persistence
                invoker = new LocalAgentInvoker(key, agent, sessionStore, invokerLogger, _invokerOptions, _timeProvider);
            }
            else if (card is not null && Uri.TryCreate(card.Url, UriKind.Absolute, out var cardUri)
                     && (cardUri.Scheme == Uri.UriSchemeHttp || cardUri.Scheme == Uri.UriSchemeHttps))
            {
                // Remote agent: route through TaskManager via HTTP
                invoker = new RemoteAgentInvoker(key, card, _taskManager, invokerLogger, _invokerOptions, _timeProvider);
            }
            else
            {
                _logger.LogDebug("Skipping agent {AgentName}: no local match and no absolute URL.", key);
                continue;
            }

            invokers[key] = invoker;
        }

        return invokers;
    }

    /// <summary>
    /// Builds the Router → AgentDispatch → ResultAggregator workflow,
    /// executes it, and returns the orchestrated result.
    /// </summary>
    public async Task<OrchestratorResult?> BuildAndExecuteAsync(
        Dictionary<string, IAgentInvoker> invokers,
        string historyAwareRequest,
        CancellationToken cancellationToken)
    {
        // Resolve the orchestrator's chat client per-request so model provider changes take effect
        var definition = await _definitionRepository.GetAgentDefinitionAsync("orchestrator", cancellationToken).ConfigureAwait(false);
        var chatClient = await _clientResolver.ResolveAsync(definition?.ModelConnectionName, cancellationToken).ConfigureAwait(false);

        var routerLogger = _loggerFactory.CreateLogger<RouterExecutor>();
        var dispatchLogger = _loggerFactory.CreateLogger<AgentDispatchExecutor>();
        var aggregatorLogger = _loggerFactory.CreateLogger<ResultAggregatorExecutor>();
        var router = new RouterExecutor(chatClient, _agentRegistry, routerLogger, _routerOptions, _promptCache);
        var dispatch = new AgentDispatchExecutor(invokers, dispatchLogger, _routerOptions, chatClient, _observer);
        var aggregator = new ResultAggregatorExecutor(aggregatorLogger, _aggregatorOptions);

        var chatMessage = new ChatMessage(ChatRole.User, historyAwareRequest);
        dispatch.SetUserMessage(chatMessage);

        var builder = new WorkflowBuilder(router)
            .WithName("LuciaOrchestratorWorkflow")
            .WithDescription("Routes Lucia user requests to specialized agents and aggregates responses.")
            .AddEdge(router, dispatch)
            .AddEdge(dispatch, aggregator)
            .WithOutputFrom(aggregator);

        var workflow = builder.Build();

        return await ExecuteWorkflowAsync(workflow, chatMessage, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OrchestratorResult?> ExecuteWorkflowAsync(
        Workflow workflow, ChatMessage input, CancellationToken cancellationToken)
    {
        var startTimestamp = _timeProvider.GetTimestamp();
        using var activity = OrchestrationTelemetry.Source.StartActivity("LuciaOrchestrator.ExecuteWorkflow", ActivityKind.Internal);
        activity?.SetTag("workflow.name", workflow.Name ?? "LuciaOrchestratorWorkflow");
        activity?.SetTag("workflow.start.executor", workflow.StartExecutorId);

        try
        {
            await using var run = await InProcessExecution.RunAsync(workflow, input, cancellationToken: cancellationToken).ConfigureAwait(false);

            OrchestratorResult? result = null;
            List<string>? errors = null;

            foreach (var evt in run.OutgoingEvents.OfType<WorkflowOutputEvent>())
            {
                if (evt.Data is OrchestratorResult orchestratorResult)
                {
                    result = orchestratorResult;
                }
                else if (evt.Data is string text)
                {
                    result = new OrchestratorResult { Text = text };
                }
            }

            foreach (var evt in run.OutgoingEvents.OfType<WorkflowErrorEvent>())
            {
                var exception = evt.Data as Exception;
                var message = exception?.Message ?? evt.Data?.ToString() ?? "Workflow execution reported an unknown error.";

                errors ??= new List<string>();
                errors.Add(message);

                if (exception is not null)
                {
                    _logger.LogError(exception, "Workflow execution emitted error event: {Message}", message);
                }
                else
                {
                    _logger.LogError("Workflow execution emitted error event: {Message}", message);
                }
            }

            if (errors is not null)
            {
                var errorSummary = string.Join("; ", errors);
                activity?.SetTag(OrchestrationTelemetry.Tags.Success, false);
                activity?.SetTag(OrchestrationTelemetry.Tags.ErrorMessage, errorSummary);

                return result ?? new OrchestratorResult { Text = errorSummary };
            }

            activity?.SetTag(OrchestrationTelemetry.Tags.Success, true);

            if (result is not null)
            {
                activity?.SetTag("workflow.output.length", result.Text.Length);
            }

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetTag(OrchestrationTelemetry.Tags.Success, false);
            activity?.SetTag(OrchestrationTelemetry.Tags.ErrorMessage, ex.Message);
            _logger.LogError(ex, "Workflow execution failed to complete.");
            throw;
        }
        finally
        {
            var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
            activity?.SetTag(OrchestrationTelemetry.Tags.ExecutionTime, elapsed.TotalMilliseconds);
        }
    }

    private static string? NormalizeAgentKey(AIAgent agent)
        => agent.Id ?? agent.Name;
}
