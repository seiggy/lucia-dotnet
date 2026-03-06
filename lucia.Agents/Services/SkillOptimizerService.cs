using lucia.Agents.Abstractions;
using lucia.Agents.Models;
using lucia.Agents.Models.HomeAssistant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace lucia.Agents.Services;

/// <summary>
/// Reusable coordinate descent optimizer for <see cref="IHybridEntityMatcher"/>
/// parameters. Extracted from test code to allow both unit tests and the
/// runtime API (skill optimizer UI) to share the same algorithm.
///
/// <para><b>Algorithm:</b> Alternates between threshold, weight, and dropoff,
/// walking each in the direction that improves a weighted score. When all three
/// stall, the step size is halved. Converges when step &lt; minStep or a perfect
/// score is reached.</para>
///
/// <para><b>Scoring:</b> Each test case contributes up to <see cref="FoundWeight"/>
/// points for finding the correct entity and <see cref="CountWeight"/> points for
/// staying within the expected result count limit.</para>
/// </summary>
public sealed class SkillOptimizerService
{
    private readonly IHybridEntityMatcher _matcher;
    private readonly ILogger<SkillOptimizerService> _logger;

    /// <summary>Score weight for finding the correct entity.</summary>
    public const double FoundWeight = 3.0;

    /// <summary>Score weight for staying within the max result count.</summary>
    public const double CountWeight = 1.0;

    /// <summary>Maximum score per test case (FoundWeight + CountWeight).</summary>
    public static readonly double MaxScorePerCase = FoundWeight + CountWeight;

    public SkillOptimizerService(
        IHybridEntityMatcher matcher,
        ILogger<SkillOptimizerService> logger)
    {
        _matcher = matcher;
        _logger = logger;
    }

    /// <summary>
    /// Runs the coordinate descent optimizer to find the best
    /// (Threshold, EmbeddingWeight, ScoreDropoffRatio) for the given
    /// test cases and entity candidates.
    /// </summary>
    /// <typeparam name="T">Matchable entity type.</typeparam>
    /// <param name="testCases">Test cases defining expected outcomes.</param>
    /// <param name="candidates">Cached entities to search against.</param>
    /// <param name="embeddingGenerator">Embedding generator for the chosen model.</param>
    /// <param name="initialParams">Starting parameter values (defaults if null).</param>
    /// <param name="onProgress">Optional callback for progress updates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The optimization result with best parameters and per-case breakdown.</returns>
    public async Task<OptimizationResult> OptimizeAsync<T>(
        IReadOnlyList<OptimizationTestCase> testCases,
        IReadOnlyList<T> candidates,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        HybridMatchOptions? initialParams = null,
        Func<OptimizationProgress, Task>? onProgress = null,
        CancellationToken cancellationToken = default) where T : IMatchableEntity
    {
        var initial = initialParams ?? new HybridMatchOptions();
        var maxScore = testCases.Count * MaxScorePerCase;

        // Evaluation cache keyed by (threshold, weight, dropoff, penalty, margin)
        var cache = new Dictionary<(double T, double W, double D, double P, double M), (double Score, List<OptimizationCaseResult> Cases)>();

        async Task<(double Score, List<OptimizationCaseResult> Cases)> EvaluateAsync(
            double threshold, double weight, double dropoff, double penalty, double margin)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = (Math.Round(threshold, 4), Math.Round(weight, 4), Math.Round(dropoff, 4), Math.Round(penalty, 4), Math.Round(margin, 4));
            if (cache.TryGetValue(key, out var cached))
                return cached;

            var options = new HybridMatchOptions
            {
                Threshold = key.Item1,
                EmbeddingWeight = key.Item2,
                ScoreDropoffRatio = key.Item3,
                DisagreementPenalty = key.Item4,
                EmbeddingResolutionMargin = key.Item5
            };

            double score = 0;
            var caseResults = new List<OptimizationCaseResult>();

            foreach (var tc in testCases)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var matches = await _matcher.FindMatchesAsync(
                    tc.SearchTerm,
                    candidates,
                    embeddingGenerator,
                    options,
                    cancellationToken).ConfigureAwait(false);

                var matchedEntityIds = matches.Select(m =>
                    m.Entity is HomeAssistantEntity he ? he.EntityId : m.Entity.MatchableName).ToList();

                var foundIds = new List<string>();
                var missedIds = new List<string>();

                foreach (var expected in tc.ExpectedEntityIds)
                {
                    if (matchedEntityIds.Any(id => string.Equals(id, expected, StringComparison.OrdinalIgnoreCase)))
                        foundIds.Add(expected);
                    else
                        missedIds.Add(expected);
                }

                var expectedCount = tc.ExpectedEntityIds.Count;
                var count = matches.Count;
                var countOk = count <= tc.MaxResults;
                var allFound = missedIds.Count == 0 && expectedCount > 0;

                // Recall: +3.0 if all found and within limits, negative when misses or over max
                double recallScore;
                if (allFound && countOk)
                {
                    recallScore = FoundWeight;
                }
                else if (allFound)
                {
                    // All expected found but too many results — over-matching penalty
                    var overflowRatio = (double)(count - tc.MaxResults) / tc.MaxResults;
                    recallScore = -overflowRatio * FoundWeight;
                }
                else if (expectedCount > 0)
                {
                    var missRatio = (double)missedIds.Count / expectedCount;
                    recallScore = -missRatio * FoundWeight;
                }
                else
                {
                    recallScore = 0;
                }

                // Precision: +1.0 for exact match, partial for extras within limit
                double precisionScore;
                if (allFound && count == expectedCount)
                {
                    precisionScore = CountWeight;
                }
                else if (allFound && countOk && count > 0)
                {
                    precisionScore = CountWeight * ((double)expectedCount / count);
                }
                else
                {
                    precisionScore = 0;
                }

                var caseScore = recallScore + precisionScore;
                score += caseScore;

                caseResults.Add(new OptimizationCaseResult
                {
                    TestCase = tc,
                    Found = allFound,
                    FoundEntityIds = foundIds,
                    MissedEntityIds = missedIds,
                    MatchCount = count,
                    CountWithinLimit = countOk,
                    CaseScore = caseScore
                });
            }

