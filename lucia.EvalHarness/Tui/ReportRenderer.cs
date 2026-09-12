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

                row.Add(ScoreCell(modelResult.OverallScore));
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
                    .Average();

                row.Add(avgScore.HasValue
                    ? $"[dim]{ScoreCell(avgScore)}[/]"
                    : "[dim]N/A[/]");
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
                var perfs = g
                    .Select(m => m.Performance)
                    .Where(performance => performance.RunCount > 0)
                    .ToList();
                return new
                {
                    ModelName = g.Key,
                    MeanMs = perfs.Count > 0 ? (double?)perfs.Average(p => p.MeanLatency.TotalMilliseconds) : null,
                    MedianMs = perfs.Count > 0 ? (double?)perfs.Average(p => p.MedianLatency.TotalMilliseconds) : null,
                    P95Ms = perfs.Count > 0 ? (double?)perfs.Average(p => p.P95Latency.TotalMilliseconds) : null,
                    MinMs = perfs.Count > 0 ? (double?)perfs.Min(p => p.MinLatency.TotalMilliseconds) : null,
                    MaxMs = perfs.Count > 0 ? (double?)perfs.Max(p => p.MaxLatency.TotalMilliseconds) : null,
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
                .AddColumn("Avg Latency")
                .AddColumn("Tokens in/out/cache")
                .AddColumn("Est. USD");

            foreach (var m in agentResult.ModelResults.OrderByDescending(m => m.OverallScore))
            {
                var passRate = m.ScoredTestCaseCount > 0
                    ? (double?)m.PassedCount / m.ScoredTestCaseCount
                    : null;
                var passRateCell = passRate.HasValue
                    ? $"[{passRate.Value switch
                    {
                        >= 0.8 => "green",
                        >= 0.5 => "yellow",
                        _ => "red"
                    }}]{passRate.Value * 100:F0}%[/]"
                    : "[dim]N/A[/]";

                table.AddRow(
                    Markup.Escape(m.ModelName),
                    passRateCell,
                    ScoreCell(m.OverallScore, m.OverallScoreStatus),
                    ScoreCell(m.ToolSelectionScore, m.OverallScoreStatus),
                    ScoreCell(m.ToolSuccessScore, m.OverallScoreStatus),
                    ScoreCell(m.ToolEfficiencyScore, m.OverallScoreStatus),
                    ScoreCell(m.TaskCompletionScore, m.TaskCompletionStatus),
                    FormatMs(m.Performance.RunCount > 0
                        ? m.Performance.MeanLatency.TotalMilliseconds
                        : null),
                    Reports.CostReportFormatting.Tokens(m.Cost),
                    Reports.CostReportFormatting.Cost(m.Cost));
            }

            AnsiConsole.Write(table);
            var tests = new Table().Border(TableBorder.Simple)
                .AddColumn("Model / test")
                .AddColumn("Tokens in/out/cache")
                .AddColumn("Est. USD");
            foreach (var model in agentResult.ModelResults)
            {
                foreach (var test in model.TestCaseResults)
                {
                    tests.AddRow(Markup.Escape($"{model.ModelName} / {test.TestCaseId}"),
                        Reports.CostReportFormatting.Tokens(test.Cost),
                        Reports.CostReportFormatting.Cost(test.Cost));
                }
            }
            AnsiConsole.Write(tests);
            AnsiConsole.WriteLine();
        }
    }

    private static void RenderWinnerRecommendation(EvalRunResult result)
    {
        AnsiConsole.Write(new Rule("[bold green]Recommendations[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var allModelScores = ModelRecommendation.Rank(result);

        // Best quality
        var bestQuality = allModelScores.FirstOrDefault();
        if (bestQuality is not null)
        {
            AnsiConsole.MarkupLine($"  [green]Best Quality:[/] [bold]{Markup.Escape(bestQuality.ModelName)}[/], {bestQuality.AverageScore:F1} avg score ({bestQuality.PassedCount}/{bestQuality.ScoredTestCount} passed), {Reports.CostReportFormatting.Usd(bestQuality.MeanTestCostUsd)} estimated per test");
        }

        // Fastest
        var fastest = allModelScores
            .Where(m => m.MeanLatencyMs.HasValue)
            .OrderBy(m => m.MeanLatencyMs)
            .FirstOrDefault();
        if (fastest is not null)
        {
            AnsiConsole.MarkupLine($"  [blue]\u26a1 Fastest:[/] [bold]{Markup.Escape(fastest.ModelName)}[/] \u2014 {fastest.MeanLatencyMs:F0}ms mean latency");
        }

        AnsiConsole.MarkupLine($"[dim]{Reports.CostReportFormatting.EstimateNote}[/]");

        AnsiConsole.WriteLine();
    }

    private static string ScoreCell(double? score, string? status = null)
    {
        if (!score.HasValue)
            return "[dim]N/A[/]";

        var color = score.Value switch
        {
            >= 80 => "green",
            >= 60 => "yellow",
            _ => "red"
        };
        var statusSuffix = status is null ? string.Empty : $" ({Markup.Escape(status)})";
        return $"[{color}]{score.Value:F1}{statusSuffix}[/]";
    }

    private static string FormatMs(double? ms)
    {
        return ms switch
        {
            null => "N/A",
            >= 1000 => $"{ms / 1000:F1}s",
            _ => $"{ms:F0}ms"
        };
    }
}
