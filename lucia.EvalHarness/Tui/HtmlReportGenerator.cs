using System.Text;
using System.Text.Json;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Infrastructure;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Generates an HTML report from evaluation results.
/// </summary>
public static class HtmlReportGenerator
{
    /// <summary>
    /// Generates an HTML report file and returns its path.
    /// </summary>
    public static string Generate(EvalRunResult result, GpuInfo gpuInfo, string reportDir)
    {
        Directory.CreateDirectory(reportDir);

        var timestamp = result.StartedAt.ToString("yyyyMMdd_HHmmss");
        var htmlPath = Path.Combine(reportDir, $"eval-{timestamp}.html");

        var html = BuildHtml(result, gpuInfo);
        File.WriteAllText(htmlPath, html);

        return htmlPath;
    }

    private static string BuildHtml(EvalRunResult result, GpuInfo gpuInfo)
    {
        var sb = new StringBuilder();
        var duration = result.CompletedAt - result.StartedAt;
        var allModels = result.AgentResults
            .SelectMany(a => a.ModelResults)
            .Select(m => m.ModelName)
            .Distinct()
            .ToList();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang='en'><head>");
        sb.AppendLine("<meta charset='UTF-8'>");
        sb.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        sb.AppendLine($"<title>lucia Eval Report — {result.RunId}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; margin: 2rem; background: #1a1a2e; color: #e0e0e0; }");
        sb.AppendLine("table { border-collapse: collapse; width: 100%; margin: 1rem 0; }");
        sb.AppendLine("th, td { padding: 0.5rem 0.75rem; border: 1px solid #333; text-align: left; }");
        sb.AppendLine("th { background: #16213e; color: #e0e0e0; }");
        sb.AppendLine("tr:nth-child(even) { background: #1a1a2e; }");
        sb.AppendLine("h1, h2, h3 { color: #4fc3f7; }");
        sb.AppendLine(".score-high { color: #66bb6a; font-weight: bold; }");
        sb.AppendLine(".score-mid { color: #fdd835; }");
        sb.AppendLine(".score-low { color: #ef5350; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head><body>");

        sb.AppendLine($"<h1>lucia Eval Harness Report</h1>");
        sb.AppendLine($"<p>Run ID: <code>{result.RunId}</code> | Duration: {duration.TotalSeconds:F1}s | GPU: {gpuInfo.GpuLabel} | Timestamp: {result.StartedAt:yyyy-MM-dd HH:mm:ss} UTC</p>");

        // Quality matrix
        sb.AppendLine("<h2>Quality Scores (0–100)</h2>");
        sb.AppendLine("<table><tr><th>Agent</th>");
        foreach (var model in allModels)
            sb.AppendLine($"<th>{model}</th>");
        sb.AppendLine("</tr>");

        foreach (var agent in result.AgentResults)
        {
            sb.AppendLine($"<tr><td><strong>{agent.AgentName}</strong></td>");
            foreach (var model in allModels)
            {
                var mr = agent.ModelResults.FirstOrDefault(m => m.ModelName == model);
                if (mr is not null)
                {
                    var cls = mr.OverallScore >= 80 ? "score-high" : mr.OverallScore >= 60 ? "score-mid" : "score-low";
                    sb.AppendLine($"<td class='{cls}'>{mr.OverallScore:F1}</td>");
                }
                else
                {
                    sb.AppendLine("<td>N/A</td>");
                }
            }
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table>");

        // Per-agent details
        foreach (var agent in result.AgentResults)
        {
            sb.AppendLine($"<h2>{agent.AgentName}</h2>");
            sb.AppendLine("<table><tr><th>Model</th><th>Overall</th><th>Tool Sel</th><th>Tool Succ</th><th>Tool Eff</th><th>Task Comp</th><th>Pass Rate</th><th>Mean Latency</th></tr>");

            foreach (var m in agent.ModelResults.OrderByDescending(m => m.OverallScore))
            {
                var passRate = m.TestCaseCount > 0 ? (double)m.PassedCount / m.TestCaseCount : 0;
                var cls = m.OverallScore >= 80 ? "score-high" : m.OverallScore >= 60 ? "score-mid" : "score-low";
                sb.AppendLine($"<tr><td>{m.ModelName}</td><td class='{cls}'>{m.OverallScore:F1}</td><td>{m.ToolSelectionScore:F1}</td><td>{m.ToolSuccessScore:F1}</td><td>{m.ToolEfficiencyScore:F1}</td><td>{m.TaskCompletionScore:F1}</td><td>{passRate:P0}</td><td>{m.Performance.MeanLatency.TotalMilliseconds:F0}ms</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }
}