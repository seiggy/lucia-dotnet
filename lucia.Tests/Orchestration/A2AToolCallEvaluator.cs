#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Custom evaluator that validates the full Lucia orchestrator A2A workflow:
/// <list type="number">
///   <item><term>Routing</term><description>
///     Did the router produce a valid routing decision?
///   </description></item>
///   <item><term>Agent Targeting</term><description>
///     Were the expected agents selected (primary + additional)?
///   </description></item>
///   <item><term>Agent Execution</term><description>
///     Did all dispatched agents execute successfully?
///   </description></item>
///   <item><term>Response Aggregation</term><description>
///     Was a non-empty aggregated response produced?
///   </description></item>
/// </list>
/// Scores each dimension on a 1–5 scale and produces four metrics plus a
/// composite <c>A2AWorkflow</c> score. Requires <see cref="A2AToolCallEvaluatorContext"/>
/// in the <c>additionalContext</c> collection.
/// </summary>
public sealed class A2AToolCallEvaluator : IEvaluator
{
    public const string RoutingMetricName = "A2A.Routing";
    public const string AgentTargetingMetricName = "A2A.AgentTargeting";
    public const string AgentExecutionMetricName = "A2A.AgentExecution";
    public const string AggregationMetricName = "A2A.Aggregation";
    public const string WorkflowMetricName = "A2A.Workflow";

    /// <inheritdoc />
    public IReadOnlyCollection<string> EvaluationMetricNames =>
    [
        RoutingMetricName,
        AgentTargetingMetricName,
        AgentExecutionMetricName,
        AggregationMetricName,
        WorkflowMetricName
    ];

    /// <inheritdoc />
    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var context = additionalContext?
            .OfType<A2AToolCallEvaluatorContext>()
            .FirstOrDefault();

        if (context is null)
        {
            return new ValueTask<EvaluationResult>(CreateMissingContextResult());
        }

        var routing = EvaluateRouting(context);
        var targeting = EvaluateAgentTargeting(context);
        var execution = EvaluateAgentExecution(context);
        var aggregation = EvaluateAggregation(context);
        var workflow = EvaluateWorkflowComposite(routing, targeting, execution, aggregation);

