using System.Text.Json.Serialization;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Evaluation;
using lucia.EvalHarness.Infrastructure;

namespace lucia.EvalHarness.Reports;

/// <summary>
/// Flattened viewmodel optimized for JSON embedding in the HTML report template.
/// Transforms the nested <see cref="EvalRunResult"/> into a structure easily
/// consumed by vanilla JavaScript.
/// </summary>
public sealed class HtmlReportData
{
    [JsonPropertyName("runId")]
    public required string RunId { get; init; }

    [JsonPropertyName("startedAt")]
    public required string StartedAt { get; init; }

    [JsonPropertyName("completedAt")]
    public required string CompletedAt { get; init; }

    [JsonPropertyName("durationSeconds")]
    public required double DurationSeconds { get; init; }

    [JsonPropertyName("gpu")]
    public required HtmlGpuInfo Gpu { get; init; }

    [JsonPropertyName("models")]
    public required List<string> Models { get; init; }

    [JsonPropertyName("agents")]
    public required List<HtmlAgentData> Agents { get; init; }

    [JsonPropertyName("profileComparison")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<HtmlProfileComparisonGroup>? ProfileComparison { get; init; }

    /// <summary>
    /// Transforms an <see cref="EvalRunResult"/> into the HTML report data model.
    /// </summary>
    public static HtmlReportData FromEvalResult(EvalRunResult result, GpuInfo gpuInfo)
    {
        var allModels = result.AgentResults
            .SelectMany(a => a.ModelResults)
            .Select(m => m.ModelName)
            .Distinct()
            .ToList();

        // Build profile comparison data if multiple profiles present
        var profileNames = result.AgentResults
            .SelectMany(a => a.ModelResults)
            .Where(m => m.ParameterProfile is not null)
            .Select(m => m.ParameterProfile!.Name)
            .Distinct()
            .ToList();

        List<HtmlProfileComparisonGroup>? profileComparison = null;
        if (profileNames.Count > 1)
        {
            profileComparison = result.AgentResults
                .SelectMany(a => a.ModelResults)
                .Where(m => m.ParameterProfile is not null)
                .GroupBy(m => m.ModelName)
                .Select(modelGroup => new HtmlProfileComparisonGroup
                {
                    ModelName = modelGroup.Key,
                    Profiles = modelGroup
                        .GroupBy(m => m.ParameterProfile!.Name)
                        .Select(pg => new HtmlProfileScore
                        {
                            ProfileName = pg.Key,
                            Parameters = new HtmlParameterData
                            {
                                Name = pg.Key,
                                Temperature = pg.First().ParameterProfile!.Temperature,
                                TopK = pg.First().ParameterProfile!.TopK,
                                TopP = pg.First().ParameterProfile!.TopP,
                                RepeatPenalty = pg.First().ParameterProfile!.RepeatPenalty
                            },
                            AvgOverall = pg.Average(m => m.OverallScore),
                            AvgToolSelection = pg.Average(m => m.ToolSelectionScore),
                            AvgToolSuccess = pg.Average(m => m.ToolSuccessScore),
                            AvgToolEfficiency = pg.Average(m => m.ToolEfficiencyScore),
                            AvgTaskCompletion = pg.Average(m => m.TaskCompletionScore),
                            PassRate = pg.Sum(m => m.TestCaseCount) > 0
                                ? (double)pg.Sum(m => m.PassedCount) / pg.Sum(m => m.TestCaseCount)
                                : 0,
                            AvgLatencyMs = pg.Average(m => m.Performance.MeanLatency.TotalMilliseconds)
                        })
                        .OrderByDescending(p => p.AvgOverall)
                        .ToList()
                })
                .ToList();
        }

        return new HtmlReportData
        {
            RunId = result.RunId,
            StartedAt = result.StartedAt.ToString("o"),
            CompletedAt = result.CompletedAt.ToString("o"),
            DurationSeconds = (result.CompletedAt - result.StartedAt).TotalSeconds,
            Gpu = new HtmlGpuInfo
            {
                Label = gpuInfo.GpuLabel,
                VramMb = gpuInfo.VramMb ?? 0
            },
            Models = allModels,
            Agents = result.AgentResults.Select(a => new HtmlAgentData
            {
                AgentName = a.AgentName,
                Models = a.ModelResults.Select(m => new HtmlModelData
                {
                    ModelName = m.ModelName,
                    OverallScore = m.OverallScore,
                    ToolSelectionScore = m.ToolSelectionScore,
                    ToolSuccessScore = m.ToolSuccessScore,
                    ToolEfficiencyScore = m.ToolEfficiencyScore,
                    TaskCompletionScore = m.TaskCompletionScore,
                    TestCaseCount = m.TestCaseCount,
                    PassedCount = m.PassedCount,
                    Parameters = m.ParameterProfile is not null
                        ? new HtmlParameterData
                        {
                            Name = m.ParameterProfile.Name,
                            Temperature = m.ParameterProfile.Temperature,
                            TopK = m.ParameterProfile.TopK,
                            TopP = m.ParameterProfile.TopP,
                            RepeatPenalty = m.ParameterProfile.RepeatPenalty,
                            Seed = m.ParameterProfile.Seed
                        }
                        : null,
                    Performance = new HtmlPerformanceData
                    {
                        MeanLatencyMs = m.Performance.MeanLatency.TotalMilliseconds,
                        MedianLatencyMs = m.Performance.MedianLatency.TotalMilliseconds,
                        P95LatencyMs = m.Performance.P95Latency.TotalMilliseconds,
                        MinLatencyMs = m.Performance.MinLatency.TotalMilliseconds,
                        MaxLatencyMs = m.Performance.MaxLatency.TotalMilliseconds
                    },
                    TestCases = m.TestCaseResults.Select(tc => new HtmlTestCaseData
                    {
                        Id = tc.TestCaseId,
                        Passed = tc.Passed,
                        Score = tc.Score,
                        LatencyMs = tc.Latency.TotalMilliseconds,
                        FailureReason = tc.FailureReason,
                        Conversation = tc.ConversationHistory?.Select(turn => new HtmlConversationTurn
                        {
                            Role = turn.Role,
                            Content = turn.Content,
                            ToolCalls = turn.ToolCalls?.Select(t => new HtmlToolCall
                            {
                                Name = t.Name,
                                Arguments = t.Arguments
                            }).ToList()
                        }).ToList()
                    }).ToList()
                }).ToList()
            }).ToList(),
            ProfileComparison = profileComparison
        };
    }
}
