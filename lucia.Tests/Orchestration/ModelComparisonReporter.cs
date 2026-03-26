#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Aggregates evaluation results across multiple models and scenarios, then
/// generates a markdown comparison report with a Models × Scenarios score matrix
/// and a failure classification breakdown.
/// </summary>
public sealed class ModelComparisonReporter
{
    private readonly List<ModelScenarioResult> _results = [];

    /// <summary>
    /// Records the outcome of running a single scenario on a single model.
    /// </summary>
    public void AddResult(
        string modelId,
        string scenarioName,
        ChatResponse response,
        EvaluationResult evaluation,
        string? expectedTool = null,
        string? expectedEntity = null,
        bool expectsNoToolCall = false)
    {
        var toolCalls = response.Messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .ToList();

        var failureType = ClassifyFailure(
            toolCalls, evaluation, expectedTool, expectedEntity, expectsNoToolCall);

        var score = ExtractPrimaryScore(evaluation);

        _results.Add(new ModelScenarioResult
        {
            ModelId = modelId,
            ScenarioName = scenarioName,
            Score = score,
            FailureType = failureType,
            ToolsCalled = toolCalls.Select(tc => tc.Name ?? "<unknown>").ToList(),
        });
    }

    /// <summary>
    /// Generates a markdown report and writes it to the specified file path.
    /// </summary>
    public void WriteReport(string outputPath)
    {
        var markdown = GenerateMarkdown();
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(outputPath, markdown, Encoding.UTF8);
    }

    /// <summary>
    /// Generates the markdown report content without writing to disk.
    /// </summary>
    public string GenerateMarkdown()
    {
        var sb = new StringBuilder();
        var models = _results.Select(r => r.ModelId).Distinct().OrderBy(m => m).ToList();
        var scenarios = _results.Select(r => r.ScenarioName).Distinct().OrderBy(s => s).ToList();

        sb.AppendLine("# Model Comparison Report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Models: {models.Count} | Scenarios: {scenarios.Count} | Total runs: {_results.Count}");
        sb.AppendLine();

        // ─── Summary table ───
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Model | Pass Rate | Avg Score | Failures |");
        sb.AppendLine("|-------|-----------|-----------|----------|");

        foreach (var model in models)
        {
            var modelResults = _results.Where(r => r.ModelId == model).ToList();
            var passCount = modelResults.Count(r => r.FailureType == FailureType.None);
            var avgScore = modelResults.Average(r => r.Score);
            var failCount = modelResults.Count - passCount;

            sb.AppendLine(
                $"| {model} " +
                $"| {passCount}/{modelResults.Count} ({100.0 * passCount / modelResults.Count:F0}%) " +
                $"| {avgScore.ToString("F1", CultureInfo.InvariantCulture)} " +
                $"| {failCount} |");
        }

        sb.AppendLine();

        // ─── Score matrix ───
        sb.AppendLine("## Score Matrix (Models × Scenarios)");
        sb.AppendLine();

        sb.Append("| Scenario |");
        foreach (var model in models)
            sb.Append($" {model} |");
        sb.AppendLine();

        sb.Append("|----------|");
        foreach (var _ in models)
            sb.Append("--------|");
        sb.AppendLine();

        foreach (var scenario in scenarios)
        {
            sb.Append($"| {scenario} |");
            foreach (var model in models)
            {
                var result = _results.FirstOrDefault(
                    r => r.ModelId == model && r.ScenarioName == scenario);
                if (result is null)
                {
                    sb.Append(" — |");
                }
                else
                {
                    var emoji = result.FailureType == FailureType.None ? "✅" : "❌";
                    sb.Append($" {emoji} {result.Score.ToString("F1", CultureInfo.InvariantCulture)} |");
                }
            }

            sb.AppendLine();
        }

        sb.AppendLine();

        // ─── Failure breakdown ───
        var failures = _results.Where(r => r.FailureType != FailureType.None).ToList();
        if (failures.Count > 0)
        {
            sb.AppendLine("## Failure Breakdown");
            sb.AppendLine();

            var byType = failures
                .GroupBy(f => f.FailureType)
                .OrderByDescending(g => g.Count());

            sb.AppendLine("| Failure Type | Count | Models Affected | Scenarios |");
            sb.AppendLine("|-------------|-------|----------------|-----------|");

            foreach (var group in byType)
            {
                var affectedModels = string.Join(", ",
                    group.Select(g => g.ModelId).Distinct().OrderBy(m => m));
                var affectedScenarios = string.Join(", ",
                    group.Select(g => g.ScenarioName).Distinct().OrderBy(s => s));
                sb.AppendLine(
                    $"| {group.Key} | {group.Count()} | {affectedModels} | {affectedScenarios} |");
            }

            sb.AppendLine();

            // ─── Detailed failures ───
            sb.AppendLine("### Failure Details");
            sb.AppendLine();

            foreach (var failure in failures.OrderBy(f => f.ScenarioName).ThenBy(f => f.ModelId))
            {
                sb.AppendLine(
                    $"- **{failure.ScenarioName}** [{failure.ModelId}]: " +
                    $"`{failure.FailureType}` — tools called: " +
                    $"[{string.Join(", ", failure.ToolsCalled)}]");
            }
        }
        else
        {
            sb.AppendLine("## Failure Breakdown");
            sb.AppendLine();
            sb.AppendLine("🎉 All scenarios passed on all models!");
        }

        return sb.ToString();
    }

