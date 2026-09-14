using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using lucia.EvalHarness.Personality;
using Spectre.Console;

namespace lucia.EvalHarness.Reports;

/// <summary>Cost reporting for personality evaluations, keeping their native 1-5 quality scale.</summary>
public static class PersonalityCostReport
{
    public static IReadOnlyList<PersonalityEvalReport> Rank(IReadOnlyList<PersonalityEvalReport> reports) =>
        reports.Where(report => report.AverageCombinedScore.HasValue)
            .OrderByDescending(report => report.AverageCombinedScore)
            .ThenBy(report => report.MeanTestCostUsd ?? decimal.MaxValue)
            .ThenBy(report => report.ModelName, StringComparer.Ordinal)
            .ToList();

    public static void Render(IReadOnlyList<PersonalityEvalReport> reports)
    {
        AnsiConsole.Write(new Rule("Personality token usage and estimated provider cost").LeftJustified());
        AnsiConsole.MarkupLine(Markup.Escape(CostReportFormatting.EstimateNote));
        var table = new Table().Border(TableBorder.Simple)
            .AddColumn("Model / scenario / profile")
            .AddColumn("Score / 5")
            .AddColumn("Tokens in/out/cache")
            .AddColumn("Est. USD");
        foreach (var report in reports)
        {
            table.AddRow(Markup.Escape(report.ModelName), Score(report.AverageCombinedScore),
                CostReportFormatting.Tokens(report.Cost), CostReportFormatting.Cost(report.Cost));
            foreach (var result in report.Results)
            {
                table.AddRow(Markup.Escape($"  {result.ScenarioId} / {result.ProfileName}"),
                    Score(result.Score), CostReportFormatting.Tokens(result.Cost),
                    CostReportFormatting.Cost(result.Cost));
            }
        }
        AnsiConsole.Write(table);
        var best = Rank(reports).FirstOrDefault();
        if (best is not null)
        {
            AnsiConsole.MarkupLine($"Best quality: [bold]{Markup.Escape(best.ModelName)}[/], {Score(best.AverageCombinedScore)}/5, " +
                $"{CostReportFormatting.Usd(best.MeanTestCostUsd)} estimated per test. Cost breaks quality ties.");
        }
    }

    public static IReadOnlyList<string> Export(IReadOnlyList<PersonalityEvalReport> reports, string reportDirectory)
    {
        Directory.CreateDirectory(reportDirectory);
        var timestamp = reports.FirstOrDefault()?.StartedAt ?? DateTimeOffset.UtcNow;
        var prefix = Path.Combine(reportDirectory, $"personality-{timestamp:yyyyMMdd_HHmmss}");
        var best = Rank(reports).FirstOrDefault();
        var recommendation = best is null
            ? "No recommendation: quality scores are unavailable."
            : $"Best quality: {best.ModelName}, {Score(best.AverageCombinedScore)}/5, " +
                $"{CostReportFormatting.Usd(best.MeanTestCostUsd)} estimated per test. Cost breaks quality ties.";
        var markdown = new StringBuilder("# Personality evaluation\n\n")
            .AppendLine(CostReportFormatting.EstimateNote).AppendLine()
            .AppendLine(recommendation).AppendLine();
        var html = new StringBuilder("""
            <!DOCTYPE html><html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>lucia Personality Eval Report</title>
            <style>
            :root{color-scheme:dark;--c-bg:#0a0a0b;--c-surface:#141416;--c-text:#ececef;--c-border:#3f3f46}
            body{background:var(--c-bg);color:var(--c-text);font-family:system-ui,sans-serif;margin:2rem}
            main{max-width:90rem;margin:auto}p{max-width:75ch;line-height:1.5}
            .table-scroll{overflow-x:auto}table{border-collapse:collapse;width:100%;background:var(--c-surface)}
            th,td{border-bottom:1px solid var(--c-border);padding:.75rem;text-align:left}
            th{white-space:nowrap}td{font-variant-numeric:tabular-nums}
            @media(max-width:40rem){body{margin:1rem}}
            </style></head><body><main><h1>Personality evaluation</h1>
            """)
            .Append("<p>").Append(WebUtility.HtmlEncode(CostReportFormatting.EstimateNote)).Append("</p><p>")
            .Append(WebUtility.HtmlEncode(recommendation)).AppendLine("</p>");

        foreach (var report in reports)
        {
            var summary = $"{report.ModelName}: {Score(report.AverageCombinedScore)}/5, " +
                $"{CostReportFormatting.Tokens(report.Cost)} tokens in / out / cached, {CostReportFormatting.Cost(report.Cost)}";
            markdown.Append("## ").AppendLine(Markdown(summary)).AppendLine()
                .AppendLine("| Scenario | Profile | Score / 5 | Tokens in / out / cached | Est. USD |")
                .AppendLine("|---|---|---|---|---|");
            html.Append("<h2>").Append(WebUtility.HtmlEncode(summary)).AppendLine("</h2>")
                .AppendLine("<div class=\"table-scroll\"><table><thead><tr><th>Scenario</th><th>Profile</th><th>Score / 5</th><th>Tokens in / out / cached</th><th>Est. USD</th></tr></thead><tbody>");
            foreach (var result in report.Results)
            {
                var cells = new[]
                {
                    result.ScenarioId, result.ProfileName, Score(result.Score),
                    CostReportFormatting.Tokens(result.Cost), CostReportFormatting.Cost(result.Cost)
                };
                markdown.Append("| ").Append(string.Join(" | ", cells.Select(Markdown))).AppendLine(" |");
                html.Append("<tr>").Append(string.Concat(cells.Select(cell => $"<td>{WebUtility.HtmlEncode(cell)}</td>")))
                    .AppendLine("</tr>");
            }
            markdown.AppendLine();
            html.AppendLine("</tbody></table></div>");
        }
        html.AppendLine("</main></body></html>");
        File.WriteAllText(prefix + ".md", markdown.ToString());
        File.WriteAllText(prefix + ".html", html.ToString());
        File.WriteAllText(prefix + ".json", JsonSerializer.Serialize(new
        {
            costNote = CostReportFormatting.EstimateNote,
            recommendation,
            reports
        }, new JsonSerializerOptions { WriteIndented = true }));
        return [prefix + ".md", prefix + ".json", prefix + ".html"];
    }

    private static string Score(double? score) =>
        score?.ToString("0.0", CultureInfo.InvariantCulture) ?? "N/A";

    private static string Markdown(string text) => text.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");
}