            var entry = (score, caseResults);
            cache[key] = entry;
            return entry;
        }

        static double Clamp(double v) => Math.Clamp(Math.Round(v, 4), 0.05, 0.95);

        // --- Coordinate descent with adaptive step size ---
        var threshold = initial.Threshold;
        var weight = initial.EmbeddingWeight;
        var dropoff = initial.ScoreDropoffRatio;
        var penalty = initial.DisagreementPenalty;
        var margin = initial.EmbeddingResolutionMargin;
        var step = 0.10;
        const double minStep = 0.01;

        var (bestScore, bestCases) = await EvaluateAsync(threshold, weight, dropoff, penalty, margin).ConfigureAwait(false);

        _logger.LogInformation("Optimizer init: T={Threshold:F4} W={Weight:F4} D={Dropoff:F4} P={Penalty:F4} M={Margin:F4} Score={Score:F1}/{Max:F0}",
            threshold, weight, dropoff, penalty, margin, bestScore, maxScore);

        if (onProgress is not null)
        {
            await onProgress(CreateProgress(0, bestScore, maxScore, threshold, weight, dropoff, penalty, margin, step, cache.Count, "initialized")).ConfigureAwait(false);
        }

        var iteration = 0;
        while (step >= minStep && bestScore < maxScore)
        {
            cancellationToken.ThrowIfCancellationRequested();
            iteration++;
            var improved = false;

            // --- Walk threshold ---
            var tResult = await WalkParameterAsync(
                current: threshold,
                step: step,
                evaluate: t => EvaluateAsync(Clamp(t), weight, dropoff, penalty, margin),
                bestScore: bestScore,
                cancellationToken).ConfigureAwait(false);

            if (tResult.Score > bestScore)
            {
                threshold = tResult.Value;
                bestScore = tResult.Score;
                bestCases = tResult.Cases;
                improved = true;
                _logger.LogDebug("Iter {Iter}: threshold→{T:F4} score={Score:F1}", iteration, threshold, bestScore);
            }

            // --- Walk weight ---
            var wResult = await WalkParameterAsync(
                current: weight,
                step: step,
                evaluate: w => EvaluateAsync(threshold, Clamp(w), dropoff, penalty, margin),
                bestScore: bestScore,
                cancellationToken).ConfigureAwait(false);

            if (wResult.Score > bestScore)
            {
                weight = wResult.Value;
                bestScore = wResult.Score;
                bestCases = wResult.Cases;
                improved = true;
                _logger.LogDebug("Iter {Iter}: weight→{W:F4} score={Score:F1}", iteration, weight, bestScore);
            }

            // --- Walk dropoff ---
            var dResult = await WalkParameterAsync(
                current: dropoff,
                step: step,
                evaluate: d => EvaluateAsync(threshold, weight, Clamp(d), penalty, margin),
                bestScore: bestScore,
                cancellationToken).ConfigureAwait(false);

            if (dResult.Score > bestScore)
            {
                dropoff = dResult.Value;
                bestScore = dResult.Score;
                bestCases = dResult.Cases;
                improved = true;
                _logger.LogDebug("Iter {Iter}: dropoff→{D:F4} score={Score:F1}", iteration, dropoff, bestScore);
            }

            // --- Walk penalty ---
            var pResult = await WalkParameterAsync(
                current: penalty,
                step: step,
                evaluate: p => EvaluateAsync(threshold, weight, dropoff, Clamp(p), margin),
                bestScore: bestScore,
                cancellationToken).ConfigureAwait(false);

            if (pResult.Score > bestScore)
            {
                penalty = pResult.Value;
                bestScore = pResult.Score;
                bestCases = pResult.Cases;
                improved = true;
                _logger.LogDebug("Iter {Iter}: penalty→{P:F4} score={Score:F1}", iteration, penalty, bestScore);
            }

            // --- Walk margin ---
            var mResult = await WalkParameterAsync(
                current: margin,
                step: step,
                evaluate: m => EvaluateAsync(threshold, weight, dropoff, penalty, Clamp(m)),
                bestScore: bestScore,
                cancellationToken).ConfigureAwait(false);

            if (mResult.Score > bestScore)
            {
                margin = mResult.Value;
                bestScore = mResult.Score;
                bestCases = mResult.Cases;
                improved = true;
                _logger.LogDebug("Iter {Iter}: margin→{M:F4} score={Score:F1}", iteration, margin, bestScore);
            }

            if (!improved)
            {
                step /= 2.0;
                _logger.LogDebug("Iter {Iter}: no improvement — halving step→{Step:F4}", iteration, step);
            }

            if (onProgress is not null)
            {
                var msg = improved
                    ? $"improved T={threshold:F4} W={weight:F4} D={dropoff:F4} P={penalty:F4} M={margin:F4}"
                    : $"halving step→{step:F4}";

                await onProgress(CreateProgress(iteration, bestScore, maxScore, threshold, weight, dropoff, penalty, margin, step, cache.Count, msg)).ConfigureAwait(false);
            }
        }

        _logger.LogInformation(
            "Optimizer done: T={Threshold:F4} W={Weight:F4} D={Dropoff:F4} P={Penalty:F4} M={Margin:F4} Score={Score:F1}/{Max:F0} ({Points} points, {Iters} iters)",
            threshold, weight, dropoff, penalty, margin, bestScore, maxScore, cache.Count, iteration);

        // Send final progress
        if (onProgress is not null)
        {
            await onProgress(CreateProgress(iteration, bestScore, maxScore, threshold, weight, dropoff, penalty, margin, step, cache.Count, "complete", isComplete: true)).ConfigureAwait(false);
        }

        return new OptimizationResult
        {
            BestParams = new HybridMatchOptions
            {
                Threshold = threshold,
                EmbeddingWeight = weight,
                ScoreDropoffRatio = dropoff,
                DisagreementPenalty = penalty,
                EmbeddingResolutionMargin = margin
            },
            Score = bestScore,
            MaxScore = maxScore,
            CaseResults = bestCases,
            TotalEvaluatedPoints = cache.Count,
            TotalIterations = iteration
        };
    }

    /// <summary>
    /// Walks a single parameter in the direction that improves the weighted
    /// score. Tries +step first, then −step. Keeps walking in whichever
    /// direction improves the score until it stops improving or hits bounds.
    /// </summary>
    private static async Task<(double Value, double Score, List<OptimizationCaseResult> Cases)> WalkParameterAsync(
        double current,
        double step,
        Func<double, Task<(double Score, List<OptimizationCaseResult> Cases)>> evaluate,
        double bestScore,
        CancellationToken cancellationToken)
    {
        var bestValue = current;
        List<OptimizationCaseResult>? bestCases = null;

        // Try positive direction first
        var direction = +1;
        var candidate = Math.Clamp(Math.Round(current + direction * step, 4), 0.05, 0.95);
        var (score, cases) = await evaluate(candidate).ConfigureAwait(false);

        if (score <= bestScore)
        {
            direction = -1;
            candidate = Math.Clamp(Math.Round(current + direction * step, 4), 0.05, 0.95);
            (score, cases) = await evaluate(candidate).ConfigureAwait(false);
        }

        if (score <= bestScore)
            return (bestValue, bestScore, bestCases ?? []);

        // Found improvement — keep walking this direction
        while (score > bestScore)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bestValue = candidate;
            bestScore = score;
            bestCases = cases;

            candidate = Math.Clamp(Math.Round(bestValue + direction * step, 4), 0.05, 0.95);
            if (Math.Abs(candidate - bestValue) < 0.001)
                break;

            (score, cases) = await evaluate(candidate).ConfigureAwait(false);
        }

        return (bestValue, bestScore, bestCases ?? []);
    }

    private static OptimizationProgress CreateProgress(
        int iteration, double score, double maxScore,
        double threshold, double weight, double dropoff, double penalty, double margin,
        double step, int evaluatedPoints, string? message,
        bool isComplete = false)
    {
        return new OptimizationProgress
        {
            Iteration = iteration,
            CurrentScore = score,
            MaxScore = maxScore,
            BestParams = new HybridMatchOptions
            {
                Threshold = threshold,
                EmbeddingWeight = weight,
                ScoreDropoffRatio = dropoff,
                DisagreementPenalty = penalty,
                EmbeddingResolutionMargin = margin
            },
            Step = step,
            EvaluatedPoints = evaluatedPoints,
            IsComplete = isComplete,
            Message = message
        };
    }
}
