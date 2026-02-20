#!/usr/bin/env dotnet-script
// Usage: dotnet script scripts/Summarize-EvalResults.csx [-- <execution-id>]
// If no execution-id supplied, uses the most recent.
// Output: compact markdown summary to stdout.

#r "nuget: System.Text.Json, 9.0.0"

using System.Text.Json;
using System.Text.RegularExpressions;

var resultsRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).Replace("Local", "Local\\Temp"),
    "lucia-eval-reports", "results");

// Also check %TEMP% directly
if (!Directory.Exists(resultsRoot))
    resultsRoot = Path.Combine(Path.GetTempPath(), "lucia-eval-reports", "results");

if (!Directory.Exists(resultsRoot))
{
    Console.Error.WriteLine($"Results directory not found: {resultsRoot}");
    return;
}

// Resolve execution
var execId = Args.Count > 0 ? Args[0] : null;
string execDir;
if (execId is not null)
{
    execDir = Path.Combine(resultsRoot, execId);
    if (!Directory.Exists(execDir))
    {
        Console.Error.WriteLine($"Execution not found: {execId}");
        Console.Error.WriteLine("Available: " + string.Join(", ",
            Directory.GetDirectories(resultsRoot).Select(Path.GetFileName).OrderDescending().Take(5)));
        return;
    }
}
else
{
    execDir = Directory.GetDirectories(resultsRoot)
        .OrderByDescending(d => Path.GetFileName(d))
        .FirstOrDefault();
    if (execDir is null) { Console.Error.WriteLine("No executions found."); return; }
    execId = Path.GetFileName(execDir);
}

// Parse all scenario JSONs
var scenarios = new List<ScenarioResult>();
foreach (var scenarioDir in Directory.GetDirectories(execDir))
{
    var jsonFile = Path.Combine(scenarioDir, "1.json");
    if (!File.Exists(jsonFile)) continue;

    using var doc = JsonDocument.Parse(File.ReadAllText(jsonFile));
    var root = doc.RootElement;

    var scenarioName = root.GetProperty("scenarioName").GetString() ?? "?";

    // Parse user prompt from messages
    var userPrompt = "";
    if (root.TryGetProperty("messages", out var msgs))
    {
        foreach (var msg in msgs.EnumerateArray())
        {
            if (msg.GetProperty("role").GetString() == "user" &&
                msg.TryGetProperty("contents", out var contents))
            {
                foreach (var c in contents.EnumerateArray())
                {
                    if (c.TryGetProperty("text", out var t))
                        userPrompt = t.GetString() ?? "";
                }
            }
        }
    }

    // Parse final agent text response
    var agentResponse = "";
    var toolCalls = new List<string>();
    if (root.TryGetProperty("modelResponse", out var modelResp) &&
        modelResp.TryGetProperty("messages", out var respMsgs))
    {
        foreach (var msg in respMsgs.EnumerateArray())
        {
            if (!msg.TryGetProperty("contents", out var contents)) continue;
            foreach (var c in contents.EnumerateArray())
            {
                var type = c.TryGetProperty("$type", out var t) ? t.GetString() : "";
                if (type == "text" && c.TryGetProperty("text", out var textEl))
                    agentResponse = textEl.GetString() ?? "";
                else if (type == "functionCall" && c.TryGetProperty("name", out var nameEl))
                    toolCalls.Add(nameEl.GetString() ?? "?");
            }
        }
    }

    // Parse metrics
    var metrics = new Dictionary<string, MetricResult>();
    if (root.TryGetProperty("evaluationResult", out var evalResult) &&
        evalResult.TryGetProperty("metrics", out var metricsEl))
    {
        foreach (var prop in metricsEl.EnumerateObject())
        {
            var name = prop.Name;
            var m = prop.Value;
            var value = m.TryGetProperty("value", out var v) ? v.ToString() : "?";
            var rating = "";
            var failed = false;
            var reason = "";
            if (m.TryGetProperty("interpretation", out var interp))
            {
                rating = interp.TryGetProperty("rating", out var r) ? r.GetString() ?? "" : "";
                failed = interp.TryGetProperty("failed", out var f) && f.GetBoolean();
                reason = interp.TryGetProperty("reason", out var rsn) ? rsn.GetString() ?? "" : "";
            }
            metrics[name] = new MetricResult(value, rating, failed, reason);
        }
    }

    // Parse tags for model
    var model = "";
    if (root.TryGetProperty("tags", out var tags))
    {
        foreach (var tag in tags.EnumerateArray())
            model = tag.GetString() ?? "";
    }

    scenarios.Add(new ScenarioResult(scenarioName, model, userPrompt, agentResponse, toolCalls, metrics));
}

