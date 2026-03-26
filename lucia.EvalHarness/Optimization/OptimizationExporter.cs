using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace lucia.EvalHarness.Optimization;

/// <summary>
/// Exports prompt optimization results to markdown and JSON files.
/// </summary>
public static class OptimizationExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Exports optimization results to the report directory.
    /// Returns the list of written file paths.
    /// </summary>
    public static IReadOnlyList<string> Export(
        IReadOnlyList<PromptOptimizationResult> results,
        string reportDir,
        DateTimeOffset timestamp)
    {
        Directory.CreateDirectory(reportDir);
        var files = new List<string>();

        var ts = timestamp.ToString("yyyyMMdd_HHmmss");
        var mdPath = Path.Combine(reportDir, $"prompt-optimization-{ts}.md");
        var jsonPath = Path.Combine(reportDir, $"prompt-optimization-{ts}.json");

        File.WriteAllText(mdPath, BuildMarkdown(results));
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(BuildJson(results), JsonOptions));

        files.Add(mdPath);
        files.Add(jsonPath);

        return files;
    }

    private static string BuildMarkdown(IReadOnlyList<PromptOptimizationResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# lucia Prompt Optimization Report");
        sb.AppendLine();

        foreach (var result in results)
        {
            sb.AppendLine($"## {result.AgentName} × {result.TargetModel}");
            sb.AppendLine();
            sb.AppendLine($"- **Current Score:** {result.CurrentScore:F1}");
            sb.AppendLine($"- **Baseline Target:** {result.BaselineScore:F1}");
            sb.AppendLine($"- **Gap:** {result.BaselineScore - result.CurrentScore:F1}");
            sb.AppendLine();

            if (result.Analysis is not null)
            {
                sb.AppendLine("### Analysis");
                sb.AppendLine();
                sb.AppendLine(result.Analysis);
                sb.AppendLine();
            }

            if (result.Suggestions.Count > 0)
            {
                sb.AppendLine("### Suggestions");
                sb.AppendLine();
                sb.AppendLine("| # | Type | Location | Predicted Impact |");
                sb.AppendLine("|---|------|----------|------------------|");

                for (var i = 0; i < result.Suggestions.Count; i++)
                {
                    var s = result.Suggestions[i];
                    sb.AppendLine($"| {i + 1} | {s.Type} | {s.Location} | {s.PredictedImpact ?? "–"} |");
                }
                sb.AppendLine();

                foreach (var (s, i) in result.Suggestions.Select((s, i) => (s, i)))
                {
                    sb.AppendLine($"#### Suggestion {i + 1}: {s.Type}");
                    sb.AppendLine();
                    sb.AppendLine($"**Reasoning:** {s.Reasoning}");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(s.Content);
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
            }

            if (result.SuggestedPrompt is not null)
            {
                sb.AppendLine("### Revised System Prompt");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(result.SuggestedPrompt);
                sb.AppendLine("```");
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static object BuildJson(IReadOnlyList<PromptOptimizationResult> results) =>
        results.Select(r => new
        {
            agentName = r.AgentName,
            targetModel = r.TargetModel,
            currentScore = r.CurrentScore,
            baselineScore = r.BaselineScore,
            analysis = r.Analysis,
            suggestions = r.Suggestions.Select(s => new
            {
                type = s.Type,
                location = s.Location,
                content = s.Content,
                reasoning = s.Reasoning,
                predictedImpact = s.PredictedImpact
            }),
            suggestedPrompt = r.SuggestedPrompt,
            originalPrompt = r.OriginalPrompt
        }).ToList();
}
