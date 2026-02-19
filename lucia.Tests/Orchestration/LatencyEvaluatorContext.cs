#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Provides measured response latency to the <see cref="LatencyEvaluator"/>.
/// Pass an instance of this class via the <c>additionalContext</c> parameter
/// when calling <see cref="Microsoft.Extensions.AI.Evaluation.Reporting.ScenarioRun.EvaluateAsync"/>.
/// </summary>
public sealed class LatencyEvaluatorContext : EvaluationContext
{
    /// <summary>
    /// Gets the measured response latency.
    /// </summary>
    public TimeSpan Latency { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="LatencyEvaluatorContext"/>
    /// with the measured response latency.
    /// </summary>
    /// <param name="latency">The elapsed time from request to response.</param>
    public LatencyEvaluatorContext(TimeSpan latency)
        : base("LatencyContext", [new TextContent($"{latency.TotalMilliseconds:F1} ms")])
    {
        Latency = latency;
    }
}
