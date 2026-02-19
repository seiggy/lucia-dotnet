#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Custom evaluator that scores model response latency on a 1–5 inverse scale.
/// <list type="bullet">
///   <item><term>5</term><description>&lt; 500 ms</description></item>
///   <item><term>4</term><description>500–750 ms</description></item>
///   <item><term>3</term><description>750–1 000 ms</description></item>
///   <item><term>2</term><description>1 000–1 500 ms</description></item>
///   <item><term>1</term><description>&gt; 1 500 ms</description></item>
/// </list>
/// Latency must be supplied via <see cref="LatencyEvaluatorContext"/> in the
/// <c>additionalContext</c> collection.
/// </summary>
public sealed class LatencyEvaluator : IEvaluator
{
    /// <summary>
    /// The name of the metric produced by this evaluator.
    /// </summary>
    public const string LatencyMetricName = "Latency";

    /// <inheritdoc />
    public IReadOnlyCollection<string> EvaluationMetricNames => [LatencyMetricName];

    /// <inheritdoc />
    public ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var context = additionalContext?
            .OfType<LatencyEvaluatorContext>()
            .FirstOrDefault();

        if (context is null)
        {
            var failMetric = new NumericMetric(LatencyMetricName, value: null);
            failMetric.Interpretation = new EvaluationMetricInterpretation(
                EvaluationRating.Unknown,
                failed: true,
                reason: "No LatencyEvaluatorContext was provided. Cannot evaluate latency.");
            return new ValueTask<EvaluationResult>(new EvaluationResult(failMetric));
        }

        var ms = context.Latency.TotalMilliseconds;
        var (score, rating, reason) = ms switch
        {
            < 500 => (5, EvaluationRating.Exceptional, $"Response latency {ms:F0} ms is excellent (< 500 ms)."),
            < 750 => (4, EvaluationRating.Good, $"Response latency {ms:F0} ms is good (500–750 ms)."),
            < 1000 => (3, EvaluationRating.Average, $"Response latency {ms:F0} ms is average (750–1 000 ms)."),
            < 1500 => (2, EvaluationRating.Poor, $"Response latency {ms:F0} ms is below average (1 000–1 500 ms)."),
            _ => (1, EvaluationRating.Unacceptable, $"Response latency {ms:F0} ms is unacceptable (> 1 500 ms).")
        };

        var metric = new NumericMetric(LatencyMetricName, value: score, reason);
        // Latency is informational only — never fail the test assertion on it
        metric.Interpretation = new EvaluationMetricInterpretation(rating, failed: false, reason: reason);

        return new ValueTask<EvaluationResult>(new EvaluationResult(metric));
    }
}