    private static FailureType ClassifyFailure(
        IReadOnlyList<FunctionCallContent> toolCalls,
        EvaluationResult evaluation,
        string? expectedTool,
        string? expectedEntity,
        bool expectsNoToolCall)
    {
        var hasFailing = evaluation.Metrics.Any(
            m => m.Value.Interpretation is { Failed: true });

        if (!hasFailing)
            return FailureType.None;

        // No tool call when one was expected
        if (!expectsNoToolCall && expectedTool is not null && toolCalls.Count == 0)
            return FailureType.NoToolCall;

        // Tool call when none expected (hallucination / out-of-domain leak)
        if (expectsNoToolCall && toolCalls.Count > 0)
            return FailureType.Hallucination;

        // Wrong tool called
        if (expectedTool is not null && toolCalls.Count > 0)
        {
            var normalizedExpected = NormalizeFunctionName(expectedTool);
            var anyCorrectTool = toolCalls.Any(tc =>
                tc.Name is not null &&
                string.Equals(
                    NormalizeFunctionName(tc.Name),
                    normalizedExpected,
                    StringComparison.OrdinalIgnoreCase));

            if (!anyCorrectTool)
                return FailureType.WrongTool;
        }

        // Entity resolution failure
        if (expectedEntity is not null && toolCalls.Count > 0)
        {
            var entityArgNames = new[] { "searchTerms", "entity", "entityId", "area" };
            var entityFound = toolCalls.Any(tc =>
                tc.Arguments is not null &&
                tc.Arguments.Any(kvp =>
                    entityArgNames.Any(n =>
                        string.Equals(kvp.Key, n, StringComparison.OrdinalIgnoreCase)) &&
                    (kvp.Value?.ToString()?.Contains(
                        expectedEntity, StringComparison.OrdinalIgnoreCase) ?? false)));

            if (!entityFound)
                return FailureType.WrongEntity;
        }

        // Default: wrong params if we got the right tool but still failed
        if (expectedTool is not null && toolCalls.Count > 0)
            return FailureType.WrongParams;

        // Generic fallback
        return FailureType.WrongTool;
    }

    private static double ExtractPrimaryScore(EvaluationResult evaluation)
    {
        // Prefer SmartHomeToolCallEvaluator score, fall back to any numeric metric
        if (evaluation.TryGet<EvaluationMetric>(SmartHomeToolCallEvaluator.MetricName, out var smartHomeMetric) &&
            smartHomeMetric is NumericMetric numericMetric &&
            numericMetric.Value.HasValue)
        {
            return numericMetric.Value.Value;
        }

        var firstNumeric = evaluation.Metrics.Values
            .OfType<NumericMetric>()
            .FirstOrDefault(m => m.Value.HasValue);

        return firstNumeric?.Value ?? 0.0;
    }

    private static string NormalizeFunctionName(string name) =>
        name.EndsWith("Async", StringComparison.Ordinal) ? name[..^5] : name;
}
