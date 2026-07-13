using System.Text;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using Spectre.Console;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Renders profile comparison data when multiple parameter profiles were evaluated.
/// Shows variance across profiles for each model, highlights best/worst profiles,
/// and computes per-test-case score deltas.
/// </summary>
public static class ProfileComparisonRenderer
{
    /// <summary>
    /// Returns true if the eval result contains multiple profiles (i.e., comparison is meaningful).
    /// </summary>
    public static bool HasMultipleProfiles(EvalRunResult result)
    {
        var profileNames = result.AgentResults
            .SelectMany(a => a.ModelResults)
            .Where(m => m.ParameterProfile is not null)
            .Select(m => m.ParameterProfile!.Name)
            .Distinct()
            .ToList();
        return profileNames.Count > 1;
    }

    /// <summary>
    /// Renders the profile comparison to the terminal using Spectre.Console tables.
    /// </summary>
    public static void RenderTui(EvalRunResult result)
    {
        if (!HasMultipleProfiles(result)) return;

        AnsiConsole.Write(new Rule("[bold magenta]Profile Comparison[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var groups = GroupByModelAndProfile(result);

        foreach (var (modelName, profileResults) in groups)
        {
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(modelName)}[/]");

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn(new TableColumn("[bold]Profile[/]").LeftAligned())
                .AddColumn(new TableColumn("[bold]Overall[/]").RightAligned())
                .AddColumn(new TableColumn("[bold]ToolSel[/]").RightAligned())
                .AddColumn(new TableColumn("[bold]ToolSucc[/]").RightAligned())
                .AddColumn(new TableColumn("[bold]ToolEff[/]").RightAligned())
                .AddColumn(new TableColumn("[bold]TaskComp[/]").RightAligned())
                .AddColumn(new TableColumn("[bold]Pass Rate[/]").RightAligned())
                .AddColumn(new TableColumn("[bold]Latency[/]").RightAligned());

            var bestScore = profileResults
                .Select(profile => profile.AvgOverall)
                .OfType<double>()
                .DefaultIfEmpty()
                .Max();

            foreach (var pr in profileResults.OrderByDescending(p => p.AvgOverall))
            {
                var isBest = pr.AvgOverall.HasValue &&
                    Math.Abs(pr.AvgOverall.Value - bestScore) < 0.01;
                var marker = isBest ? " [green]\u2b50[/]" : "";
                var color = pr.AvgOverall >= bestScore * 0.95 ? "green"
                    : pr.AvgOverall >= bestScore * 0.8 ? "yellow" : "red";

                table.AddRow(
                    $"{Markup.Escape(pr.ProfileName)}{marker}",
                    $"[{color}]{FormatScore(pr.AvgOverall)}[/]",
                    FormatScore(pr.AvgToolSelection),
                    FormatScore(pr.AvgToolSuccess),
                    FormatScore(pr.AvgToolEfficiency),
                    $"{FormatScore(pr.AvgTaskCompletion)}",
                    $"{pr.PassRate:P0}",
                    FormatMs(pr.AvgLatencyMs));
            }

            // Variance row
            if (profileResults.Count > 1)
            {
                var scores = profileResults.Select(p => p.AvgOverall).OfType<double>().ToList();
                if (scores.Count == 0)
                    continue;
                var variance = CalculateVariance(scores);
                var range = scores.Max() - scores.Min();
                table.AddEmptyRow();
                table.AddRow(
                    "[dim]Variance[/]",
                    $"[dim]\u03c3\u00b2={variance:F1} range={range:F1}[/]",
                    "", "", "", "", "", "");
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }
    }

    /// <summary>
    /// Appends the profile comparison section to a markdown report.
    /// </summary>
    public static void AppendMarkdown(StringBuilder sb, EvalRunResult result)
    {
        if (!HasMultipleProfiles(result)) return;

        sb.AppendLine("## Profile Comparison");
        sb.AppendLine();

        var groups = GroupByModelAndProfile(result);

        foreach (var (modelName, profileResults) in groups)
        {
            var availableProfiles = profileResults.Where(profile => profile.AvgOverall.HasValue).ToList();
            if (availableProfiles.Count == 0)
            {
                sb.AppendLine($"### {modelName} (scores unavailable)");
                sb.AppendLine();
                continue;
            }

            var bestScore = availableProfiles.Max(profile => profile.AvgOverall!.Value);
            var bestProfile = availableProfiles.First(
                profile => Math.Abs(profile.AvgOverall!.Value - bestScore) < 0.01);

            sb.AppendLine($"### {modelName} (best: {bestProfile.ProfileName} @ {bestScore:F1})");
            sb.AppendLine();
            sb.AppendLine("| Profile | Overall | ToolSel | ToolSucc | ToolEff | TaskComp | Pass Rate | Latency | \u0394 Best |");
            sb.AppendLine("|---------|---------|---------|----------|---------|----------|-----------|---------|--------|");

            foreach (var pr in profileResults.OrderByDescending(p => p.AvgOverall))
            {
                var delta = pr.AvgOverall.HasValue
                    ? pr.AvgOverall.Value - bestScore
                    : (double?)null;
                var star = delta.HasValue && Math.Abs(delta.Value) < 0.01 ? " \u2b50" : "";
                sb.AppendLine(
                    $"| {pr.ProfileName}{star} | {FormatScore(pr.AvgOverall)} | " +
                    $"{FormatScore(pr.AvgToolSelection)} | {FormatScore(pr.AvgToolSuccess)} | " +
                    $"{FormatScore(pr.AvgToolEfficiency)} | {FormatScore(pr.AvgTaskCompletion)} | " +
                    $"{pr.PassRate:P0} | {FormatMs(pr.AvgLatencyMs)} | " +
                    $"{FormatDelta(delta)} |");
            }

            // Statistics
            if (profileResults.Count > 1)
            {
                var scores = profileResults.Select(p => p.AvgOverall).OfType<double>().ToList();
                if (scores.Count == 0)
                    continue;
                var variance = CalculateVariance(scores);
                var stdDev = Math.Sqrt(variance);
                sb.AppendLine();
                sb.AppendLine($"**Variance:** \u03c3\u00b2={variance:F2}, \u03c3={stdDev:F2}, range={scores.Max() - scores.Min():F1}");
            }
            sb.AppendLine();

            // Per-test-case profile comparison
            AppendPerTestCaseComparison(sb, modelName, profileResults, result);
        }
    }

    private static void AppendPerTestCaseComparison(
        StringBuilder sb,
        string modelName,
        List<ProfileAggregation> profileResults,
        EvalRunResult result)
    {
        // Collect all test case IDs across all profiles for this model
        var allTestCases = result.AgentResults
            .SelectMany(a => a.ModelResults)
            .Where(m => m.ModelName == modelName)
            .SelectMany(m => m.TestCaseResults)
            .Select(tc => tc.TestCaseId)
            .Distinct()
            .ToList();

        if (allTestCases.Count == 0) return;

        sb.AppendLine($"<details><summary>Per-test-case scores across profiles ({allTestCases.Count} tests)</summary>");
        sb.AppendLine();
        sb.Append("| Test Case |");
        foreach (var pr in profileResults) sb.Append($" {pr.ProfileName} |");
        sb.AppendLine(" \u0394 |");
        sb.Append("|-----------|");
        foreach (var _ in profileResults) sb.Append("--------|");
        sb.AppendLine("------|");

        foreach (var tcId in allTestCases)
        {
            sb.Append($"| {tcId} |");
            var scores = new List<double>();
            foreach (var pr in profileResults)
            {
                var tcResult = result.AgentResults
                    .SelectMany(a => a.ModelResults)
                    .Where(m => m.ModelName == modelName && m.ParameterProfile?.Name == pr.ProfileName)
                    .SelectMany(m => m.TestCaseResults)
                    .FirstOrDefault(tc => tc.TestCaseId == tcId);

                if (tcResult?.Score is { } score)
                {
                    var icon = tcResult.Passed ? "\u2705" : "\u274c";
                    sb.Append($" {icon} {tcResult.Score:F0} |");
                    scores.Add(score);
                }
                else
                {
                    sb.Append(" \u2013 |");
                }
            }

            var range = scores.Count > 1 ? scores.Max() - scores.Min() : 0;
            sb.AppendLine($" {range:F0} |");
        }

        sb.AppendLine();
        sb.AppendLine("</details>");
        sb.AppendLine();
    }

    // ─── Helpers ─────────────────────────────────────────────────────

    private static IReadOnlyList<(string ModelName, List<ProfileAggregation> Profiles)> GroupByModelAndProfile(
        EvalRunResult result)
    {
        return result.AgentResults
            .SelectMany(a => a.ModelResults)
            .Where(m => m.ParameterProfile is not null)
            .GroupBy(m => m.ModelName)
            .Select(modelGroup => (
                ModelName: modelGroup.Key,
                Profiles: modelGroup
                    .GroupBy(m => m.ParameterProfile!.Name)
                    .Select(profileGroup => new ProfileAggregation
                    {
                        ProfileName = profileGroup.Key,
                        Profile = profileGroup.First().ParameterProfile!,
                        AvgOverall = Average(profileGroup.Select(m => m.OverallScore)),
                        AvgToolSelection = Average(profileGroup.Select(m => m.ToolSelectionScore)),
                        AvgToolSuccess = Average(profileGroup.Select(m => m.ToolSuccessScore)),
                        AvgToolEfficiency = Average(profileGroup.Select(m => m.ToolEfficiencyScore)),
                        AvgTaskCompletion = Average(profileGroup.Select(m => m.TaskCompletionScore)),
                        TotalPassed = profileGroup.Sum(m => m.PassedCount),
                        TotalTests = profileGroup.Sum(m => m.TestCaseCount),
                        AvgLatencyMs = profileGroup.Average(m => m.Performance.MeanLatency.TotalMilliseconds)
                    })
                    .ToList()))
            .ToList();
    }

    private static double CalculateVariance(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0;
        var mean = values.Average();
        return values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
    }

    private static double? Average(IEnumerable<double?> scores)
    {
        var available = scores.OfType<double>().ToList();
        return available.Count > 0 ? available.Average() : null;
    }

    private static string FormatMs(double ms) => ms >= 1000 ? $"{ms / 1000:F1}s" : $"{ms:F0}ms";

    private static string FormatDelta(double? delta) => delta switch
    {
        null => "N/A",
        >= -0.01 and <= 0.01 => "\u2014",
        > 0 => $"+{delta:F1}",
        _ => $"{delta:F1}"
    };

    private static string FormatScore(double? score) =>
        score.HasValue ? score.Value.ToString("F1") : "N/A";
}
