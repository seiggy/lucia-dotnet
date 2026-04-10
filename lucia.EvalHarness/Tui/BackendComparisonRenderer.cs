using System.Text;
using lucia.EvalHarness.Evaluation;
using Spectre.Console;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Renders side-by-side backend latency and quality comparisons when the eval run
/// contains results from multiple inference backends (e.g., Ollama vs llama.cpp).
/// </summary>
public static class BackendComparisonRenderer
{
    /// <summary>
    /// Returns true if the eval result contains results from more than one backend.
    /// Backend is identified by the <c>@BackendName</c> suffix on model names.
    /// </summary>
    public static bool HasMultipleBackends(EvalRunResult result)
    {
        var backends = result.AgentResults
            .SelectMany(a => a.ModelResults)
            .Select(m => ExtractBackendName(m.ModelName))
            .Where(b => b is not null)
            .Distinct()
            .ToList();

        return backends.Count > 1;
    }

    /// <summary>
    /// Renders the backend comparison to the terminal using Spectre.Console tables.
    /// </summary>
    public static void RenderTui(EvalRunResult result)
    {
        if (!HasMultipleBackends(result)) return;

        AnsiConsole.Write(new Rule("[bold cyan]Backend Comparison[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var groups = GroupByBaseModelAndBackend(result);

        foreach (var (baseModel, backendResults) in groups)
        {
            if (backendResults.Count < 2) continue;

            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title($"[bold]Backend Latency Comparison: {Markup.Escape(baseModel)}[/]");

            table.AddColumn(new TableColumn("[bold]Metric[/]").LeftAligned());

            foreach (var br in backendResults)
            {
                table.AddColumn(new TableColumn($"[bold]{Markup.Escape(br.BackendName)}[/]").RightAligned());
            }

            table.AddColumn(new TableColumn("[bold]Δ (faster)[/]").RightAligned());

            AddLatencyRow(table, "Mean", backendResults, br => br.Performance.MeanLatency);
            AddLatencyRow(table, "Median", backendResults, br => br.Performance.MedianLatency);
            AddLatencyRow(table, "P95", backendResults, br => br.Performance.P95Latency);
            AddLatencyRow(table, "Min", backendResults, br => br.Performance.MinLatency);
            AddLatencyRow(table, "Max", backendResults, br => br.Performance.MaxLatency);

            // Quality row
            table.AddEmptyRow();
            var qualityRow = new List<string> { "[dim]Overall Score[/]" };
            var scores = backendResults.Select(br => br.AvgOverall).ToList();
            var bestScore = scores.Max();
            foreach (var br in backendResults)
            {
                var color = Math.Abs(br.AvgOverall - bestScore) < 0.01 ? "green" : "dim";
                qualityRow.Add($"[{color}]{br.AvgOverall:F1}[/]");
            }
            qualityRow.Add(FormatScoreDelta(backendResults));
            table.AddRow(qualityRow.ToArray());

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }
    }

    /// <summary>
    /// Appends the backend comparison section to a markdown report.
    /// </summary>
    public static void AppendMarkdown(StringBuilder sb, EvalRunResult result)
    {
        if (!HasMultipleBackends(result)) return;

        sb.AppendLine("## Backend Comparison");
        sb.AppendLine();

        var groups = GroupByBaseModelAndBackend(result);

        foreach (var (baseModel, backendResults) in groups)
        {
            if (backendResults.Count < 2) continue;

            sb.AppendLine($"### {baseModel}");
            sb.AppendLine();
            sb.Append("| Metric |");
            foreach (var br in backendResults) sb.Append($" {br.BackendName} |");
            sb.AppendLine(" Δ (faster) |");
            sb.Append("|--------|");
            foreach (var _ in backendResults) sb.Append("--------|");
            sb.AppendLine("------------|");

            AppendMarkdownLatencyRow(sb, "Mean", backendResults, br => br.Performance.MeanLatency);
            AppendMarkdownLatencyRow(sb, "Median", backendResults, br => br.Performance.MedianLatency);
            AppendMarkdownLatencyRow(sb, "P95", backendResults, br => br.Performance.P95Latency);
            AppendMarkdownLatencyRow(sb, "Min", backendResults, br => br.Performance.MinLatency);
            AppendMarkdownLatencyRow(sb, "Max", backendResults, br => br.Performance.MaxLatency);

            // Quality row
            sb.Append("| **Overall Score** |");
            foreach (var br in backendResults) sb.Append($" {br.AvgOverall:F1} |");
            sb.AppendLine($" {FormatScoreDeltaPlain(backendResults)} |");

            sb.AppendLine();
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────

    private static void AddLatencyRow(
        Table table,
        string metricName,
        List<BackendAggregation> backendResults,
        Func<BackendAggregation, TimeSpan> selector)
    {
        var row = new List<string> { metricName };
        var values = backendResults.Select(selector).ToList();
        var fastest = values.Min();

        for (var i = 0; i < backendResults.Count; i++)
        {
            var ms = values[i].TotalMilliseconds;
            var color = values[i] == fastest ? "green" : "dim";
            row.Add($"[{color}]{FormatMs(ms)}[/]");
        }

        row.Add(FormatLatencyDelta(backendResults, values));
        table.AddRow(row.ToArray());
    }

    private static void AppendMarkdownLatencyRow(
        StringBuilder sb,
        string metricName,
        List<BackendAggregation> backendResults,
        Func<BackendAggregation, TimeSpan> selector)
    {
        sb.Append($"| {metricName} |");
        var values = backendResults.Select(selector).ToList();
        foreach (var v in values)
        {
            sb.Append($" {FormatMs(v.TotalMilliseconds)} |");
        }

        sb.AppendLine($" {FormatLatencyDeltaPlain(backendResults, values)} |");
    }

    private static string FormatLatencyDelta(
        List<BackendAggregation> backends,
        List<TimeSpan> values)
    {
        if (backends.Count != 2) return "";

        var (faster, pct) = ComputeDelta(backends, values);
        if (faster is null) return "[dim]—[/]";
        return $"[green]{Markup.Escape(faster)} {pct:F0}%[/]";
    }

    private static string FormatLatencyDeltaPlain(
        List<BackendAggregation> backends,
        List<TimeSpan> values)
    {
        if (backends.Count != 2) return "";

        var (faster, pct) = ComputeDelta(backends, values);
        if (faster is null) return "—";
        return $"{faster} {pct:F0}%";
    }

    private static (string? FasterName, double Pct) ComputeDelta(
        List<BackendAggregation> backends,
        List<TimeSpan> values)
    {
        if (values[0] == TimeSpan.Zero || values[1] == TimeSpan.Zero)
            return (null, 0);

        var ms0 = values[0].TotalMilliseconds;
        var ms1 = values[1].TotalMilliseconds;

        if (Math.Abs(ms0 - ms1) < 1) return (null, 0);

        if (ms0 < ms1)
        {
            var pct = -(1 - ms0 / ms1) * 100;
            return (backends[0].BackendName, pct);
        }
        else
        {
            var pct = -(1 - ms1 / ms0) * 100;
            return (backends[1].BackendName, pct);
        }
    }

    private static string FormatScoreDelta(List<BackendAggregation> backends)
    {
        if (backends.Count != 2) return "";
        var delta = backends[0].AvgOverall - backends[1].AvgOverall;
        if (Math.Abs(delta) < 0.1) return "[dim]—[/]";
        var winner = delta > 0 ? backends[0].BackendName : backends[1].BackendName;
        return $"[green]{Markup.Escape(winner)} +{Math.Abs(delta):F1}[/]";
    }

    private static string FormatScoreDeltaPlain(List<BackendAggregation> backends)
    {
        if (backends.Count != 2) return "";
        var delta = backends[0].AvgOverall - backends[1].AvgOverall;
        if (Math.Abs(delta) < 0.1) return "—";
        var winner = delta > 0 ? backends[0].BackendName : backends[1].BackendName;
        return $"{winner} +{Math.Abs(delta):F1}";
    }

    /// <summary>
    /// Extracts the backend suffix from a tagged model name (e.g., "gemma4:e2b@Ollama" → "Ollama").
    /// Returns null if there is no <c>@</c> tag.
    /// </summary>
    internal static string? ExtractBackendName(string modelName)
    {
        var idx = modelName.LastIndexOf('@');
        return idx >= 0 ? modelName[(idx + 1)..] : null;
    }

    /// <summary>
    /// Extracts the base model name without backend suffix (e.g., "gemma4:e2b@Ollama" → "gemma4:e2b").
    /// </summary>
    internal static string ExtractBaseModel(string modelName)
    {
        var idx = modelName.LastIndexOf('@');
        return idx >= 0 ? modelName[..idx] : modelName;
    }

    private static IReadOnlyList<(string BaseModel, List<BackendAggregation> Backends)> GroupByBaseModelAndBackend(
        EvalRunResult result)
    {
        return result.AgentResults
            .SelectMany(a => a.ModelResults)
            .Where(m => ExtractBackendName(m.ModelName) is not null)
            .GroupBy(m => ExtractBaseModel(m.ModelName))
            .Select(modelGroup => (
                BaseModel: modelGroup.Key,
                Backends: modelGroup
                    .GroupBy(m => ExtractBackendName(m.ModelName)!)
                    .Select(backendGroup =>
                    {
                        var results = backendGroup.ToList();
                        var allPerf = results.Select(r => r.Performance).ToList();
                        return new BackendAggregation
                        {
                            BackendName = backendGroup.Key,
                            AvgOverall = results.Average(r => r.OverallScore),
                            TotalPassed = results.Sum(r => r.PassedCount),
                            TotalTests = results.Sum(r => r.TestCaseCount),
                            Performance = new ModelPerformanceSummary
                            {
                                ModelName = modelGroup.Key,
                                RunCount = allPerf.Sum(p => p.RunCount),
                                MeanLatency = TimeSpan.FromMilliseconds(allPerf.Average(p => p.MeanLatency.TotalMilliseconds)),
                                MedianLatency = TimeSpan.FromMilliseconds(allPerf.Average(p => p.MedianLatency.TotalMilliseconds)),
                                P95Latency = TimeSpan.FromMilliseconds(allPerf.Average(p => p.P95Latency.TotalMilliseconds)),
                                MinLatency = TimeSpan.FromMilliseconds(allPerf.Min(p => p.MinLatency.TotalMilliseconds)),
                                MaxLatency = TimeSpan.FromMilliseconds(allPerf.Max(p => p.MaxLatency.TotalMilliseconds))
                            }
                        };
                    })
                    .ToList()))
            .Where(g => g.Backends.Count > 1)
            .ToList();
    }

    private static string FormatMs(double ms) => ms >= 1000 ? $"{ms / 1000:F1}s" : $"{ms:F0}ms";
}
