using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Infrastructure;
using Spectre.Console;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Renders rich terminal reports for evaluation results using Spectre.Console tables.
/// </summary>
public static class ReportRenderer
{
    /// <summary>
    /// Renders the full evaluation report to the terminal.
    /// </summary>
    public static void Render(EvalRunResult result, GpuInfo gpuInfo)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold cornflowerblue]Evaluation Results[/]").LeftJustified());
        AnsiConsole.WriteLine();

        RenderEnvironmentHeader(result, gpuInfo);
        RenderQualityMatrix(result);
        RenderPerformanceMatrix(result);
        RenderDetailedAgentReports(result);
        ProfileComparisonRenderer.RenderTui(result);
        BackendComparisonRenderer.RenderTui(result);
        RenderWinnerRecommendation(result);
    }

    private static void RenderEnvironmentHeader(EvalRunResult result, GpuInfo gpuInfo)
    {
        var duration = result.CompletedAt - result.StartedAt;
        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold]Run Environment[/]")
            .AddColumn("Property")
            .AddColumn("Value");

        table.AddRow("Run ID", result.RunId);
        table.AddRow("Duration", $"{duration.TotalSeconds:F1}s");
        table.AddRow("GPU", gpuInfo.GpuLabel);
        table.AddRow("Timestamp", result.StartedAt.ToString("yyyy-MM-dd HH:mm:ss UTC"));

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static void RenderQualityMatrix(EvalRunResult result)
    {
        AnsiConsole.Write(new Rule("[bold]Quality Scores (0\u2013100)[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var allModels = result.AgentResults
            .SelectMany(a => a.ModelResults)
            .Select(m => m.ModelName)
            .Distinct()
            .ToList();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[bold]Agent[/]").LeftAligned());

        foreach (var model in allModels)
        {
            table.AddColumn(new TableColumn($"[bold]{Markup.Escape(model)}[/]").Centered());
        }

        foreach (var agentResult in result.AgentResults)
        {
            var row = new List<string> { $"[bold]{Markup.Escape(agentResult.AgentName)}[/]" };

            foreach (var model in allModels)
            {
                var modelResult = agentResult.ModelResults.FirstOrDefault(m => m.ModelName == model);
                if (modelResult is null)
                {
                    row.Add("[dim]N/A[/]");
                    continue;
                }

                var score = modelResult.OverallScore;
                var color = score switch
                {
                    >= 80 => "green",
                    >= 60 => "yellow",
                    _ => "red"
                };
                row.Add($"[{color}]{score:F1}[/]");
            }

            table.AddRow(row.ToArray());
        }

        // Sub-metric breakdown
        table.AddEmptyRow();
        foreach (var metric in new[] { ("Tool Selection", "ToolSelectionScore"), ("Tool Success", "ToolSuccessScore"), ("Tool Efficiency", "ToolEfficiencyScore"), ("Task Completion", "TaskCompletionScore") })
        {
            var row = new List<string> { $"[dim]  {metric.Item1}[/]" };
            foreach (var model in allModels)
            {
                var avgScore = result.AgentResults
                    .SelectMany(a => a.ModelResults)
                    .Where(m => m.ModelName == model)
                    .Select(m => metric.Item2 switch
                    {
                        "ToolSelectionScore" => m.ToolSelectionScore,
                        "ToolSuccessScore" => m.ToolSuccessScore,
                        "ToolEfficiencyScore" => m.ToolEfficiencyScore,
                        "TaskCompletionScore" => m.TaskCompletionScore,
                        _ => 0
                    })
                    .DefaultIfEmpty(0)
                    .Average();

                var color = avgScore switch
                {
                    >= 80 => "green",
                    >= 60 => "yellow",
                    _ => "red"
                };
                row.Add($"[dim {color}]{avgScore:F1}[/]");
            }
            table.AddRow(row.ToArray());
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static void RenderPerformanceMatrix(EvalRunResult result)
    {
        AnsiConsole.Write(new Rule("[bold]Performance (Latency)[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[bold]Model[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Mean[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Median[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]P95[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Min[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Max[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Runs[/]").RightAligned());

        var allModels = result.AgentResults
            .SelectMany(a => a.ModelResults)
            .GroupBy(m => m.ModelName)
            .Select(g =>
            {
                var perfs = g.Select(m => m.Performance).ToList();
                return new
                {
                    ModelName = g.Key,
                    MeanMs = perfs.Average(p => p.MeanLatency.TotalMilliseconds),
                    MedianMs = perfs.Average(p => p.MedianLatency.TotalMilliseconds),
                    P95Ms = perfs.Average(p => p.P95Latency.TotalMilliseconds),
                    MinMs = perfs.Min(p => p.MinLatency.TotalMilliseconds),
                    MaxMs = perfs.Max(p => p.MaxLatency.TotalMilliseconds),
                    Runs = perfs.Sum(p => p.RunCount)
                };
            })
            .OrderBy(m => m.MeanMs)
            .ToList();

        foreach (var model in allModels)
        {
            table.AddRow(
                Markup.Escape(model.ModelName),
                FormatMs(model.MeanMs),
                FormatMs(model.MedianMs),
                FormatMs(model.P95Ms),
                FormatMs(model.MinMs),
                FormatMs(model.MaxMs),
                model.Runs.ToString());
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static void RenderDetailedAgentReports(EvalRunResult result)
    {
        foreach (var agentResult in result.AgentResults)
        {
            AnsiConsole.Write(new Rule($"[bold]{Markup.Escape(agentResult.AgentName)}[/]").LeftJustified());
            AnsiConsole.WriteLine();

            var table = new Table()
                .Border(TableBorder.Simple)
                .AddColumn("Model")
                .AddColumn("Pass Rate")
                .AddColumn("Overall")
                .AddColumn("ToolSel")
                .AddColumn("ToolSucc")
                .AddColumn("ToolEff")
                .AddColumn("TaskComp")
                .AddColumn("Avg Latency");

            foreach (var m in agentResult.ModelResults.OrderByDescending(m => m.OverallScore))
            {
                var passRate = m.TestCaseCount > 0
                    ? (double)m.PassedCount / m.TestCaseCount
                    : 0;

                var passColor = passRate switch
                {
                    >= 0.8 => "green",
                    >= 0.5 => "yellow",
                    _ => "red"
                };

                table.AddRow(
                    Markup.Escape(m.ModelName),
                    $"[{passColor}]{passRate:P0}[/]",
                    ScoreCell(m.OverallScore),
                    ScoreCell(m.ToolSelectionScore),
                    ScoreCell(m.ToolSuccessScore),
                    ScoreCell(m.ToolEfficiencyScore),
                    ScoreCell(m.TaskCompletionScore),
                    FormatMs(m.Performance.MeanLatency.TotalMilliseconds));
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }
    }

    private static void RenderWinnerRecommendation(EvalRunResult result)
    {
        AnsiConsole.Write(new Rule("[bold green]Recommendations[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var allModelScores = result.AgentResults
            .SelectMany(a => a.ModelResults)
            .GroupBy(m => m.ModelName)
            .Select(g => new
            {
                ModelName = g.Key,
                AvgScore = g.Average(m => m.OverallScore),
                AvgLatencyMs = g.Average(m => m.Performance.MeanLatency.TotalMilliseconds),
                TotalPassed = g.Sum(m => m.PassedCount),
                TotalTests = g.Sum(m => m.TestCaseCount)
            })
            .ToList();

        // Best quality
        var bestQuality = allModelScores.OrderByDescending(m => m.AvgScore).FirstOrDefault();
        if (bestQuality is not null)
        {
            AnsiConsole.MarkupLine($"  [green]\U0001f3c6 Best Quality:[/] [bold]{Markup.Escape(bestQuality.ModelName)}[/] \u2014 {bestQuality.AvgScore:F1} avg score ({bestQuality.TotalPassed}/{bestQuality.TotalTests} passed)");
        }

        // Fastest
        var fastest = allModelScores.OrderBy(m => m.AvgLatencyMs).FirstOrDefault();
        if (fastest is not null)
        {
            AnsiConsole.MarkupLine($"  [blue]\u26a1 Fastest:[/] [bold]{Markup.Escape(fastest.ModelName)}[/] \u2014 {fastest.AvgLatencyMs:F0}ms mean latency");
        }

        // Best value (quality / latency ratio)
        var bestValue = allModelScores
            .Where(m => m.AvgLatencyMs > 0)
            .OrderByDescending(m => m.AvgScore / m.AvgLatencyMs * 1000)
            .FirstOrDefault();
        if (bestValue is not null && bestValue.ModelName != bestQuality?.ModelName)
        {
            AnsiConsole.MarkupLine($"  [yellow]\U0001f4b0 Best Value:[/] [bold]{Markup.Escape(bestValue.ModelName)}[/] \u2014 {bestValue.AvgScore:F1} score at {bestValue.AvgLatencyMs:F0}ms");
        }

        AnsiConsole.WriteLine();
    }

    private static string ScoreCell(double score)
    {
        var color = score switch
        {
            >= 80 => "green",
            >= 60 => "yellow",
            _ => "red"
        };
        return $"[{color}]{score:F1}[/]";
    }

    private static string FormatMs(double ms)
    {
        return ms switch
        {
            >= 1000 => $"{ms / 1000:F1}s",
            _ => $"{ms:F0}ms"
        };
    }
}
