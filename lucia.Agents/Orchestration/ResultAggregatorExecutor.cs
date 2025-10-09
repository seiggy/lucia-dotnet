using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Aggregates agent responses into a single natural language message.
/// </summary>
public sealed class ResultAggregatorExecutor : ReflectingExecutor<ResultAggregatorExecutor>, IMessageHandler<AgentResponse, string>
{
    /// <summary>Executor identifier.</summary>
    public const string ExecutorId = "ResultAggregator";

    /// <summary>Workflow state scope used for aggregation state.</summary>
    public const string StateScope = "aggregation";

    /// <summary>Workflow state key used for aggregation state.</summary>
    public const string StateKey = "responses";

    private readonly ILogger<ResultAggregatorExecutor> _logger;
    private readonly ResultAggregatorOptions _options;

    public ResultAggregatorExecutor(ILogger<ResultAggregatorExecutor> logger, IOptions<ResultAggregatorOptions> options)
        : base(ExecutorId)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<string> HandleAsync(AgentResponse message, IWorkflowContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);

        await context.AddEventAsync(new ExecutorInvokedEvent(this.Id, message), cancellationToken).ConfigureAwait(false);

        var state = await LoadStateAsync(context, cancellationToken).ConfigureAwait(false);

        state.Responses[message.AgentId] = message;

        var ordered = OrderResponses(state.Responses.Values);
        var summary = BuildSummary(ordered);

        await context.QueueStateUpdateAsync(StateKey, state, StateScope, cancellationToken).ConfigureAwait(false);
        await context.AddEventAsync(new ExecutorCompletedEvent(this.Id, summary), cancellationToken).ConfigureAwait(false);

        if (summary.FailedAgents.Count > 0)
        {
            var reason = BuildFailureReason(summary);
            await context.AddEventAsync(new ExecutorFailedEvent(this.Id, new InvalidOperationException(reason)), cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("Result aggregation encountered failures: {Reason}", reason);
        }
        else
        {
            _logger.LogInformation("Result aggregation succeeded for agents {Agents}", string.Join(", ", summary.SuccessfulAgents));
        }

        return summary.Message;
    }

    public ValueTask<string> HandleAsync(AgentResponse message, IWorkflowContext context)
        => HandleAsync(message, context, CancellationToken.None);

    private async Task<ResultAggregationState> LoadStateAsync(IWorkflowContext context, CancellationToken cancellationToken)
    {
        var stored = await context.ReadStateAsync<ResultAggregationState>(StateKey, StateScope, cancellationToken).ConfigureAwait(false);
        return stored ?? new ResultAggregationState();
    }

    private AggregationResult BuildSummary(IEnumerable<AgentResponse> responses)
    {
        var successes = new List<AgentResponse>();
        var failures = new List<AggregatedFailure>();
        long totalTime = 0;

        foreach (var response in responses)
        {
            totalTime += Math.Max(0, response.ExecutionTimeMs);

            if (response.Success)
            {
                successes.Add(response);
            }
            else
            {
                failures.Add(new AggregatedFailure(response.AgentId, response.ErrorMessage ?? _options.DefaultFailureMessage));
            }
        }

        var message = ComposeMessage(successes, failures);
        var successAgents = successes.Select(r => r.AgentId).ToList();

        return new AggregationResult(
            message,
            successAgents,
            failures,
            totalTime);
    }

    private IReadOnlyList<AgentResponse> OrderResponses(IEnumerable<AgentResponse> responses)
    {
        var priorityLookup = _options.AgentPriority
            .Select((agentId, index) => (agentId, index))
            .ToDictionary(tuple => tuple.agentId, tuple => tuple.index, StringComparer.OrdinalIgnoreCase);

        return responses
            .OrderBy(r => priorityLookup.TryGetValue(r.AgentId, out var index) ? index : int.MaxValue)
            .ThenBy(r => r.AgentId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string ComposeMessage(IReadOnlyList<AgentResponse> successes, IReadOnlyList<AggregatedFailure> failures)
    {
        var builder = new StringBuilder();

        if (successes.Count > 0)
        {
            var successMessages = successes
                .Select(response => NormalizeSuccessMessage(response))
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .ToList();

            if (successMessages.Count > 0)
            {
                builder.Append(string.Join(' ', successMessages));
            }
        }

        if (failures.Count > 0)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            if (failures.Count == 1)
            {
                var failure = failures[0];
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "However, I couldn't complete {0}: {1}.",
                    FormatAgentName(failure.AgentId),
                    failure.Error);
            }
            else
            {
                builder.Append("However, I ran into issues with ");
                builder.Append(string.Join(
                    ", ",
                    failures.Select(f => string.Format(CultureInfo.InvariantCulture, "{0} ({1})", FormatAgentName(f.AgentId), f.Error))));
                if (!builder.ToString().EndsWith('.'))
                {
                    builder.Append('.');
                }
            }
        }

        if (builder.Length == 0)
        {
            builder.Append(_options.DefaultFallbackMessage);
        }

        return builder.ToString();
    }

    private string NormalizeSuccessMessage(AgentResponse response)
    {
        var content = response.Content?.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                _options.DefaultSuccessTemplate,
                FormatAgentName(response.AgentId));
        }

        return content;
    }

    private static string FormatAgentName(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return "agent";
        }

        var parts = agentId.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Select(static part =>
        {
            if (part.Length == 0)
            {
                return part;
            }

            if (part.Length == 1)
            {
                return char.ToUpperInvariant(part[0]).ToString();
            }

            return char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant();
        }));
    }

    private static string BuildFailureReason(AggregationResult result)
    {
        var parts = result.FailedAgents
            .Select(failure => string.Format(CultureInfo.InvariantCulture, "{0}: {1}", failure.AgentId, failure.Error))
            .ToArray();

        return string.Join("; ", parts);
    }
}

/// <summary>
/// Aggregation options for the result aggregator executor.
/// </summary>
public sealed class ResultAggregatorOptions
{
    /// <summary>Defines the priority order for agents when combining responses.</summary>
    public IList<string> AgentPriority { get; set; } = new List<string> { "light-agent", "music-agent", "climate-agent", "security-agent", "general-assistant" };

    /// <summary>Template used when an agent succeeds but returns no content.</summary>
    public string DefaultSuccessTemplate { get; set; } = "{0} completed successfully.";

    /// <summary>Fallback message when no responses are available.</summary>
    public string DefaultFallbackMessage { get; set; } = "I'm still working on that request.";

    /// <summary>Fallback error message when agent failure provides no reason.</summary>
    public string DefaultFailureMessage { get; set; } = "Unknown error";
}

/// <summary>
/// State persisted between aggregator invocations.
/// </summary>
public sealed class ResultAggregationState
{
    /// <summary>Agent responses collected so far.</summary>
    public Dictionary<string, AgentResponse> Responses { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Aggregation telemetry emitted to workflow context subscribers.
/// </summary>
public sealed record AggregationResult(
    string Message,
    IReadOnlyList<string> SuccessfulAgents,
    IReadOnlyList<AggregatedFailure> FailedAgents,
    long TotalExecutionTimeMs);

/// <summary>
/// Represents a failure surfaced during aggregation.
/// </summary>
public sealed record AggregatedFailure(string AgentId, string Error);
