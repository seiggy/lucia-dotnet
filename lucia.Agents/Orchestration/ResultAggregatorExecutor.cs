using System.Globalization;
using System.Text;
using lucia.Agents.Orchestration.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace lucia.Agents.Orchestration;

/// <summary>
/// Aggregates agent responses into a single natural language message.
/// </summary>
public sealed class ResultAggregatorExecutor : Executor
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

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
        => protocolBuilder.ConfigureRoutes(rb => rb.AddHandler<List<OrchestratorAgentResponse>, OrchestratorResult>(HandleAsync));

    public async ValueTask<OrchestratorResult> HandleAsync(List<OrchestratorAgentResponse> responses, IWorkflowContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(responses);
        ArgumentNullException.ThrowIfNull(context);

        await context.AddEventAsync(new ExecutorInvokedEvent(this.Id, responses), cancellationToken).ConfigureAwait(false);

        // Order responses by agent priority and build summary
        var ordered = OrderResponses(responses);
        var summary = BuildSummary(ordered);

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

        return new OrchestratorResult
        {
            Text = summary.Message,
            NeedsInput = summary.NeedsInput
        };
    }

    private AggregationResult BuildSummary(IEnumerable<OrchestratorAgentResponse> responses)
    {
        var successes = new List<OrchestratorAgentResponse>();
        var failures = new List<AggregatedFailure>();
        long totalTime = 0;
        var needsInput = false;

        foreach (var response in responses)
        {
            totalTime += Math.Max(0, response.ExecutionTimeMs);

            if (response.Success)
            {
                successes.Add(response);
                if (response.NeedsInput)
                {
                    needsInput = true;
                }
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
            totalTime,
            needsInput);
    }

    private IReadOnlyList<OrchestratorAgentResponse> OrderResponses(IEnumerable<OrchestratorAgentResponse> responses)
    {
        var priorityLookup = _options.AgentPriority
            .Select((agentId, index) => (agentId, index))
            .ToDictionary(tuple => tuple.agentId, tuple => tuple.index, StringComparer.OrdinalIgnoreCase);

        return responses
            .OrderBy(r => priorityLookup.TryGetValue(r.AgentId, out var index) ? index : int.MaxValue)
            .ThenBy(r => r.AgentId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string ComposeMessage(IReadOnlyList<OrchestratorAgentResponse> successes, IReadOnlyList<AggregatedFailure> failures)
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

    private string NormalizeSuccessMessage(OrchestratorAgentResponse response)
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