// ── Generate Markdown ──────────────────────────────────────────────────

var sb = new System.Text.StringBuilder();
sb.AppendLine($"# Eval Report: `{execId}`");
sb.AppendLine();

// Summary table
var passed = scenarios.Count(s => !s.Metrics.Any(m => m.Value.Failed));
var failed = scenarios.Count - passed;
sb.AppendLine($"**{scenarios.Count} scenarios** | \u2705 {passed} passed | \u274c {failed} failed");
sb.AppendLine();

// Group by test (strip model from scenario name)
var grouped = scenarios
    .GroupBy(s => Regex.Replace(s.ScenarioName, @"\[.*?\]$", ""))
    .OrderBy(g => g.Key);

// Metric columns — collect all unique metric names
var allMetricNames = scenarios.SelectMany(s => s.Metrics.Keys).Distinct().OrderBy(n => n).ToList();

sb.AppendLine("| Scenario | Model | " + string.Join(" | ", allMetricNames) + " | Result |");
sb.AppendLine("|" + string.Join("|", Enumerable.Repeat("---", allMetricNames.Count + 3)) + "|");

foreach (var group in grouped)
{
    foreach (var s in group.OrderBy(s => s.Model))
    {
        var shortName = Regex.Replace(s.ScenarioName, @"\[.*?\]$", "").Split('.').Last();
        var result = s.Metrics.Any(m => m.Value.Failed) ? "\u274c" : "\u2705";

        var metricCells = allMetricNames.Select(name =>
        {
            if (!s.Metrics.TryGetValue(name, out var m)) return "-";
            var icon = m.Failed ? "\u274c" : "\u2705";
            return $"{icon} {m.Value} ({m.Rating})";
        });

        sb.AppendLine($"| {shortName} | {s.Model} | {string.Join(" | ", metricCells)} | {result} |");
    }
}

sb.AppendLine();

// Details for failed scenarios
var failedScenarios = scenarios.Where(s => s.Metrics.Any(m => m.Value.Failed)).ToList();
if (failedScenarios.Any())
{
    sb.AppendLine("## Failed Scenario Details");
    sb.AppendLine();
    foreach (var s in failedScenarios)
    {
        sb.AppendLine($"### {s.ScenarioName}");
        sb.AppendLine($"- **Prompt:** {s.UserPrompt}");
        sb.AppendLine($"- **Tools called:** {(s.ToolCalls.Any() ? string.Join(" \u2192 ", s.ToolCalls) : "none")}");

        var respPreview = s.AgentResponse.Length > 200
            ? s.AgentResponse[..200] + "..."
            : s.AgentResponse;
        sb.AppendLine($"- **Response:** {(string.IsNullOrEmpty(respPreview) ? "(empty)" : respPreview)}");

        var failedMetrics = s.Metrics.Where(m => m.Value.Failed);
        foreach (var fm in failedMetrics)
            sb.AppendLine($"- \u274c **{fm.Key}:** {fm.Value.Rating} ({fm.Value.Value}) \u2014 {fm.Value.Reason}");
        sb.AppendLine();
    }
}

Console.Write(sb.ToString());

// ── Types ──────────────────────────────────────────────────────────────

record MetricResult(string Value, string Rating, bool Failed, string Reason);
record ScenarioResult(
    string ScenarioName,
    string Model,
    string UserPrompt,
    string AgentResponse,
    List<string> ToolCalls,
    Dictionary<string, MetricResult> Metrics);
