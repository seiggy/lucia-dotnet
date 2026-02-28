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

        // Evaluation cache keyed by (threshold, weight, dropoff)
        var cache = new Dictionary<(double T, double W, double D), (double Score, List<OptimizationCaseResult> Cases)>();

        async Task<(double Score, List<OptimizationCaseResult> Cases)> EvaluateAsync(
            double threshold, double weight, double dropoff)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = (Math.Round(threshold, 4), Math.Round(weight, 4), Math.Round(dropoff, 4));
            if (cache.TryGetValue(key, out var cached))
                return cached;

            var options = new HybridMatchOptions
            {
                Threshold = key.Item1,
                EmbeddingWeight = key.Item2,
                ScoreDropoffRatio = key.Item3
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

                var found = matches.Any(m =>
                    string.Equals(m.Entity.MatchableName, tc.ExpectedEntityId, StringComparison.OrdinalIgnoreCase) ||
                    (m.Entity is Models.LightEntity le &&
                     string.Equals(le.EntityId, tc.ExpectedEntityId, StringComparison.OrdinalIgnoreCase)));

                var count = matches.Count;
                var countOk = count <= tc.MaxResults;
                var caseScore = (found ? FoundWeight : 0) + (countOk ? CountWeight : 0);
                score += caseScore;

                caseResults.Add(new OptimizationCaseResult
                {
                    TestCase = tc,
                    Found = found,
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
        var step = 0.10;
        const double minStep = 0.01;

        var (bestScore, bestCases) = await EvaluateAsync(threshold, weight, dropoff).ConfigureAwait(false);

        _logger.LogInformation("Optimizer init: T={Threshold:F4} W={Weight:F4} D={Dropoff:F4} Score={Score:F1}/{Max:F0}",
            threshold, weight, dropoff, bestScore, maxScore);

        if (onProgress is not null)
        {
            await onProgress(CreateProgress(0, bestScore, maxScore, threshold, weight, dropoff, step, cache.Count, "initialized")).ConfigureAwait(false);
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
                evaluate: t => EvaluateAsync(Clamp(t), weight, dropoff),
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
                evaluate: w => EvaluateAsync(threshold, Clamp(w), dropoff),
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
                evaluate: d => EvaluateAsync(threshold, weight, Clamp(d)),
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

            if (!improved)
            {
                step /= 2.0;
                _logger.LogDebug("Iter {Iter}: no improvement — halving step→{Step:F4}", iteration, step);
            }

            if (onProgress is not null)
            {
                var msg = improved
                    ? $"improved T={threshold:F4} W={weight:F4} D={dropoff:F4}"
                    : $"halving step→{step:F4}";

                await onProgress(CreateProgress(iteration, bestScore, maxScore, threshold, weight, dropoff, step, cache.Count, msg)).ConfigureAwait(false);
            }
        }

        _logger.LogInformation(
            "Optimizer done: T={Threshold:F4} W={Weight:F4} D={Dropoff:F4} Score={Score:F1}/{Max:F0} ({Points} points, {Iters} iters)",
            threshold, weight, dropoff, bestScore, maxScore, cache.Count, iteration);

        // Send final progress
        if (onProgress is not null)
        {
            await onProgress(CreateProgress(iteration, bestScore, maxScore, threshold, weight, dropoff, step, cache.Count, "complete", isComplete: true)).ConfigureAwait(false);
        }

        return new OptimizationResult
        {
            BestParams = new HybridMatchOptions
            {
                Threshold = threshold,
                EmbeddingWeight = weight,
                ScoreDropoffRatio = dropoff
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
        double threshold, double weight, double dropoff,
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
                ScoreDropoffRatio = dropoff
            },
            Step = step,
            EvaluatedPoints = evaluatedPoints,
            IsComplete = isComplete,
            Message = message
        };
    }
}
