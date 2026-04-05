using AgentEval.Core;
using AgentEval.MAF;
using AgentEval.Metrics.Agentic;
using AgentEval.Models;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Providers;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Evaluation;

/// <summary>
/// Result of a full evaluation run across selected models and agents.
/// </summary>
public sealed class EvalRunResult
{
    public required string RunId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required IReadOnlyList<AgentEvalResult> AgentResults { get; init; }
}

/// <summary>
/// Results for a single agent evaluated across multiple models.
/// </summary>
public sealed class AgentEvalResult
{
    public required string AgentName { get; init; }
    public required IReadOnlyList<ModelEvalResult> ModelResults { get; init; }
}

/// <summary>
/// Results for a single model on a single agent's test suite.
/// </summary>
public sealed class ModelEvalResult
{
    public required string ModelName { get; init; }
    public required string AgentName { get; init; }
    public required double ToolSelectionScore { get; init; }
    public required double ToolSuccessScore { get; init; }
    public required double ToolEfficiencyScore { get; init; }
    public required double TaskCompletionScore { get; init; }
    public required double OverallScore { get; init; }
    public required int TestCaseCount { get; init; }
    public required int PassedCount { get; init; }
    public required ModelPerformanceSummary Performance { get; init; }
    public required IReadOnlyList<TestCaseResult> TestCaseResults { get; init; }

    /// <summary>
    /// The inference parameter profile used for this evaluation.
    /// </summary>
    public ModelParameterProfile? ParameterProfile { get; init; }
}

/// <summary>
/// Result of a single test case evaluation.
/// </summary>
public sealed class TestCaseResult
{
    public required string TestCaseId { get; init; }
    public required bool Passed { get; init; }
    public required double Score { get; init; }
    public required TimeSpan Latency { get; init; }
    public string? FailureReason { get; init; }

    /// <summary>
    /// The agent's full text response for this test case.
    /// </summary>
    public string? AgentOutput { get; init; }

    /// <summary>
    /// The user input that was sent to the agent.
    /// </summary>
    public string? Input { get; init; }

    /// <summary>
    /// Raw tool call records captured during execution.
    /// Only populated when trace capture is enabled.
    /// </summary>
    public IReadOnlyList<ToolCallTrace>? ToolCalls { get; init; }

    /// <summary>
    /// Full ordered conversation history: system prompt, user input, assistant
    /// responses, tool calls, and tool results. Populated when tracing is enabled.
    /// </summary>
    public IReadOnlyList<ConversationTurn>? ConversationHistory { get; init; }
}

/// <summary>
/// Captured tool call details for trace export.
/// </summary>
public sealed class ToolCallTrace
{
    public required string ToolName { get; init; }
    public required int Order { get; init; }
    public Dictionary<string, object?>? Arguments { get; init; }
    public string? Result { get; init; }
    public string? Error { get; init; }
    public double? DurationMs { get; init; }
}

/// <summary>
/// Orchestrates AgentEval evaluations across models and agents.
/// Uses <see cref="MAFEvaluationHarness"/> for agent execution,
/// <see cref="ToolSelectionMetric"/> / <see cref="ToolSuccessMetric"/> /
/// <see cref="ToolEfficiencyMetric"/> for code-based metrics,
/// and <see cref="TaskCompletionMetric"/> for LLM-as-judge scoring.
/// </summary>
public sealed class EvalRunner
{
    private readonly HarnessConfiguration _config;
    private readonly IChatClient _judgeChatClient;
    private readonly PerformanceCollector _perfCollector = new();

    public EvalRunner(
        HarnessConfiguration config,
        IChatClient judgeChatClient)
    {
        _config = config;
        _judgeChatClient = judgeChatClient;
    }

