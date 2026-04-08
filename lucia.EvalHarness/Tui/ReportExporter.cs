using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Infrastructure;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Exports evaluation results to markdown and JSON files on disk.
/// </summary>
public static class ReportExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new TimeSpanJsonConverter() }
    };

    /// <summary>
    /// Writes markdown and JSON reports to the specified directory.
    /// Returns the paths of the written files.
    /// </summary>
    public static IReadOnlyList<string> Export(EvalRunResult result, GpuInfo gpuInfo, string reportDir)
    {
        Directory.CreateDirectory(reportDir);

        var timestamp = result.StartedAt.ToString("yyyyMMdd_HHmmss");
        var mdPath = Path.Combine(reportDir, $"eval-{timestamp}.md");
        var jsonPath = Path.Combine(reportDir, $"eval-{timestamp}.json");

        File.WriteAllText(mdPath, BuildMarkdown(result, gpuInfo));
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(BuildJsonReport(result, gpuInfo), JsonOptions));

        return [mdPath, jsonPath];
    }

    private static string BuildMarkdown(EvalRunResult result, GpuInfo gpuInfo)
    {
        var sb = new StringBuilder();
        var duration = result.CompletedAt - result.StartedAt;

        sb.AppendLine("# lucia Eval Harness Report");
        sb.AppendLine();
        sb.AppendLine($"| Property | Value |");
        sb.AppendLine($"|----------|-------|");
        sb.AppendLine($"| Run ID | `{result.RunId}` |");
        sb.AppendLine($"| Duration | {duration.TotalSeconds:F1}s |");
        sb.AppendLine($"| GPU | {gpuInfo.GpuLabel} |");
        sb.AppendLine($"| Timestamp | {result.StartedAt:yyyy-MM-dd HH:mm:ss} UTC |");
        sb.AppendLine();

        // ── Model Parameters ────────────────────────────────────────
        AppendModelParameters(sb, result);

        // ── Quality matrix ──────────────────────────────────────────
        sb.AppendLine("## Quality Scores (0–100)");
        sb.AppendLine();

        var allModels = result.AgentResults
            .SelectMany(a => a.ModelResults)
            .Select(m => m.ModelName)
            .Distinct()
            .ToList();

        sb.Append("| Agent |");
        foreach (var m in allModels) sb.Append($" {m} |");
        sb.AppendLine();
        sb.Append("|-------|");
        foreach (var _ in allModels) sb.Append("--------|");
        sb.AppendLine();

        foreach (var agentResult in result.AgentResults)
        {
            sb.Append($"| **{agentResult.AgentName}** |");
            foreach (var model in allModels)
            {
                var mr = agentResult.ModelResults.FirstOrDefault(m => m.ModelName == model);
                sb.Append(mr is not null ? $" {mr.OverallScore:F1} |" : " N/A |");
            }
            sb.AppendLine();
        }
        sb.AppendLine();

        // ── Performance matrix ──────────────────────────────────────
        sb.AppendLine("## Performance (Latency)");
        sb.AppendLine();
        sb.AppendLine("| Model | Mean | Median | P95 | Min | Max | Runs |");
        sb.AppendLine("|-------|------|--------|-----|-----|-----|------|");

        var perfByModel = result.AgentResults
            .SelectMany(a => a.ModelResults)
            .GroupBy(m => m.ModelName)
            .Select(g => (
                Name: g.Key,
                MeanMs: g.Average(m => m.Performance.MeanLatency.TotalMilliseconds),
                MedianMs: g.Average(m => m.Performance.MedianLatency.TotalMilliseconds),
                P95Ms: g.Average(m => m.Performance.P95Latency.TotalMilliseconds),
                MinMs: g.Min(m => m.Performance.MinLatency.TotalMilliseconds),
                MaxMs: g.Max(m => m.Performance.MaxLatency.TotalMilliseconds),
                Runs: g.Sum(m => m.Performance.RunCount)))
            .OrderBy(m => m.MeanMs);

        foreach (var m in perfByModel)
        {
            sb.AppendLine($"| {m.Name} | {FormatMs(m.MeanMs)} | {FormatMs(m.MedianMs)} | {FormatMs(m.P95Ms)} | {FormatMs(m.MinMs)} | {FormatMs(m.MaxMs)} | {m.Runs} |");
        }
        sb.AppendLine();

        // ── Per-agent detail with test cases ────────────────────────
        foreach (var agentResult in result.AgentResults)
        {
            sb.AppendLine($"## {agentResult.AgentName}");
            sb.AppendLine();
            sb.AppendLine("| Model | Pass Rate | Overall | ToolSel | ToolSucc | ToolEff | TaskComp | Avg Latency |");
            sb.AppendLine("|-------|-----------|---------|---------|----------|---------|----------|-------------|");

            foreach (var m in agentResult.ModelResults.OrderByDescending(m => m.OverallScore))
            {
                var passRate = m.TestCaseCount > 0 ? (double)m.PassedCount / m.TestCaseCount : 0;
                sb.AppendLine($"| {m.ModelName} | {passRate:P0} | {m.OverallScore:F1} | {m.ToolSelectionScore:F1} | {m.ToolSuccessScore:F1} | {m.ToolEfficiencyScore:F1} | {m.TaskCompletionScore:F1} | {FormatMs(m.Performance.MeanLatency.TotalMilliseconds)} |");
            }
            sb.AppendLine();

            // Per-test-case details
            AppendTestCaseDetails(sb, agentResult);
        }

        // ── Cross-model comparison ──────────────────────────────────
        AppendCrossModelComparison(sb, result);

        // ── Profile comparison (when multiple profiles evaluated) ────
        ProfileComparisonRenderer.AppendMarkdown(sb, result);

        // ── Backend comparison (when multiple backends evaluated) ────
        BackendComparisonRenderer.AppendMarkdown(sb, result);

        // ── Recommendations ─────────────────────────────────────────
        sb.AppendLine("## Recommendations");
        sb.AppendLine();

        var allModelScores = result.AgentResults
            .SelectMany(a => a.ModelResults)
            .GroupBy(m => m.ModelName)
            .Select(g => (
                Name: g.Key,
                AvgScore: g.Average(m => m.OverallScore),
                AvgLatencyMs: g.Average(m => m.Performance.MeanLatency.TotalMilliseconds),
                TotalPassed: g.Sum(m => m.PassedCount),
                TotalTests: g.Sum(m => m.TestCaseCount)))
            .ToList();

        var best = allModelScores.OrderByDescending(m => m.AvgScore).FirstOrDefault();
        if (best.Name is not null)
            sb.AppendLine($"- **Best Quality:** {best.Name} — {best.AvgScore:F1} avg score ({best.TotalPassed}/{best.TotalTests} passed)");

        var fastest = allModelScores.OrderBy(m => m.AvgLatencyMs).FirstOrDefault();
        if (fastest.Name is not null)
            sb.AppendLine($"- **Fastest:** {fastest.Name} — {fastest.AvgLatencyMs:F0}ms mean latency");

        return sb.ToString();
    }

    private static void AppendModelParameters(StringBuilder sb, EvalRunResult result)
    {
        var profiles = result.AgentResults
            .SelectMany(a => a.ModelResults)
            .Where(m => m.ParameterProfile is not null)
            .Select(m => (m.ModelName, Profile: m.ParameterProfile!))
            .DistinctBy(x => x.ModelName)
            .ToList();

        if (profiles.Count == 0) return;

        sb.AppendLine("## Model Parameters");
        sb.AppendLine();
        sb.AppendLine("| Model | Profile | Temperature | Top-K | Top-P | Repeat Penalty | Seed |");
        sb.AppendLine("|-------|---------|-------------|-------|-------|----------------|------|");

        foreach (var (modelName, profile) in profiles)
        {
            sb.AppendLine($"| {modelName} | {profile.Name} | {profile.Temperature} | {profile.TopK} | {profile.TopP} | {profile.RepeatPenalty} | {profile.Seed?.ToString() ?? "–"} |");
        }
        sb.AppendLine();
    }

    private static void AppendTestCaseDetails(StringBuilder sb, AgentEvalResult agentResult)
    {
        foreach (var modelResult in agentResult.ModelResults.OrderByDescending(m => m.OverallScore))
        {
            sb.AppendLine($"### {agentResult.AgentName} × {modelResult.ModelName} — Test Cases");
            sb.AppendLine();
            sb.AppendLine("| # | Scenario | Result | Score | Latency | Failure Reason |");
            sb.AppendLine("|---|----------|--------|-------|---------|----------------|");

            var i = 1;
            foreach (var tc in modelResult.TestCaseResults)
            {
                var icon = tc.Passed ? "✅" : "❌";
                var failure = tc.FailureReason is not null
                    ? Truncate(tc.FailureReason, 80)
                    : "–";
                sb.AppendLine($"| {i++} | {tc.TestCaseId} | {icon} | {tc.Score:F0} | {FormatMs(tc.Latency.TotalMilliseconds)} | {failure} |");
            }
            sb.AppendLine();
        }
    }

    private static void AppendCrossModelComparison(StringBuilder sb, EvalRunResult result)
    {
        var allModels = result.AgentResults
            .SelectMany(a => a.ModelResults)
            .Select(m => m.ModelName)
            .Distinct()
            .ToList();

        if (allModels.Count < 2) return;

        sb.AppendLine("## Cross-Model Comparison (Delta from Best)");
        sb.AppendLine();

        // Find best score per agent
        foreach (var agentResult in result.AgentResults)
        {
            var bestScore = agentResult.ModelResults.Max(m => m.OverallScore);
            var bestModel = agentResult.ModelResults.First(m => m.OverallScore == bestScore).ModelName;

            sb.AppendLine($"### {agentResult.AgentName} (baseline: {bestModel} @ {bestScore:F1})");
            sb.AppendLine();
            sb.AppendLine("| Model | Score | Delta | ToolSel Δ | ToolSucc Δ | ToolEff Δ | TaskComp Δ |");
            sb.AppendLine("|-------|-------|-------|-----------|------------|-----------|------------|");

            var bestResult = agentResult.ModelResults.First(m => m.ModelName == bestModel);

            foreach (var m in agentResult.ModelResults.OrderByDescending(m => m.OverallScore))
            {
                var delta = m.OverallScore - bestScore;
                var selDelta = m.ToolSelectionScore - bestResult.ToolSelectionScore;
                var succDelta = m.ToolSuccessScore - bestResult.ToolSuccessScore;
                var effDelta = m.ToolEfficiencyScore - bestResult.ToolEfficiencyScore;
                var compDelta = m.TaskCompletionScore - bestResult.TaskCompletionScore;

                sb.AppendLine($"| {m.ModelName} | {m.OverallScore:F1} | {FormatDelta(delta)} | {FormatDelta(selDelta)} | {FormatDelta(succDelta)} | {FormatDelta(effDelta)} | {FormatDelta(compDelta)} |");
            }
            sb.AppendLine();
        }
    }

    private static object BuildJsonReport(EvalRunResult result, GpuInfo gpuInfo) => new
    {
        runId = result.RunId,
        startedAt = result.StartedAt,
        completedAt = result.CompletedAt,
        durationSeconds = (result.CompletedAt - result.StartedAt).TotalSeconds,
        gpu = new { gpuInfo.GpuLabel, gpuInfo.GpuName, gpuInfo.VramMb, gpuInfo.DriverVersion, gpuInfo.Source },
        agents = result.AgentResults.Select(a => new
        {
            agentName = a.AgentName,
            models = a.ModelResults.Select(m => new
            {
                modelName = m.ModelName,
                overallScore = m.OverallScore,
                toolSelectionScore = m.ToolSelectionScore,
                toolSuccessScore = m.ToolSuccessScore,
                toolEfficiencyScore = m.ToolEfficiencyScore,
                taskCompletionScore = m.TaskCompletionScore,
                testCaseCount = m.TestCaseCount,
                passedCount = m.PassedCount,
                modelParameters = m.ParameterProfile is not null ? new
                {
                    profile = m.ParameterProfile.Name,
                    temperature = m.ParameterProfile.Temperature,
                    topK = m.ParameterProfile.TopK,
                    topP = m.ParameterProfile.TopP,
                    numPredict = m.ParameterProfile.NumPredict,
                    repeatPenalty = m.ParameterProfile.RepeatPenalty,
                    seed = m.ParameterProfile.Seed
                } : null,
                performance = new
                {
                    meanLatencyMs = m.Performance.MeanLatency.TotalMilliseconds,
                    medianLatencyMs = m.Performance.MedianLatency.TotalMilliseconds,
                    p95LatencyMs = m.Performance.P95Latency.TotalMilliseconds,
                    minLatencyMs = m.Performance.MinLatency.TotalMilliseconds,
                    maxLatencyMs = m.Performance.MaxLatency.TotalMilliseconds,
                    runCount = m.Performance.RunCount
                },
                testCases = m.TestCaseResults.Select(tc => new
                {
                    id = tc.TestCaseId,
                    passed = tc.Passed,
                    score = tc.Score,
                    latencyMs = tc.Latency.TotalMilliseconds,
                    failureReason = tc.FailureReason
                })
            })
        })
    };

    private static string FormatMs(double ms) => ms >= 1000 ? $"{ms / 1000:F1}s" : $"{ms:F0}ms";

    private static string FormatDelta(double delta) => delta switch
    {
        0 => "—",
        > 0 => $"+{delta:F1}",
        _ => $"{delta:F1}"
    };

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen] + "…";

    private sealed class TimeSpanJsonConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => TimeSpan.FromMilliseconds(reader.GetDouble());

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value.TotalMilliseconds);
    }
}
