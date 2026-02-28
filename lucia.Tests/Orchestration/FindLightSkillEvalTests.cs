#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

using System.Text.RegularExpressions;
using lucia.Agents.Configuration;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Tests the <c>FindLightAsync</c> skill method directly against real embeddings
/// and the live Home Assistant entity cache. Each test case supplies a search term
/// (including STT-artifact variants) and asserts that the expected entity is found
/// and that the total match count does not exceed the expected maximum.
/// This bypasses the LLM entirely — we are evaluating the embedding similarity
/// algorithm and its resilience to speech-to-text noise.
/// </summary>
[Trait("Category", "Eval")]
public sealed class FindLightSkillEvalTests : AgentEvalTestBase
{
    private readonly ITestOutputHelper _output;

    public FindLightSkillEvalTests(EvalTestFixture fixture, ITestOutputHelper output)
        : base(fixture)
    {
        _output = output;
    }

    /// <summary>
    /// Cross-product of (embeddingModelId) × (searchTerm, expectedEntityId, expectedMaxResults, variant).
    /// Each embedding model is tested against every STT variant.
    /// </summary>
    private static IEnumerable<object[]> WithVariants(
        params (string SearchTerm, string ExpectedEntityId, int ExpectedMaxResults, string Variant)[] cases)
    {
        foreach (var modelPair in ModelIds)
        {
            var embeddingModelId = (string)modelPair[1];
            foreach (var (searchTerm, expectedEntityId, expectedMaxResults, variant) in cases)
            {
                yield return [embeddingModelId, searchTerm, expectedEntityId, expectedMaxResults, variant];
            }
        }
    }

    // ── Kitchen Lights ──────────────────────────────────────────────
    public static IEnumerable<object[]> KitchenLightPrompts => WithVariants(
        ("Kitchen Lights", "light.kitchen_lights_light", 1, "exact"),
        ("kitchen light", "light.kitchen_lights_light", 1, "lowercase"),
        ("Kitchen Light", "light.kitchen_lights_light", 1, "singular"),
        ("Kichen Light", "light.kitchen_lights_light", 1, "stt-typo"),
        ("Kitchin Lite", "light.kitchen_lights_light", 1, "stt-phonetic"));

    // ── Zack's Light ────────────────────────────────────────────────
    public static IEnumerable<object[]> ZacksLightPrompts => WithVariants(
        ("Zack\u2019s Light", "light.zacks_light", 2, "exact"),
        ("Zach's Light", "light.zacks_light", 2, "stt-spelling"),
        ("Sack's Light", "light.zacks_light", 2, "stt-lisp-sack"),
        ("Zag's Light", "light.zacks_light", 2, "stt-lisp-zag"),
        ("Zagslight", "light.zacks_light", 2, "stt-lisp-zaglight"),
        ("Sag's Light", "light.zacks_light", 2, "stt-lisp-sag"));

    // ── Garage Lights ───────────────────────────────────────────────
    public static IEnumerable<object[]> GarageLightPrompts => WithVariants(
        ("Garage Lights", "light.garage_lights", 3, "exact"),
        ("garage light", "light.garage_lights", 3, "lowercase"),
        ("Garaj Light", "light.garage_lights", 3, "stt-phonetic"));

    // ── Dianna's Lamp ───────────────────────────────────────────────
    public static IEnumerable<object[]> DiannasLampPrompts => WithVariants(
        ("Dianna\u2019s Lamp", "light.diannas_lamp", 3, "exact"),
        ("Diana's Lamp", "light.diannas_lamp", 3, "stt-spelling"),
        ("Dianna's Light", "light.diannas_lamp", 3, "stt-synonym"));

    [Theory]
    [MemberData(nameof(KitchenLightPrompts))]
    public async Task FindLight_KitchenLight(
        string embeddingModelId, string searchTerm, string expectedEntityId,
        int expectedMaxResults, string variant)
    {
        await RunFindLightTest(embeddingModelId, searchTerm, expectedEntityId, expectedMaxResults, variant);
    }

