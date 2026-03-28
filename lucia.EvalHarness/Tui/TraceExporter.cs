using System.Text.Json;
using System.Text.Json.Serialization;
using lucia.EvalHarness.Evaluation;
using Spectre.Console;

namespace lucia.EvalHarness.Tui;

/// <summary>
/// Exports per-test-case conversation traces for debugging model behavior.
/// Each model × agent combination gets its own trace file containing the full
/// input → tool calls → output chain for every test case.
/// </summary>
public static class TraceExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Writes trace files for all evaluated models/agents.
    /// Returns the list of written file paths.
    /// </summary>
    public static IReadOnlyList<string> Export(EvalRunResult result, string reportDir)
    {
        var traceDir = Path.Combine(reportDir, "traces");
        Directory.CreateDirectory(traceDir);

        var timestamp = result.StartedAt.ToString("yyyyMMdd_HHmmss");
        var writtenFiles = new List<string>();

        foreach (var agentResult in result.AgentResults)
        {
            foreach (var modelResult in agentResult.ModelResults)
            {
                var safeName = SanitizeFileName($"{modelResult.ModelName}_{agentResult.AgentName}");
                var filePath = Path.Combine(traceDir, $"trace-{safeName}-{timestamp}.json");

                var trace = new TraceDocument
                {
                    Model = modelResult.ModelName,
                    Agent = agentResult.AgentName,
                    Timestamp = result.StartedAt,
                    Summary = new TraceSummary
                    {
                        TotalTests = modelResult.TestCaseCount,
                        Passed = modelResult.PassedCount,
                        Failed = modelResult.TestCaseCount - modelResult.PassedCount,
                        OverallScore = modelResult.OverallScore,
                        MeanLatencyMs = modelResult.Performance.MeanLatency.TotalMilliseconds
                    },
                    TestCases = modelResult.TestCaseResults.Select(tc => new TraceTestCase
                    {
                        Id = tc.TestCaseId,
                        Passed = tc.Passed,
                        Score = tc.Score,
                        LatencyMs = tc.Latency.TotalMilliseconds,
                        FailureReason = tc.FailureReason,
                        Conversation = tc.ConversationHistory?.Select(turn => new TraceConversationTurn
                        {
                            Role = turn.Role,
                            Content = turn.Content,
                            ToolCalls = turn.ToolCalls?.Select(t => new TraceConversationToolCall
                            {
                                CallId = t.CallId,
                                Name = t.Name,
                                Arguments = t.Arguments
                            }).ToList(),
                            ToolCallId = turn.ToolCallId,
                            ToolName = turn.ToolName
                        }).ToList()
                            // Fallback: if no conversation history, build from Input/ToolCalls/Output
                            ?? BuildFallbackConversation(tc)
                    }).ToList()
                };

                File.WriteAllText(filePath, JsonSerializer.Serialize(trace, JsonOptions));
                writtenFiles.Add(filePath);
            }
        }

        return writtenFiles;
    }

    /// <summary>
    /// Renders a TUI summary of trace data for a specific model/agent when running interactively.
    /// </summary>
    public static void RenderTraceSummary(EvalRunResult result)
    {
        AnsiConsole.Write(new Rule("[bold]Conversation Traces[/]").LeftJustified());
        AnsiConsole.WriteLine();

        foreach (var agentResult in result.AgentResults)
        {
            foreach (var modelResult in agentResult.ModelResults)
            {
                AnsiConsole.MarkupLine($"[bold]{Markup.Escape(modelResult.ModelName)}[/] \u2192 [cyan]{Markup.Escape(agentResult.AgentName)}[/]");

                foreach (var tc in modelResult.TestCaseResults)
                {
                    var icon = tc.Passed ? "[green]\u2713[/]" : "[red]\u2717[/]";
                    AnsiConsole.MarkupLine($"  {icon} [bold]{Markup.Escape(tc.TestCaseId)}[/] (score: {tc.Score:F0}, {tc.Latency.TotalMilliseconds:F0}ms)");

                    if (tc.ConversationHistory is { Count: > 0 })
                    {
                        foreach (var turn in tc.ConversationHistory)
                        {
                            var (roleColor, roleIcon) = turn.Role switch
                            {
                                "system" => ("dim", "\U0001f4cb"),
                                "user" => ("blue", "\U0001f464"),
                                "assistant" => ("green", "\U0001f916"),
                                "tool" => ("yellow", "\U0001f527"),
                                _ => ("white", "\u2022")
                            };

                            if (turn.Role == "system")
                            {
                                AnsiConsole.MarkupLine($"    [{roleColor}]{roleIcon} system:[/] [{roleColor}]{Markup.Escape(Truncate(turn.Content ?? "", 100))}[/]");
                            }
                            else if (turn.ToolCalls is { Count: > 0 })
                            {
                                // Assistant making tool calls
                                foreach (var call in turn.ToolCalls)
                                {
                                    var argsStr = call.Arguments is { Count: > 0 }
                                        ? string.Join(", ", call.Arguments.Select(a => $"{a.Key}={a.Value}"))
                                        : "";
                                    AnsiConsole.MarkupLine($"    [{roleColor}]{roleIcon} assistant \u2192 {Markup.Escape(call.Name)}({Markup.Escape(Truncate(argsStr, 80))})[/]");
                                }
                                if (turn.Content is not null)
                                    AnsiConsole.MarkupLine($"    [{roleColor}]   + text: {Markup.Escape(Truncate(turn.Content, 100))}[/]");
                            }
                            else if (turn.Role == "tool")
                            {
                                AnsiConsole.MarkupLine($"    [{roleColor}]{roleIcon} {Markup.Escape(turn.ToolName ?? "tool")} \u2190 {Markup.Escape(Truncate(turn.Content ?? "", 120))}[/]");
                            }
                            else
                            {
                                AnsiConsole.MarkupLine($"    [{roleColor}]{roleIcon} {turn.Role}: {Markup.Escape(Truncate(turn.Content ?? "", 120))}[/]");
                            }
                        }
                    }
                    else if (tc.FailureReason is not null)
                    {
                        AnsiConsole.MarkupLine($"    [red]Error: {Markup.Escape(Truncate(tc.FailureReason, 120))}[/]");
                    }
                }
                AnsiConsole.WriteLine();
            }
        }
    }

    private static List<TraceConversationTurn> BuildFallbackConversation(TestCaseResult tc)
    {
        var turns = new List<TraceConversationTurn>();
        if (tc.Input is not null)
            turns.Add(new TraceConversationTurn { Role = "user", Content = tc.Input });
        if (tc.ToolCalls is { Count: > 0 })
        {
            foreach (var tool in tc.ToolCalls)
            {
                turns.Add(new TraceConversationTurn
                {
                    Role = "assistant",
                    ToolCalls = [new TraceConversationToolCall { Name = tool.ToolName, Arguments = tool.Arguments?.ToDictionary(k => k.Key, v => v.Value?.ToString()) }]
                });
                turns.Add(new TraceConversationTurn { Role = "tool", Content = tool.Result, ToolName = tool.ToolName });
            }
        }
        if (tc.AgentOutput is not null)
            turns.Add(new TraceConversationTurn { Role = "assistant", Content = tc.AgentOutput });
        return turns;
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen] + "\u2026";

    private static string SanitizeFileName(string name) =>
        string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));
}

