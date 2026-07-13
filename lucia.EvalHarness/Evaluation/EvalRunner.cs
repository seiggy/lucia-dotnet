using AgentEval.Core;
using AgentEval.MAF;
using AgentEval.Metrics.Agentic;
using AgentEval.Models;
using lucia.Agents.Abstractions;
using lucia.EvalHarness.Configuration;
using lucia.EvalHarness.Providers;
using lucia.HomeAssistant.Services;
using Microsoft.Extensions.AI;

namespace lucia.EvalHarness.Evaluation;

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
    private readonly IChatClient? _judgeChatClient;
    private readonly PerformanceCollector _perfCollector = new();

    public EvalRunner(
        HarnessConfiguration config,
        IChatClient? judgeChatClient)
    {
        _config = config;
        _judgeChatClient = judgeChatClient;
    }

    internal Func<TestCase, int, CancellationToken,
        Task<(EvaluationContext Context, PerformanceSnapshot Performance)>>? TestContextFactory { get; init; }

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

        var metrics = CreateCodeMetrics(testCases);
        var judgeMetric = CreateJudgeMetric();
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

            EvaluationContext context;
            PerformanceSnapshot perf;
            string? agentOutput = null;
            IReadOnlyList<ToolCallTrace>? toolCalls = null;

            try
            {
                if (TestContextFactory is not null)
                {
                    (context, perf) = await TestContextFactory(tc, i, ct);
                }
                else
                {
                    var measured = await _perfCollector.MeasureAsync(async () =>
                        await harness.RunEvaluationAsync(evaluableAgent, tc, options, ct), ct);
                    var evalResult = measured.Result;
                    perf = measured.Perf;
                    agentOutput = evalResult.ActualOutput;
                    toolCalls = CaptureToolCalls(evalResult.ToolUsage);

                    context = new EvaluationContext
                    {
                        Input = tc.Input,
                        Output = evalResult.ActualOutput ?? string.Empty,
                        ToolUsage = evalResult.ToolUsage,
                        Performance = evalResult.Performance,
                        ExpectedTools = tc.ExpectedTools
                    };
                }

                perfSnapshots.Add(perf);
            }
            catch (Exception exception)
                when (JudgeAvailability.TryClassify(exception, ct, out var status))
            {
                testCaseResults.Add(new TestCaseResult
                {
                    TestCaseId = tc.Name ?? $"test_{i}",
                    Passed = false,
                    Score = null,
                    Latency = TimeSpan.Zero,
                    FailureReason = status == JudgeAvailability.Timeout
                        ? "Agent provider request timed out."
                        : "Agent provider request failed.",
                    Input = tc.Input
                });
                continue;
            }

            double selectionScore = 0;
            double successScore = 0;
            double efficiencyScore = 0;

            foreach (var metric in metrics)
            {
                var metricResult = await metric.EvaluateAsync(context, ct);
                switch (metric.Name)
                {
                    case "code_tool_selection":
                        selectionScore = metricResult.Score;
                        metricScores["tool_selection"].Add(selectionScore);
                        break;
                    case "code_tool_success":
                        successScore = metricResult.Score;
                        metricScores["tool_success"].Add(successScore);
                        break;
                    case "code_tool_efficiency":
                        efficiencyScore = metricResult.Score;
                        metricScores["tool_efficiency"].Add(efficiencyScore);
                        break;
                }
            }

            double? completionScore = null;
            string? judgeStatus = null;
            string? judgeReason = null;

            if (judgeMetric is null)
            {
                judgeStatus = JudgeAvailability.NotConfigured;
                judgeReason = JudgeAvailability.Reason(judgeStatus);
            }
            else
            {
                try
                {
                    var judgeResult = await judgeMetric.EvaluateAsync(context, ct);
                    completionScore = judgeResult.Score;
                    metricScores["task_completion"].Add(judgeResult.Score);
                }
                catch (Exception exception)
                    when (JudgeAvailability.TryClassify(exception, ct, out var status))
                {
                    judgeStatus = status;
                    judgeReason = JudgeAvailability.Reason(status);
                }
            }

            var availableScores = new List<double>
            {
                selectionScore,
                successScore,
                efficiencyScore
            };
            if (completionScore.HasValue)
            {
                availableScores.Add(completionScore.Value);
            }

            var averageScore = availableScores.Average();
            testCaseResults.Add(new TestCaseResult
            {
                TestCaseId = tc.Name ?? $"test_{i}",
                Passed = averageScore >= 70,
                Score = averageScore,
                Latency = perf.TotalDuration,
                Input = tc.Input,
                AgentOutput = agentOutput ?? context.Output,
                ToolCalls = toolCalls ?? CaptureToolCalls(context.ToolUsage),
                ConversationHistory = agentInstance.Tracer?.Turns.ToList(),
                JudgeStatus = judgeStatus,
                JudgeReason = judgeReason
            });
        }

        var perfSummary = ModelPerformanceSummary.FromSnapshots(modelName, perfSnapshots);

        var allMetricScores = metricScores.Values.SelectMany(values => values).ToList();
        var taskCompletionStatus = AggregateJudgeStatus(testCaseResults, metricScores["task_completion"].Count);

        return new ModelEvalResult
        {
            ModelName = modelName,
            AgentName = agentInstance.AgentName,
            ToolSelectionScore = Average(metricScores["tool_selection"]),
            ToolSuccessScore = Average(metricScores["tool_success"]),
            ToolEfficiencyScore = Average(metricScores["tool_efficiency"]),
            TaskCompletionScore = AverageOrNull(metricScores["task_completion"]),
            TaskCompletionStatus = taskCompletionStatus,
            TaskCompletionReason = taskCompletionStatus is null
                ? null
                : JudgeAvailability.Reason(taskCompletionStatus),
            OverallScore = allMetricScores.Count > 0 ? allMetricScores.Average() : null,
            OverallScoreStatus = allMetricScores.Count > 0 ? null : JudgeAvailability.Unavailable,
            OverallScoreReason = allMetricScores.Count > 0
                ? null
                : JudgeAvailability.Reason(JudgeAvailability.Unavailable),
            TestCaseCount = testCases.Count,
            PassedCount = testCaseResults.Count(r => r.Passed),
            Performance = perfSummary,
            TestCaseResults = testCaseResults,
            ParameterProfile = parameterProfile
        };
    }

    private static IReadOnlyList<IAgenticMetric> CreateCodeMetrics(IReadOnlyList<TestCase> testCases)
    {
        // Collect expected tools from all test cases for the selection metric
        var allExpectedTools = testCases
            .Where(tc => tc.ExpectedTools is { Count: > 0 })
            .SelectMany(tc => tc.ExpectedTools!)
            .Distinct()
            .ToList();

        return
        [
            new ToolSelectionMetric(allExpectedTools),
            new ToolSuccessMetric(),
            new ToolEfficiencyMetric()
        ];
    }

    private static double Average(List<double> values) =>
        values.Count > 0 ? values.Average() : 0;

    private IAgenticMetric? CreateJudgeMetric() =>
        _judgeChatClient is null
            ? null
            : new TaskCompletionMetric(new ValidatingJudgeChatClient(_judgeChatClient));

    private static double? AverageOrNull(List<double> values) =>
        values.Count > 0 ? values.Average() : null;

    private static string? AggregateJudgeStatus(
        IReadOnlyList<TestCaseResult> results,
        int availableCount)
    {
        var unavailable = results
            .Select(result => result.JudgeStatus)
            .Where(status => status is not null)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (unavailable.Count == 0)
        {
            return null;
        }

        if (availableCount > 0 || unavailable.Count > 1)
        {
            return JudgeAvailability.Partial;
        }

        return unavailable[0];
    }

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
        IEntityLocationService? locationService = null,
        ModelParameterProfile? parameterProfile = null,
        Action<string>? onProgress = null,
        CancellationToken ct = default)
    {
        // Scenario evaluation requires conversation tracing for tool call validation.
        // Without a tracer, the conversation list is empty and every scenario reports
        // "Expected N tool call(s) but only got 0" — a silent false-failure.
        if (agentInstance.Tracer is null && scenarios.Any(s => s.ExpectedToolCalls.Count > 0))
        {
            throw new InvalidOperationException(
                "Scenario evaluation requires conversation tracing (RealAgentFactory.EnableTracing = true) " +
                "to validate tool calls. Enable tracing before creating agent instances.");
        }

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
                await ScenarioValidator.SetupInitialStateAsync(haClient, scenario, locationService);

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
        var scores = testCaseResults
            .Where(result => result.Score.HasValue)
            .Select(result => result.Score!.Value)
            .ToList();
        var aggregateScore = scores.Count > 0 ? scores.Average() : (double?)null;

        return new ModelEvalResult
        {
            ModelName = modelName,
            AgentName = agentInstance.AgentName,
            ToolSelectionScore = aggregateScore ?? 0,
            ToolSuccessScore = aggregateScore ?? 0,
            ToolEfficiencyScore = aggregateScore ?? 0,
            TaskCompletionScore = aggregateScore,
            TaskCompletionStatus = aggregateScore.HasValue ? null : JudgeAvailability.Unavailable,
            TaskCompletionReason = aggregateScore.HasValue
                ? null
                : JudgeAvailability.Reason(JudgeAvailability.Unavailable),
            OverallScore = aggregateScore,
            OverallScoreStatus = aggregateScore.HasValue ? null : JudgeAvailability.Unavailable,
            OverallScoreReason = aggregateScore.HasValue
                ? null
                : JudgeAvailability.Reason(JudgeAvailability.Unavailable),
            TestCaseCount = scenarios.Count,
            PassedCount = passedCount,
            Performance = perfSummary,
            TestCaseResults = testCaseResults,
            ParameterProfile = parameterProfile
        };
    }
}