        return new ValueTask<EvaluationResult>(
            new EvaluationResult(routing, targeting, execution, aggregation, workflow));
    }

    /// <summary>
    /// Did the router produce a non-null decision with confidence > 0?
    /// </summary>
    private static NumericMetric EvaluateRouting(A2AToolCallEvaluatorContext context)
    {
        var decision = context.RoutingDecision;

        if (decision is null)
        {
            return CreateMetric(RoutingMetricName, 1, EvaluationRating.Unacceptable,
                "Router produced no routing decision.");
        }

        if (string.IsNullOrWhiteSpace(decision.AgentId))
        {
            return CreateMetric(RoutingMetricName, 1, EvaluationRating.Unacceptable,
                "Router decision has an empty AgentId.");
        }

        if (string.IsNullOrWhiteSpace(decision.Reasoning))
        {
            return CreateMetric(RoutingMetricName, 3, EvaluationRating.Average,
                $"Router selected '{decision.AgentId}' (confidence {decision.Confidence:F2}) but provided no reasoning.");
        }

        return decision.Confidence switch
        {
            >= 0.8 => CreateMetric(RoutingMetricName, 5, EvaluationRating.Exceptional,
                $"Router selected '{decision.AgentId}' with high confidence ({decision.Confidence:F2}). Reasoning: {decision.Reasoning}"),
            >= 0.6 => CreateMetric(RoutingMetricName, 4, EvaluationRating.Good,
                $"Router selected '{decision.AgentId}' with moderate confidence ({decision.Confidence:F2}). Reasoning: {decision.Reasoning}"),
            >= 0.4 => CreateMetric(RoutingMetricName, 3, EvaluationRating.Average,
                $"Router selected '{decision.AgentId}' with low confidence ({decision.Confidence:F2}). Reasoning: {decision.Reasoning}"),
            _ => CreateMetric(RoutingMetricName, 2, EvaluationRating.Poor,
                $"Router selected '{decision.AgentId}' with very low confidence ({decision.Confidence:F2}). Reasoning: {decision.Reasoning}")
        };
    }

    /// <summary>
    /// Were the expected agents selected by the routing decision?
    /// </summary>
    private static NumericMetric EvaluateAgentTargeting(A2AToolCallEvaluatorContext context)
    {
        var decision = context.RoutingDecision;
        var expected = context.ExpectedAgentIds;

        if (decision is null || expected.Count == 0)
        {
            return CreateMetric(AgentTargetingMetricName, 1, EvaluationRating.Unacceptable,
                "No routing decision or no expected agents specified.");
        }

        // Collect all routed agent IDs
        var routed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { decision.AgentId };
        if (decision.AdditionalAgents is { Count: > 0 })
        {
            foreach (var additional in decision.AdditionalAgents)
            {
                routed.Add(additional);
            }
        }

        var matched = expected.Count(id => routed.Any(r => r.Contains(id, StringComparison.OrdinalIgnoreCase)));
        var ratio = (double)matched / expected.Count;

        var routedList = string.Join(", ", routed);
        var expectedList = string.Join(", ", expected);

        return ratio switch
        {
            1.0 => CreateMetric(AgentTargetingMetricName, 5, EvaluationRating.Exceptional,
                $"All expected agents targeted. Routed: [{routedList}], Expected: [{expectedList}]."),
            >= 0.5 => CreateMetric(AgentTargetingMetricName, 3, EvaluationRating.Average,
                $"Partial match: {matched}/{expected.Count} expected agents targeted. Routed: [{routedList}], Expected: [{expectedList}]."),
            _ => CreateMetric(AgentTargetingMetricName, 1, EvaluationRating.Unacceptable,
                $"Targeting miss: {matched}/{expected.Count} expected agents targeted. Routed: [{routedList}], Expected: [{expectedList}].")
        };
    }

    /// <summary>
    /// Did all dispatched agents execute successfully?
    /// </summary>
    private static NumericMetric EvaluateAgentExecution(A2AToolCallEvaluatorContext context)
    {
        var responses = context.AgentResponses;

        if (responses.Count == 0)
        {
            return CreateMetric(AgentExecutionMetricName, 1, EvaluationRating.Unacceptable,
                "No agents were dispatched or no responses captured.");
        }

        var succeeded = responses.Count(r => r.Success);
        var failed = responses.Where(r => !r.Success).ToList();

        if (failed.Count == 0)
        {
            var agentSummary = string.Join(", ", responses.Select(r => $"{r.AgentId} ({r.ExecutionTimeMs}ms)"));
            return CreateMetric(AgentExecutionMetricName, 5, EvaluationRating.Exceptional,
                $"All {succeeded} agent(s) executed successfully: [{agentSummary}].");
        }

        var failSummary = string.Join("; ", failed.Select(r => $"{r.AgentId}: {r.ErrorMessage ?? "unknown error"}"));
        var ratio = (double)succeeded / responses.Count;

        return ratio switch
        {
            >= 0.5 => CreateMetric(AgentExecutionMetricName, 3, EvaluationRating.Average,
                $"{succeeded}/{responses.Count} agents succeeded. Failures: [{failSummary}]."),
            _ => CreateMetric(AgentExecutionMetricName, 1, EvaluationRating.Unacceptable,
                $"Only {succeeded}/{responses.Count} agents succeeded. Failures: [{failSummary}].")
        };
    }

    /// <summary>
    /// Was a non-empty aggregated response produced?
    /// </summary>
    private static NumericMetric EvaluateAggregation(A2AToolCallEvaluatorContext context)
    {
        if (string.IsNullOrWhiteSpace(context.AggregatedResponse))
        {
            return CreateMetric(AggregationMetricName, 1, EvaluationRating.Unacceptable,
                "No aggregated response was produced.");
        }

        if (context.AggregatedResponse.Length < 10)
        {
            return CreateMetric(AggregationMetricName, 2, EvaluationRating.Poor,
                $"Aggregated response is suspiciously short ({context.AggregatedResponse.Length} chars).");
        }

        return CreateMetric(AggregationMetricName, 5, EvaluationRating.Exceptional,
            $"Aggregated response produced ({context.AggregatedResponse.Length} chars).");
    }

    /// <summary>
    /// Composite score: average of the four sub-dimensions, rounded.
    /// </summary>
    private static NumericMetric EvaluateWorkflowComposite(
        NumericMetric routing,
        NumericMetric targeting,
        NumericMetric execution,
        NumericMetric aggregation)
    {
        var values = new[] { routing.Value, targeting.Value, execution.Value, aggregation.Value };
        var validValues = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();

        if (validValues.Count == 0)
        {
            return CreateMetric(WorkflowMetricName, 1, EvaluationRating.Unacceptable,
                "Unable to compute composite — no sub-metrics have values.");
        }

        var avg = validValues.Average();
        var rounded = (int)Math.Round(avg);

        var rating = rounded switch
        {
            >= 5 => EvaluationRating.Exceptional,
            4 => EvaluationRating.Good,
            3 => EvaluationRating.Average,
            2 => EvaluationRating.Poor,
            _ => EvaluationRating.Unacceptable
        };

        var details = $"Routing={routing.Value}, Targeting={targeting.Value}, Execution={execution.Value}, Aggregation={aggregation.Value}";

        return CreateMetric(WorkflowMetricName, rounded, rating,
            $"Composite A2A workflow score: {avg:F1}/5. [{details}]");
    }

    private static NumericMetric CreateMetric(string name, int score, EvaluationRating rating, string reason)
    {
        var metric = new NumericMetric(name, value: score, reason);
        metric.Interpretation = new EvaluationMetricInterpretation(rating, reason: reason);
        return metric;
    }

    private EvaluationResult CreateMissingContextResult()
    {
        var metrics = EvaluationMetricNames.Select(name =>
        {
            var m = new NumericMetric(name, value: null);
            m.Interpretation = new EvaluationMetricInterpretation(
                EvaluationRating.Unknown,
                failed: true,
                reason: "No A2AToolCallEvaluatorContext was provided.");
            return (EvaluationMetric)m;
        }).ToArray();

        return new EvaluationResult(metrics);
    }
}