// ── Trace document models ─────────────────────────────────────────

file sealed class TraceDocument
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("agent")]
    public required string Agent { get; init; }

    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("summary")]
    public required TraceSummary Summary { get; init; }

    [JsonPropertyName("test_cases")]
    public required List<TraceTestCase> TestCases { get; init; }
}

file sealed class TraceSummary
{
    [JsonPropertyName("total_tests")]
    public required int TotalTests { get; init; }

    [JsonPropertyName("passed")]
    public required int Passed { get; init; }

    [JsonPropertyName("failed")]
    public required int Failed { get; init; }

    [JsonPropertyName("overall_score")]
    public required double OverallScore { get; init; }

    [JsonPropertyName("mean_latency_ms")]
    public required double MeanLatencyMs { get; init; }
}

file sealed class TraceTestCase
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("passed")]
    public required bool Passed { get; init; }

    [JsonPropertyName("score")]
    public required double Score { get; init; }

    [JsonPropertyName("latency_ms")]
    public required double LatencyMs { get; init; }

    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; init; }

    [JsonPropertyName("conversation")]
    public required List<TraceConversationTurn> Conversation { get; init; }
}

sealed class TraceConversationTurn
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TraceConversationToolCall>? ToolCalls { get; init; }

    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; init; }

    [JsonPropertyName("tool_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolName { get; init; }
}

sealed class TraceConversationToolCall
{
    [JsonPropertyName("call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CallId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string?>? Arguments { get; init; }
}
