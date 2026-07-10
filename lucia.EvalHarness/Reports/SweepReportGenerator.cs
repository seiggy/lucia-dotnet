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
        sb.AppendLine($"## Baseline: {baselineModel} (mean score: {baselineMean:F1})");
        sb.AppendLine();
        sb.AppendLine("| Agent | Score (run 1) |");
        sb.AppendLine("|-------|---------------|");
        foreach (var br in result.BaselineResults)
        {
            sb.AppendLine($"| {br.AgentName} | {br.OverallScore:F1} |");
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
            var worstEntry = entries.OrderBy(e => e.MeanScore).First();
            sb.AppendLine($"- **Best config:** {bestEntry.Profile.ToSummary()} -> **{bestEntry.MeanScore:F1}** (delta from baseline mean: {bestEntry.MeanScore - baselineMean:+0.0;-0.0}, σ={bestEntry.ScoreStdDev:F2})");
            sb.AppendLine($"- **Worst config:** {worstEntry.Profile.ToSummary()} -> **{worstEntry.MeanScore:F1}**");
            sb.AppendLine($"- **Score range:** {worstEntry.MeanScore:F1} - {bestEntry.MeanScore:F1}");
            sb.AppendLine();

            // Full results table
            sb.AppendLine("| # | Temperature | Top-K | Top-P | Repeat | Mean Score | σ | Delta from Baseline | Avg Latency |");
            sb.AppendLine("|---|-------------|-------|-------|--------|------------|---|---------------------|-------------|");

            var rank = 1;
            foreach (var entry in entries.OrderByDescending(e => e.MeanScore))
            {
                var delta = entry.MeanScore - baselineMean;
                var marker = entry == bestEntry ? " *" : "";
                sb.AppendLine(
                    $"| {rank++}{marker} | {entry.Profile.Temperature} | {entry.Profile.TopK} | " +
                    $"{entry.Profile.TopP} | {entry.Profile.RepeatPenalty} | " +
                    $"{entry.MeanScore:F1} | {entry.ScoreStdDev:F2} | {delta:+0.0;-0.0} | {entry.AverageLatencyMs:F0}ms |");
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
                var baseScore = baseline?.OverallScore ?? 0;
                var delta = agentResult.OverallScore - baseScore;
                sb.AppendLine($"| {agentResult.AgentName} | {agentResult.OverallScore:F1} | {baseScore:F1} | {delta:+0.0;-0.0} |");
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
            var delta = best.MeanScore - baselineMean;
            var pct = baselineMean > 0 ? best.MeanScore / baselineMean * 100 : 0;
            sb.AppendLine($"- **{targetModel}:** Use `{best.Profile.ToSummary()}` -> {best.MeanScore:F1} ({pct:F0}% of baseline mean, {delta:+0.0;-0.0} delta)");
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
            agents = result.BaselineResults.Select(r => new { r.AgentName, r.OverallScore })
        },
        targets = result.TargetResults.Select(kvp => new
        {
            model = kvp.Key,
            // Use SelectWinner so the JSON best-config matches what the runner chose
            bestConfig = kvp.Value
                .OrderByDescending(e => e.MeanScore)
                .ThenBy(e => e.ScoreVariance)
                .Select(e => new
                {
                    parameters = new
                    {
                        e.Profile.Name,
                        e.Profile.Temperature,
                        e.Profile.TopK,
                        e.Profile.TopP,
                        e.Profile.RepeatPenalty
                    },
                    // Emit both averageScore (backward compat) and meanScore (new)
                    averageScore = e.AverageScore,
                    meanScore = e.MeanScore,
                    scoreVariance = e.ScoreVariance,
                    scoreStdDev = e.ScoreStdDev,
                    minRunMean = e.MinRunMean,
                    runCount = e.AllRunResults.Count,
                    averageLatencyMs = e.AverageLatencyMs,
                    agents = e.Results.Select(r => new { r.AgentName, r.OverallScore })
                }).FirstOrDefault(),
            allConfigs = kvp.Value.OrderByDescending(e => e.MeanScore).Select(e => new
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
                scoreVariance = e.ScoreVariance,
                scoreStdDev = e.ScoreStdDev,
                runCount = e.AllRunResults.Count,
                averageLatencyMs = e.AverageLatencyMs
            })
        })
    };
}
