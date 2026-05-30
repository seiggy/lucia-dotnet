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

        // Baseline scores
        var baselineAvg = result.BaselineResults.Average(r => r.OverallScore);
        var baselineModel = result.BaselineResults.FirstOrDefault()?.ModelName ?? "unknown";
        sb.AppendLine($"## Baseline: {baselineModel} (score: {baselineAvg:F1})");
        sb.AppendLine();
        sb.AppendLine("| Agent | Score |");
        sb.AppendLine("|-------|-------|");
        foreach (var br in result.BaselineResults)
        {
            sb.AppendLine($"| {br.AgentName} | {br.OverallScore:F1} |");
        }
        sb.AppendLine();

        // Per-target model sweep results
        foreach (var (targetModel, entries) in result.TargetResults)
        {
            sb.AppendLine($"## {targetModel} — Parameter Sweep Results");
            sb.AppendLine();

            // Summary: best configuration
            var bestEntry = entries.OrderByDescending(e => e.AverageScore).First();
            var worstEntry = entries.OrderBy(e => e.AverageScore).First();
            sb.AppendLine($"- **Best config:** {bestEntry.Profile.ToSummary()} → **{bestEntry.AverageScore:F1}** (Δ from baseline: {bestEntry.AverageScore - baselineAvg:+0.0;-0.0})");
            sb.AppendLine($"- **Worst config:** {worstEntry.Profile.ToSummary()} → **{worstEntry.AverageScore:F1}**");
            sb.AppendLine($"- **Score range:** {worstEntry.AverageScore:F1} – {bestEntry.AverageScore:F1}");
            sb.AppendLine();

            // Full results table
            sb.AppendLine("| # | Temperature | Top-K | Top-P | Repeat | Avg Score | Δ Baseline | Avg Latency |");
            sb.AppendLine("|---|-------------|-------|-------|--------|-----------|------------|-------------|");

            var rank = 1;
            foreach (var entry in entries.OrderByDescending(e => e.AverageScore))
            {
                var delta = entry.AverageScore - baselineAvg;
                var marker = entry == bestEntry ? " ⭐" : "";
                sb.AppendLine(
                    $"| {rank++}{marker} | {entry.Profile.Temperature} | {entry.Profile.TopK} | " +
                    $"{entry.Profile.TopP} | {entry.Profile.RepeatPenalty} | " +
                    $"{entry.AverageScore:F1} | {delta:+0.0;-0.0} | {entry.AverageLatencyMs:F0}ms |");
            }
            sb.AppendLine();

            // Per-agent breakdown for best config
            sb.AppendLine($"### Best Config Breakdown ({bestEntry.Profile.ToSummary()})");
            sb.AppendLine();
            sb.AppendLine("| Agent | Score | Baseline | Delta |");
            sb.AppendLine("|-------|-------|----------|-------|");

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
            var best = entries.OrderByDescending(e => e.AverageScore).First();
            var delta = best.AverageScore - baselineAvg;
            var pct = baselineAvg > 0 ? best.AverageScore / baselineAvg * 100 : 0;
            sb.AppendLine($"- **{targetModel}:** Use `{best.Profile.ToSummary()}` → {best.AverageScore:F1} ({pct:F0}% of baseline, {delta:+0.0;-0.0} delta)");
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
            averageScore = result.BaselineResults.Average(r => r.OverallScore),
            agents = result.BaselineResults.Select(r => new { r.AgentName, r.OverallScore })
        },
        targets = result.TargetResults.Select(kvp => new
        {
            model = kvp.Key,
            bestConfig = kvp.Value.OrderByDescending(e => e.AverageScore).Select(e => new
            {
                parameters = new
                {
                    e.Profile.Name,
                    e.Profile.Temperature,
                    e.Profile.TopK,
                    e.Profile.TopP,
                    e.Profile.RepeatPenalty
                },
                averageScore = e.AverageScore,
                averageLatencyMs = e.AverageLatencyMs,
                agents = e.Results.Select(r => new { r.AgentName, r.OverallScore })
            }).First(),
            allConfigs = kvp.Value.OrderByDescending(e => e.AverageScore).Select(e => new
            {
                parameters = new
                {
                    e.Profile.Temperature,
                    e.Profile.TopK,
                    e.Profile.TopP,
                    e.Profile.RepeatPenalty
                },
                averageScore = e.AverageScore,
                averageLatencyMs = e.AverageLatencyMs
            })
        })
    };
}