    [Theory]
    [MemberData(nameof(ZacksLightPrompts))]
    public async Task FindLight_ZacksLight(
        string embeddingModelId, string searchTerm, string expectedEntityId,
        int expectedMaxResults, string variant)
    {
        await RunFindLightTest(embeddingModelId, searchTerm, expectedEntityId, expectedMaxResults, variant);
    }

    [Theory]
    [MemberData(nameof(GarageLightPrompts))]
    public async Task FindLight_GarageLight(
        string embeddingModelId, string searchTerm, string expectedEntityId,
        int expectedMaxResults, string variant)
    {
        await RunFindLightTest(embeddingModelId, searchTerm, expectedEntityId, expectedMaxResults, variant);
    }

    [Theory]
    [MemberData(nameof(DiannasLampPrompts))]
    public async Task FindLight_DiannasLamp(
        string embeddingModelId, string searchTerm, string expectedEntityId,
        int expectedMaxResults, string variant)
    {
        await RunFindLightTest(embeddingModelId, searchTerm, expectedEntityId, expectedMaxResults, variant);
    }

    private async Task RunFindLightTest(
        string embeddingModelId, string searchTerm, string expectedEntityId,
        int expectedMaxResults, string variant)
    {
        _output.WriteLine($"[FindLight] embedding={embeddingModelId} search=\"{searchTerm}\" variant={variant}");

        var skill = await Fixture.CreateLightControlSkillAsync(embeddingModelId);
        var result = await skill.FindLightAsync(searchTerm);

        _output.WriteLine($"[FindLight] result: {result}");

        Assert.False(
            string.IsNullOrWhiteSpace(result),
            $"FindLightAsync returned empty for \"{searchTerm}\" ({variant})");

        Assert.DoesNotContain("No matching lights found", result,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("No lights available", result,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(expectedEntityId, result,
            StringComparison.OrdinalIgnoreCase);

        // Validate that the result set is not too broad
        var matchCount = CountMatchesInResult(result);
        _output.WriteLine($"[FindLight] matchCount={matchCount} expectedMax={expectedMaxResults}");

        Assert.True(
            matchCount <= expectedMaxResults,
            $"Too many matches for \"{searchTerm}\" ({variant}): got {matchCount}, expected ≤{expectedMaxResults}");

        _output.WriteLine($"[FindLight] ✅ PASS — found {expectedEntityId} ({matchCount} match(es))");
    }

    // ── Parameter Optimization ────────────────────────────────────────

    /// <summary>
    /// All test cases used for parameter optimization, combining every
    /// <c>FindLight_*</c> test group into a single collection.
    /// </summary>
    private static readonly (string SearchTerm, string ExpectedEntityId, int MaxResults, string Variant)[] AllTestCases =
    [
        // Kitchen
        ("Kitchen Lights", "light.kitchen_lights_light", 1, "exact"),
        ("kitchen light", "light.kitchen_lights_light", 1, "lowercase"),
        ("Kitchen Light", "light.kitchen_lights_light", 1, "singular"),
        ("Kichen Light", "light.kitchen_lights_light", 1, "stt-typo"),
        ("Kitchin Lite", "light.kitchen_lights_light", 1, "stt-phonetic"),
        // Zack's
        ("Zack\u2019s Light", "light.zacks_light", 2, "exact"),
        ("Zach's Light", "light.zacks_light", 2, "stt-spelling"),
        ("Sack's Light", "light.zacks_light", 2, "stt-lisp-sack"),
        ("Zag's Light", "light.zacks_light", 2, "stt-lisp-zag"),
        ("Zagslight", "light.zacks_light", 2, "stt-lisp-zaglight"),
        ("Sag's Light", "light.zacks_light", 2, "stt-lisp-sag"),
        // Garage
        ("Garage Lights", "light.garage_lights", 3, "exact"),
        ("garage light", "light.garage_lights", 3, "lowercase"),
        ("Garaj Light", "light.garage_lights", 3, "stt-phonetic"),
        // Dianna's
        ("Dianna\u2019s Lamp", "light.diannas_lamp", 3, "exact"),
        ("Diana's Lamp", "light.diannas_lamp", 3, "stt-spelling"),
        ("Dianna's Light", "light.diannas_lamp", 3, "stt-synonym"),
    ];

    /// <summary>
    /// Scoring weights for test outcomes. Finding the correct entity is
    /// significantly more important than staying within the result-count
    /// limit — a noisy result set is recoverable, a missed entity is not.
    /// </summary>
    private const double FoundWeight = 3.0;
    private const double CountWeight = 1.0;
    private static readonly double MaxScorePerCase = FoundWeight + CountWeight;

    /// <summary>
    /// Optimizes <see cref="LightControlSkillOptions.HybridSimilarityThreshold"/>
    /// and <see cref="LightControlSkillOptions.EmbeddingWeight"/> using adaptive
    /// coordinate descent with weighted scoring.
    ///
    /// <para><b>Scoring:</b> Each test case contributes up to 3 points for
    /// finding the correct entity (✅) and 1 point for staying within the
    /// expected max result count. A parameter set that finds all lights but
    /// returns extra matches (⚠️) scores higher than one that misses lights
    /// entirely (❌).</para>
    ///
    /// <para><b>Algorithm:</b> Alternates between threshold and weight,
    /// walking each in the direction that improves the weighted score.
    /// When neither can improve, the step size is halved and the search
    /// continues at finer granularity until convergence.</para>
    /// </summary>
    [Trait("Category", "Eval")]
    [Theory]
    [InlineData("text-embedding-3-small")]
    [InlineData("nomic-embed-text")]
    public async Task OptimizeHybridParameters(string embeddingModelId)
    {
        var options = new LightControlSkillOptions();
        var monitor = CreateMutableOptionsMonitor(options);

        _output.WriteLine($"=== Optimizing parameters for {embeddingModelId} ===");
        _output.WriteLine($"Test cases: {AllTestCases.Length}  |  " +
                          $"MaxScore: {AllTestCases.Length * MaxScorePerCase:F0}  |  " +
                          $"Weights: found={FoundWeight} count={CountWeight}");
        _output.WriteLine("");

        var skill = await Fixture.CreateLightControlSkillAsync(embeddingModelId, monitor);

        // Evaluation cache keyed by (threshold, weight, dropoff)
        var cache = new Dictionary<(double T, double W, double D), ParameterResult>();

        async Task<ParameterResult> EvaluateAsync(double threshold, double weight, double dropoff)
        {
            var key = (Math.Round(threshold, 4), Math.Round(weight, 4), Math.Round(dropoff, 4));
            if (cache.TryGetValue(key, out var cached))
                return cached;

            options.HybridSimilarityThreshold = key.Item1;
            options.EmbeddingWeight = key.Item2;
            options.ScoreDropoffRatio = key.Item3;

            double score = 0;
            var caseResults = new List<TestCaseResult>();

            foreach (var tc in AllTestCases)
            {
                var result = await skill.FindLightAsync(tc.SearchTerm);
                var found = !string.IsNullOrWhiteSpace(result)
                    && !result.Contains("No matching lights found", StringComparison.OrdinalIgnoreCase)
                    && !result.Contains("No lights available", StringComparison.OrdinalIgnoreCase)
                    && result.Contains(tc.ExpectedEntityId, StringComparison.OrdinalIgnoreCase);

                var count = CountMatchesInResult(result);
                var countOk = count <= tc.MaxResults;

                var caseScore = (found ? FoundWeight : 0) + (countOk ? CountWeight : 0);
                score += caseScore;

                caseResults.Add(new TestCaseResult(
                    tc.Variant, found, count, tc.MaxResults, caseScore));
            }

            var entry = new ParameterResult(key.Item1, key.Item2, key.Item3, score,
                AllTestCases.Length * MaxScorePerCase, caseResults);
            cache[key] = entry;
            return entry;
        }

        static double Clamp(double v) => Math.Clamp(Math.Round(v, 4), 0.05, 0.95);

        // --- Coordinate descent with adaptive step size ---
        var threshold = options.HybridSimilarityThreshold;
        var weight = options.EmbeddingWeight;
        var dropoff = options.ScoreDropoffRatio;
        var step = 0.10;
        const double minStep = 0.01;

        var best = await EvaluateAsync(threshold, weight, dropoff);
        LogEvaluation("init", best, step);

        var iteration = 0;
        while (step >= minStep && best.Score < best.MaxScore)
        {
            iteration++;
            var improved = false;

            // --- Walk threshold ---
            var tResult = await WalkParameterAsync(
                current: threshold,
                step: step,
                evaluate: t => EvaluateAsync(Clamp(t), weight, dropoff),
                bestScore: best.Score);

            if (tResult.Score > best.Score)
            {
                threshold = tResult.Value;
                best = tResult.Result;
                improved = true;
                LogEvaluation($"iter {iteration} threshold→{threshold:F4}", best, step);
            }

            // --- Walk weight ---
            var wResult = await WalkParameterAsync(
                current: weight,
                step: step,
                evaluate: w => EvaluateAsync(threshold, Clamp(w), dropoff),
                bestScore: best.Score);

            if (wResult.Score > best.Score)
            {
                weight = wResult.Value;
                best = wResult.Result;
                improved = true;
                LogEvaluation($"iter {iteration} weight→{weight:F4}", best, step);
            }

            // --- Walk dropoff ---
            var dResult = await WalkParameterAsync(
                current: dropoff,
                step: step,
                evaluate: d => EvaluateAsync(threshold, weight, Clamp(d)),
                bestScore: best.Score);

            if (dResult.Score > best.Score)
            {
                dropoff = dResult.Value;
                best = dResult.Result;
                improved = true;
                LogEvaluation($"iter {iteration} dropoff→{dropoff:F4}", best, step);
            }

            if (!improved)
            {
                step /= 2.0;
                _output.WriteLine($"[iter {iteration}] no improvement — halving step→{step:F4}");
            }
        }

        // --- Final report ---
        var ranked = cache.Values
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.Threshold)
            .ThenBy(r => r.Weight)
            .ThenBy(r => r.Dropoff)
            .ToList();

        _output.WriteLine("");
        _output.WriteLine($"Evaluated {cache.Count} unique parameter points");
        _output.WriteLine("Threshold | Weight | Dropoff |  Score  |   Max");
        _output.WriteLine("--------- | ------ | ------- | ------- | -----");
        foreach (var r in ranked.Take(20))
            _output.WriteLine($"   {r.Threshold:F4} | {r.Weight:F4} | {r.Dropoff:F4}  | {r.Score,6:F1} | {r.MaxScore:F0}");

        _output.WriteLine("");
        _output.WriteLine($"=== Best: Threshold={best.Threshold:F4}  Weight={best.Weight:F4}  " +
                          $"Dropoff={best.Dropoff:F4}  Score={best.Score:F1}/{best.MaxScore:F0} ===");

        LogTestCaseDetails(best);

        var perfect = ranked.Where(r => r.Score >= r.MaxScore).ToList();
        if (perfect.Count > 0)
        {
            _output.WriteLine("");
            _output.WriteLine($"✅ {perfect.Count} parameter point(s) achieve perfect score:");
            foreach (var p in perfect)
                _output.WriteLine($"   Threshold={p.Threshold:F4}  Weight={p.Weight:F4}  Dropoff={p.Dropoff:F4}");
        }

        Assert.True(
            best.Score >= best.MaxScore,
            $"No parameter combination achieves a perfect score for {embeddingModelId}. " +
            $"Best: Threshold={best.Threshold:F4} Weight={best.Weight:F4} " +
            $"Dropoff={best.Dropoff:F4} Score={best.Score:F1}/{best.MaxScore:F0}");
    }

    /// <summary>
    /// Walks a single parameter in the direction that improves the weighted
    /// score. Tries +step first, then −step. Keeps walking in whichever
    /// direction improves the score until it stops improving or hits bounds.
    /// </summary>
    private static async Task<(double Value, double Score, ParameterResult Result)> WalkParameterAsync(
        double current,
        double step,
        Func<double, Task<ParameterResult>> evaluate,
        double bestScore)
    {
        var bestValue = current;
        ParameterResult? bestResult = null;

        // Try positive direction first
        var direction = +1;
        var candidate = Math.Clamp(Math.Round(current + direction * step, 4), 0.05, 0.95);
        var result = await evaluate(candidate);

        if (result.Score <= bestScore)
        {
            direction = -1;
            candidate = Math.Clamp(Math.Round(current + direction * step, 4), 0.05, 0.95);
            result = await evaluate(candidate);
        }

        if (result.Score <= bestScore)
            return (bestValue, bestScore, null!);

        // Found improvement — keep walking this direction
        while (result.Score > bestScore)
        {
            bestValue = candidate;
            bestScore = result.Score;
            bestResult = result;

            candidate = Math.Clamp(Math.Round(bestValue + direction * step, 4), 0.05, 0.95);
            if (Math.Abs(candidate - bestValue) < 0.001)
                break;

            result = await evaluate(candidate);
        }

        return (bestValue, bestScore, bestResult!);
    }

    private void LogEvaluation(string label, ParameterResult pr, double step)
    {
        _output.WriteLine($"[{label}] T={pr.Threshold:F4}  W={pr.Weight:F4}  " +
                          $"score={pr.Score:F1}/{pr.MaxScore:F0}  step={step:F4}");
        LogTestCaseDetails(pr);
    }

    private void LogTestCaseDetails(ParameterResult pr)
    {
        foreach (var tc in pr.CaseResults)
        {
            var status = tc.CaseScore >= MaxScorePerCase ? "\u2705" :
                         tc.Found ? "\u26a0\ufe0f" : "\u274c";
            var countInfo = tc.Found
                ? $"count={tc.MatchCount}/{tc.MaxExpected}"
                : "not-found";
            _output.WriteLine($"  {status} {tc.Variant,-20} {countInfo,-20} +{tc.CaseScore:F0}");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private sealed record ParameterResult(
        double Threshold,
        double Weight,
        double Dropoff,
        double Score,
        double MaxScore,
        List<TestCaseResult> CaseResults);

    private sealed record TestCaseResult(
        string Variant,
        bool Found,
        int MatchCount,
        int MaxExpected,
        double CaseScore);

    private static IOptionsMonitor<LightControlSkillOptions> CreateMutableOptionsMonitor(
        LightControlSkillOptions options)
    {
        var monitor = FakeItEasy.A.Fake<IOptionsMonitor<LightControlSkillOptions>>();
        FakeItEasy.A.CallTo(() => monitor.CurrentValue).ReturnsLazily(_ => options);
        return monitor;
    }

    /// <summary>
    /// Parses the FindLightAsync result string to count how many matches were returned.
    /// Single-match format: "Found light: ..."
    /// Multi-match format: "Found N matching light(s):\n- ...\n- ..."
    /// </summary>
    private static int CountMatchesInResult(string result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return 0;

        if (result.StartsWith("Found light:", StringComparison.OrdinalIgnoreCase))
            return 1;

        var dashLines = Regex.Matches(result, @"^- ", RegexOptions.Multiline);
        return dashLines.Count > 0 ? dashLines.Count : 1;
    }
}
