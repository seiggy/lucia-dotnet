using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;

namespace lucia.EvalHarness.Reports;

/// <summary>
/// Generates parameter sweep comparison reports showing how each parameter
/// configuration affects model performance relative to a baseline.
/// </summary>
public static class SweepReportGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Exports the sweep results as markdown and JSON files.
    /// Returns the list of written file paths.
    /// </summary>
    public static IReadOnlyList<string> Export(SweepResult result, string reportDir)
    {
        Directory.CreateDirectory(reportDir);

        var timestamp = result.StartedAt.ToString("yyyyMMdd_HHmmss");
        var mdPath = Path.Combine(reportDir, $"sweep-{timestamp}.md");
        var jsonPath = Path.Combine(reportDir, $"sweep-{timestamp}.json");

        File.WriteAllText(mdPath, BuildMarkdown(result));
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(BuildJsonReport(result), JsonOptions));

        return [mdPath, jsonPath];
    }

    private static string BuildMarkdown(SweepResult result)
    {
        var sb = new StringBuilder();
        var duration = result.CompletedAt - result.StartedAt;

        sb.AppendLine("# lucia Parameter Sweep Report");
        sb.AppendLine();
        sb.AppendLine($"| Property | Value |");
        sb.AppendLine($"|----------|-------|");
        sb.AppendLine($"| Run ID | `{result.RunId}` |");
        sb.AppendLine($"| Duration | {duration.TotalSeconds:F1}s |");
        sb.AppendLine($"| Timestamp | {result.StartedAt:yyyy-MM-dd HH:mm:ss} UTC |");
        sb.AppendLine();

        // Baseline scores — BaselineMeanScore is the mean across all N baseline runs
        var baselineMean = result.BaselineMeanScore;
        var baselineModel = result.BaselineResults.FirstOrDefault()?.ModelName ?? "unknown";
        sb.AppendLine($"## Baseline: {baselineModel} (mean score: {FormatScore(baselineMean)})");
        sb.AppendLine();
        sb.AppendLine("| Agent | Score (run 1) |");
        sb.AppendLine("|-------|---------------|");
        foreach (var br in result.BaselineResults)
        {
            sb.AppendLine($"| {br.AgentName} | {FormatScore(br.OverallScore)} |");
        }
        sb.AppendLine();

        // Per-target model sweep results
        foreach (var (targetModel, entries) in result.TargetResults)
        {
            if (!entries.Any()) continue;

            sb.AppendLine($"## {targetModel} -- Parameter Sweep Results");
            sb.AppendLine();

            // Use SweepRunAggregator.SelectWinner so the reported config exactly matches
            // what the runner selected (mean primary, variance tie-breaker).
            var bestEntry = SweepRunAggregator.SelectWinner(entries);
            if (bestEntry is null)
            {
                sb.AppendLine("- **No winner:** all configuration scores are unavailable.");
                sb.AppendLine();
                continue;
            }

            var worstEntry = entries
                .Where(entry => entry.MeanScore.HasValue)
                .OrderBy(entry => entry.MeanScore)
                .First();
            sb.AppendLine($"- **Best config:** {bestEntry.Profile.ToSummary()} -> **{FormatScore(bestEntry.MeanScore)}** (delta from baseline mean: {FormatDelta(bestEntry.MeanScore, baselineMean)}, σ={FormatNumber(bestEntry.ScoreStdDev)})");
            sb.AppendLine($"- **Worst config:** {worstEntry.Profile.ToSummary()} -> **{FormatScore(worstEntry.MeanScore)}**");
            sb.AppendLine($"- **Score range:** {FormatScore(worstEntry.MeanScore)} - {FormatScore(bestEntry.MeanScore)}");
            sb.AppendLine();

            // Full results table
            sb.AppendLine("| # | Temperature | Top-K | Top-P | Repeat | Mean Score | σ | Delta from Baseline | Avg Latency |");
            sb.AppendLine("|---|-------------|-------|-------|--------|------------|---|---------------------|-------------|");

            var rank = 1;
            foreach (var entry in entries.OrderByDescending(e => e.MeanScore).ThenBy(e => e.ScoreVariance ?? double.MaxValue))
            {
                var delta = FormatDelta(entry.MeanScore, baselineMean);
                var marker = entry == bestEntry ? " *" : "";
                sb.AppendLine(
                    $"| {rank++}{marker} | {entry.Profile.Temperature} | {entry.Profile.TopK} | " +
                    $"{entry.Profile.TopP} | {entry.Profile.RepeatPenalty} | " +
                    $"{FormatScore(entry.MeanScore)} | {FormatNumber(entry.ScoreStdDev)} | {delta} | {FormatLatency(entry.AverageLatencyMs)} |");
            }
            sb.AppendLine();

            // Per-agent breakdown for best config
            sb.AppendLine($"### Best Config Breakdown ({bestEntry.Profile.ToSummary()})");
            sb.AppendLine();
            sb.AppendLine("| Agent | Score (run 1) | Baseline | Delta |");
            sb.AppendLine("|-------|---------------|----------|-------|");

            foreach (var agentResult in bestEntry.Results)
            {
                var baseline = result.BaselineResults.FirstOrDefault(b => b.AgentName == agentResult.AgentName);
                var baseScore = baseline?.OverallScore;
                sb.AppendLine($"| {agentResult.AgentName} | {FormatScore(agentResult.OverallScore)} | {FormatScore(baseScore)} | {FormatDelta(agentResult.OverallScore, baseScore)} |");
            }
            sb.AppendLine();
        }

        // Recommendations
        sb.AppendLine("## Recommendations");
        sb.AppendLine();

        foreach (var (targetModel, entries) in result.TargetResults)
        {
            if (!entries.Any()) continue;

            // Use SelectWinner for consistent winner selection with the runner
            var best = SweepRunAggregator.SelectWinner(entries);
            if (best is null)
            {
                sb.AppendLine($"- **{targetModel}:** No winner; all scores are unavailable.");
                continue;
            }

            var pct = baselineMean is > 0 && best.MeanScore.HasValue
                ? best.MeanScore.Value / baselineMean.Value * 100
                : (double?)null;
            sb.AppendLine($"- **{targetModel}:** Use `{best.Profile.ToSummary()}` -> {FormatScore(best.MeanScore)} ({FormatPercent(pct)} of baseline mean, {FormatDelta(best.MeanScore, baselineMean)} delta)");
        }

        return sb.ToString();
    }

    private static object BuildJsonReport(SweepResult result) => new
    {
        runId = result.RunId,
        startedAt = result.StartedAt,
        completedAt = result.CompletedAt,
        durationSeconds = (result.CompletedAt - result.StartedAt).TotalSeconds,
        baseline = new
        {
            model = result.BaselineResults.FirstOrDefault()?.ModelName,
            // Emit both for backward compat and new consumers
            averageScore = result.BaselineMeanScore,
            meanScore = result.BaselineMeanScore,
            scoreStatus = result.BaselineMeanScore.HasValue ? "available" : "unavailable",
            agents = result.BaselineResults.Select(r => new
            {
                r.AgentName,
                r.OverallScore,
                r.OverallScoreStatus,
                r.OverallScoreReason
            })
        },
        targets = result.TargetResults.Select(kvp => new
        {
            model = kvp.Key,
            // Use SelectWinner so bestConfig exactly matches what the runner chose
            bestConfig = BuildBestConfigJson(kvp.Value),
            bestConfigStatus = SweepRunAggregator.SelectWinner(kvp.Value) is null
                ? "unavailable"
                : "available",
            allConfigs = kvp.Value.OrderByDescending(e => e.MeanScore).ThenBy(e => e.ScoreVariance ?? double.MaxValue).Select(e => new
            {
                parameters = new
                {
                    e.Profile.Temperature,
                    e.Profile.TopK,
                    e.Profile.TopP,
                    e.Profile.RepeatPenalty
                },
                // Emit both averageScore (backward compat) and meanScore (new)
                averageScore = e.AverageScore,
                meanScore = e.MeanScore,
                scoreStatus = e.MeanScore.HasValue ? "available" : "unavailable",
                scoreVariance = e.ScoreVariance,
                scoreStdDev = e.ScoreStdDev,
                runCount = e.AllRunResults.Count,
                averageLatencyMs = e.AverageLatencyMs
            })
        })
    };

    /// <summary>
    /// Projects the winning <see cref="SweepEntry"/> (chosen via
    /// <see cref="SweepRunAggregator.SelectWinner"/>) into the JSON bestConfig shape.
    /// Returns null for an empty entry list so the JSON field is omitted.
    /// </summary>
    private static object? BuildBestConfigJson(IReadOnlyList<SweepEntry> entries)
    {
        if (entries.Count == 0) return null;

        var w = SweepRunAggregator.SelectWinner(entries);
        if (w is null) return null;
        return new
        {
            parameters = new
            {
                w.Profile.Name,
                w.Profile.Temperature,
                w.Profile.TopK,
                w.Profile.TopP,
                w.Profile.RepeatPenalty
            },
            // Emit both averageScore (backward compat) and meanScore (new)
            averageScore = w.AverageScore,
            meanScore = w.MeanScore,
            scoreVariance = w.ScoreVariance,
            scoreStdDev = w.ScoreStdDev,
            minRunMean = w.MinRunMean,
            runCount = w.AllRunResults.Count,
            averageLatencyMs = w.AverageLatencyMs,
            agents = w.Results.Select(r => new { r.AgentName, r.OverallScore })
        };
    }

    private static string FormatScore(double? score) =>
        score.HasValue ? score.Value.ToString("F1") : "N/A";

    private static string FormatDelta(double? score, double? baseline) =>
        score.HasValue && baseline.HasValue
            ? $"{score.Value - baseline.Value:+0.0;-0.0}"
            : "N/A";

    private static string FormatPercent(double? percentage) =>
        percentage.HasValue ? $"{percentage.Value:F0}%" : "N/A";

    private static string FormatNumber(double? value) =>
        value.HasValue ? value.Value.ToString("F2") : "N/A";

    private static string FormatLatency(double? milliseconds) =>
        milliseconds.HasValue ? $"{milliseconds.Value:F0}ms" : "N/A";
}