    /// <summary>
    /// Runs evaluation for a real lucia agent instance against its test suite.
    /// The agent is already constructed with the Ollama model wired in.
    /// Reports progress via the provided callback.
    /// </summary>
    public async Task<ModelEvalResult> EvaluateRealAgentAsync(
        string modelName,
        RealAgentInstance agentInstance,
        IReadOnlyList<TestCase> testCases,
        ModelParameterProfile? parameterProfile = null,
        Action<string>? onProgress = null,
        CancellationToken ct = default)
    {
        var harness = new MAFEvaluationHarness(verbose: false);
        var options = new EvaluationOptions
        {
            TrackTools = true,
            TrackPerformance = true,
            ModelName = modelName
        };

        var metrics = CreateMetrics(testCases);
        var testCaseResults = new List<TestCaseResult>();
        var perfSnapshots = new List<PerformanceSnapshot>();
        var metricScores = new Dictionary<string, List<double>>
        {
            ["tool_selection"] = [],
            ["tool_success"] = [],
            ["tool_efficiency"] = [],
            ["task_completion"] = []
        };

        // Wrap the real agent's AIAgent for AgentEval
        var aiAgent = agentInstance.Agent.GetAIAgent();
        var evaluableAgent = new MAFAgentAdapter(aiAgent);

        for (var i = 0; i < testCases.Count; i++)
        {
            var tc = testCases[i];
            onProgress?.Invoke($"[{i + 1}/{testCases.Count}] {tc.Name ?? tc.Input[..Math.Min(40, tc.Input.Length)]}...");

            // Reset conversation tracer for this test case
            agentInstance.Tracer?.Reset();

            try
            {
                var (evalResult, perf) = await _perfCollector.MeasureAsync(async () =>
                    await harness.RunEvaluationAsync(evaluableAgent, tc, options, ct), ct);

                perfSnapshots.Add(perf);

                // Evaluate metrics
                var context = new EvaluationContext
                {
                    Input = tc.Input,
                    Output = evalResult.ActualOutput ?? string.Empty,
                    ToolUsage = evalResult.ToolUsage,
                    Performance = evalResult.Performance,
                    ExpectedTools = tc.ExpectedTools
                };

                double selScore = 0, succScore = 0, effScore = 0, compScore = 0;

                foreach (var metric in metrics)
                {
                    var metricResult = await metric.EvaluateAsync(context, ct);
                    switch (metric.Name)
                    {
                        case "code_tool_selection":
                            selScore = metricResult.Score;
                            metricScores["tool_selection"].Add(selScore);
                            break;
                        case "code_tool_success":
                            succScore = metricResult.Score;
                            metricScores["tool_success"].Add(succScore);
                            break;
                        case "code_tool_efficiency":
                            effScore = metricResult.Score;
                            metricScores["tool_efficiency"].Add(effScore);
                            break;
                        case "llm_task_completion":
                            compScore = metricResult.Score;
                            metricScores["task_completion"].Add(compScore);
                            break;
                    }
                }

                var avgScore = (selScore + succScore + effScore + compScore) / 4;
                testCaseResults.Add(new TestCaseResult
                {
                    TestCaseId = tc.Name ?? $"test_{i}",
                    Passed = avgScore >= 70,
                    Score = avgScore,
                    Latency = perf.TotalDuration,
                    Input = tc.Input,
                    AgentOutput = evalResult.ActualOutput,
                    ToolCalls = CaptureToolCalls(evalResult.ToolUsage),
                    ConversationHistory = agentInstance.Tracer?.Turns.ToList()
                });
            }
            catch (Exception ex)
            {
                testCaseResults.Add(new TestCaseResult
                {
                    TestCaseId = tc.Name ?? $"test_{i}",
                    Passed = false,
                    Score = 0,
                    Latency = TimeSpan.Zero,
                    FailureReason = ex.Message,
                    Input = tc.Input
                });
            }
        }

        var perfSummary = ModelPerformanceSummary.FromSnapshots(modelName, perfSnapshots);

        return new ModelEvalResult
        {
            ModelName = modelName,
            AgentName = agentInstance.AgentName,
            ToolSelectionScore = Average(metricScores["tool_selection"]),
            ToolSuccessScore = Average(metricScores["tool_success"]),
            ToolEfficiencyScore = Average(metricScores["tool_efficiency"]),
            TaskCompletionScore = Average(metricScores["task_completion"]),
            OverallScore = metricScores.Values.SelectMany(v => v).DefaultIfEmpty(0).Average(),
            TestCaseCount = testCases.Count,
            PassedCount = testCaseResults.Count(r => r.Passed),
            Performance = perfSummary,
            TestCaseResults = testCaseResults,
            ParameterProfile = parameterProfile
        };
    }

    private IReadOnlyList<IAgenticMetric> CreateMetrics(IReadOnlyList<TestCase> testCases)
    {
        // Collect expected tools from all test cases for the selection metric
        var allExpectedTools = testCases
            .Where(tc => tc.ExpectedTools is { Count: > 0 })
            .SelectMany(tc => tc.ExpectedTools!)
            .Distinct()
            .ToList();

        var metrics = new List<IAgenticMetric>
        {
            new ToolSelectionMetric(allExpectedTools),
            new ToolSuccessMetric(),
            new ToolEfficiencyMetric()
        };

        // TaskCompletionMetric uses the judge LLM
        metrics.Add(new TaskCompletionMetric(_judgeChatClient));

        return metrics;
    }

    private static double Average(List<double> values) =>
        values.Count > 0 ? values.Average() : 0;

    private static IReadOnlyList<ToolCallTrace>? CaptureToolCalls(AgentEval.Models.ToolUsageReport? toolUsage)
    {
        if (toolUsage is null || toolUsage.Count == 0)
            return null;

        return toolUsage.Calls.Select(call => new ToolCallTrace
        {
            ToolName = call.Name,
            Order = call.Order,
            Arguments = call.Arguments?.ToDictionary(
                kvp => kvp.Key,
                kvp => (object?)kvp.Value?.ToString()),
            Result = call.Result?.ToString(),
            Error = call.Exception?.Message,
            DurationMs = call.Duration?.TotalMilliseconds
        }).ToList();
    }

    private static string BuildScenarioPrompt(TestScenario scenario)
    {
        var sb = new System.Text.StringBuilder();

        if (scenario.SpeakerId is not null || scenario.DeviceArea is not null || scenario.Location is not null)
        {
            sb.Append('[');

            var hasPreviousMetadata = false;

            if (scenario.SpeakerId is not null)
            {
                sb.Append($"Speaker: {scenario.SpeakerId}");
                hasPreviousMetadata = true;
            }

            if (scenario.DeviceArea is not null)
            {
                if (hasPreviousMetadata)
                {
                    sb.Append(" | ");
                }

                sb.Append($"Device Area: {scenario.DeviceArea}");
                hasPreviousMetadata = true;
            }

            if (scenario.Location is not null)
            {
                if (hasPreviousMetadata)
                {
                    sb.Append(" | ");
                }

                sb.Append($"Location: {scenario.Location}");
            }

            sb.AppendLine("]");
        }

        sb.Append(scenario.UserPrompt);
        return sb.ToString();
    }

    // ─── Scenario-Based Evaluation ────────────────────────────────────

    /// <summary>
    /// Runs scenario-based evaluation: sets up known HA state per test,
    /// runs the agent, then validates tool call chain and final state
    /// using <see cref="ScenarioValidator"/>.
    /// </summary>
    public async Task<ModelEvalResult> EvaluateScenariosAsync(
        string modelName,
        RealAgentInstance agentInstance,
        IReadOnlyList<TestScenario> scenarios,
        IHomeAssistantClient haClient,
        ModelParameterProfile? parameterProfile = null,
        Action<string>? onProgress = null,
        CancellationToken ct = default)
    {
        var harness = new MAFEvaluationHarness(verbose: false);
        var options = new EvaluationOptions
        {
            TrackTools = true,
            TrackPerformance = true,
            ModelName = modelName
        };

        var testCaseResults = new List<TestCaseResult>();
        var perfSnapshots = new List<PerformanceSnapshot>();

        var aiAgent = agentInstance.Agent.GetAIAgent();
        var evaluableAgent = new MAFAgentAdapter(aiAgent);

        for (var i = 0; i < scenarios.Count; i++)
        {
            var scenario = scenarios[i];
            onProgress?.Invoke($"[{i + 1}/{scenarios.Count}] {scenario.Id}");

            // Reset tracer for this scenario
            agentInstance.Tracer?.Reset();

            try
            {
                // Set up known HA state
                await ScenarioValidator.SetupInitialStateAsync(haClient, scenario);

                // Build an AgentEval TestCase from the scenario
                var promptText = BuildScenarioPrompt(scenario);
                var testCase = new TestCase
                {
                    Name = scenario.Id,
                    Input = promptText,
                    ExpectedOutputContains = scenario.ResponseMustContain.FirstOrDefault()
                };

                // Run the agent
                var (evalResult, perf) = await _perfCollector.MeasureAsync(async () =>
                    await harness.RunEvaluationAsync(evaluableAgent, testCase, options, ct), ct);

                perfSnapshots.Add(perf);

                // Get conversation turns for validation
                var conversation = agentInstance.Tracer?.Turns.ToList()
                    ?? new List<ConversationTurn>();

                // Validate against scenario expectations
                var validation = await ScenarioValidator.ValidateAsync(scenario, conversation, haClient);

                testCaseResults.Add(new TestCaseResult
                {
                    TestCaseId = scenario.Id,
                    Passed = validation.Passed,
                    Score = validation.Score,
                    Latency = perf.TotalDuration,
                    Input = promptText,
                    AgentOutput = evalResult.ActualOutput,
                    ToolCalls = CaptureToolCalls(evalResult.ToolUsage),
                    ConversationHistory = conversation,
                    FailureReason = validation.Passed ? null : validation.Summary
                });
            }
            catch (Exception ex)
            {
                testCaseResults.Add(new TestCaseResult
                {
                    TestCaseId = scenario.Id,
                    Passed = false,
                    Score = 0,
                    Latency = TimeSpan.Zero,
                    FailureReason = ex.Message,
                    Input = scenario.UserPrompt
                });
            }
        }

        var perfSummary = ModelPerformanceSummary.FromSnapshots(modelName, perfSnapshots);
        var passedCount = testCaseResults.Count(r => r.Passed);
        var scores = testCaseResults.Select(r => r.Score).ToList();

        return new ModelEvalResult
        {
            ModelName = modelName,
            AgentName = agentInstance.AgentName,
            ToolSelectionScore = scores.DefaultIfEmpty(0).Average(),
            ToolSuccessScore = scores.DefaultIfEmpty(0).Average(),
            ToolEfficiencyScore = scores.DefaultIfEmpty(0).Average(),
            TaskCompletionScore = scores.DefaultIfEmpty(0).Average(),
            OverallScore = scores.DefaultIfEmpty(0).Average(),
            TestCaseCount = scenarios.Count,
            PassedCount = passedCount,
            Performance = perfSummary,
            TestCaseResults = testCaseResults,
            ParameterProfile = parameterProfile
        };
    }
}
