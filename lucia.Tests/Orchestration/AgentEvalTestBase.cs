#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

using System.Diagnostics;
using System.Text.Json;
using FakeItEasy;
using lucia.Agents.Orchestration;
using lucia.Agents.Orchestration.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Microsoft.Extensions.Configuration;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Abstract base class for all agent evaluation tests. Provides shared helpers for
/// reporting configuration, model ID selection, and running real agent instances
/// with evaluation.
/// </summary>
[Collection(EvalTestCollection.Name)]
public abstract class AgentEvalTestBase
{
    protected EvalTestFixture Fixture { get; }

    protected AgentEvalTestBase(EvalTestFixture fixture)
    {
        Fixture = fixture;
    }

    // ─── Model parameterization ───────────────────────────────────────

    /// <summary>
    /// Returns deployment IDs for <c>[MemberData]</c> parameterization.
    /// Sourced from the <c>EvalConfiguration.Models</c> array in <c>appsettings.json</c>.
    /// </summary>
    public static IEnumerable<object[]> ModelIds
    {
        get
        {
            // Static context — we need to build config independently here since
            // xUnit calls this before the fixture is constructed.
            var configBuilder = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables();

            var configRoot = configBuilder.Build();
            var config = new EvalConfiguration();
            configRoot.GetSection("EvalConfiguration").Bind(config);

            if (config.Models.Count == 0)
            {
                // Fallback to a single default model
                return [["gpt-4o"]];
            }

            return config.Models.Select(m => new object[] { m.DeploymentName });
        }
    }

    // ─── Reporting configuration ──────────────────────────────────────

    /// <summary>
    /// Stable execution name computed once per test run so all scenarios
    /// land in the same report.
    /// </summary>
    private static readonly string s_executionName =
        $"{DateTime.Now:yyyyMMddTHHmmss}";

    /// <summary>
    /// Creates a <see cref="ReportingConfiguration"/> with quality evaluators and
    /// optional custom evaluators. Reports are stored on disk for <c>dotnet aieval report</c>.
    /// </summary>
    /// <param name="includeTextEvaluators">
    /// When <c>true</c> (default), includes <see cref="RelevanceEvaluator"/> and
    /// <see cref="CoherenceEvaluator"/>.
    /// Set to <c>false</c> for scenarios where the model response
    /// contains no text content (e.g. raw routing JSON).
    /// </param>
    /// <param name="includeToolEvaluators">
    /// When <c>true</c> (default), includes <see cref="ToolCallAccuracyEvaluator"/> and
    /// <see cref="TaskAdherenceEvaluator"/>. Set to <c>false</c> for orchestrator-level
    /// tests where intermediate tool calls are not captured.
    /// </param>
    /// <param name="additionalEvaluators">Extra evaluators to include.</param>
    protected ReportingConfiguration CreateReportingConfig(
        bool includeTextEvaluators = true,
        bool includeToolEvaluators = true,
        params IEvaluator[] additionalEvaluators)
    {
        var evaluators = new List<IEvaluator>();

        if (includeTextEvaluators)
        {
            evaluators.Add(new RelevanceEvaluator());
            evaluators.Add(new CoherenceEvaluator());
        }

        if (includeToolEvaluators)
        {
            evaluators.Add(new ToolCallAccuracyEvaluator());
            evaluators.Add(new TaskAdherenceEvaluator());
        }

        evaluators.Add(new LatencyEvaluator());
        evaluators.AddRange(additionalEvaluators);

        var reportPath = Fixture.Configuration.ReportPath
            ?? Path.Combine(Path.GetTempPath(), "lucia-eval-reports");
        var executionName = Fixture.Configuration.ExecutionName
            ?? s_executionName;

        return DiskBasedReportingConfiguration.Create(
            storageRootPath: reportPath,
            evaluators: evaluators,
            chatConfiguration: Fixture.JudgeChatConfiguration,
            enableResponseCaching: false,
            executionName: executionName);
    }

    // ─── Helper: run real agent + evaluate ─────────────────────────────

    /// <summary>
    /// Runs a real <see cref="AIAgent"/> instance (e.g. <c>LightAgent</c>, <c>MusicAgent</c>)
    /// via <see cref="AIAgent.RunAsync"/>, which invokes the full <see cref="ChatClientAgent"/>
    /// pipeline including function invocation. The resulting <see cref="AgentResponse"/> is
    /// converted to a <see cref="ChatResponse"/> for evaluation.
    /// </summary>
    protected async Task<(ChatResponse Response, EvaluationResult Evaluation)> RunAgentAndEvaluateAsync(
        string deploymentName,
        AIAgent agent,
        string userMessage,
        ReportingConfiguration reportingConfig,
        string scenarioName,
        params string[] additionalTags)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, userMessage)
        };

        var stopwatch = Stopwatch.StartNew();
        var agentResponse = await agent.RunAsync(messages);
        stopwatch.Stop();

        // Convert AgentResponse → ChatResponse for the evaluation framework
        var chatResponse = new ChatResponse(agentResponse.Messages);

        var tags = new List<string> { deploymentName };
        tags.AddRange(additionalTags);

        await using var scenarioRun = await reportingConfig.CreateScenarioRunAsync(
            $"{scenarioName}[{deploymentName}]",
            additionalTags: tags);

        // Pass latency context to evaluators.
        // Note: AIAgent doesn't expose ChatOptions/Tools directly — the tool accuracy
        // evaluator will run without explicit tool context for agent-based tests.
        var latencyContext = new LatencyEvaluatorContext(stopwatch.Elapsed);

        var result = await scenarioRun.EvaluateAsync(messages, chatResponse, additionalContext: [latencyContext]);

        return (chatResponse, result);
    }

    /// <summary>
    /// Runs a real <see cref="AIAgent"/> with a <see cref="ChatHistoryCapture"/> that
    /// records intermediate tool calls. The captured messages — including
    /// <see cref="FunctionCallContent"/> items consumed by <see cref="FunctionInvokingChatClient"/>
    /// — are used to build the <see cref="ChatResponse"/> for evaluation, and tool definitions
    /// are passed as <see cref="ToolCallAccuracyEvaluatorContext"/> and
    /// <see cref="TaskAdherenceEvaluatorContext"/>.
    /// </summary>
    protected async Task<(ChatResponse Response, EvaluationResult Evaluation)> RunAgentAndEvaluateAsync(
        string deploymentName,
        AIAgent agent,
        ChatHistoryCapture capture,
        string userMessage,
        ReportingConfiguration reportingConfig,
        string scenarioName,
        params string[] additionalTags)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, userMessage)
        };

        capture.Reset();
        var stopwatch = Stopwatch.StartNew();
        var agentResponse = await agent.RunAsync(messages);
        stopwatch.Stop();

        // Build ChatResponse from captured history — includes intermediate tool calls
        // that are otherwise consumed by FunctionInvokingChatClient
        var chatResponse = new ChatResponse(capture.ResponseMessages);

        var tags = new List<string> { deploymentName };
        tags.AddRange(additionalTags);

        await using var scenarioRun = await reportingConfig.CreateScenarioRunAsync(
            $"{scenarioName}[{deploymentName}]",
            additionalTags: tags);

        // Build evaluator contexts with captured tool definitions and latency
        var contexts = new List<EvaluationContext> { new LatencyEvaluatorContext(stopwatch.Elapsed) };

        if (capture.ToolDefinitions.Count > 0)
        {
            contexts.Add(new ToolCallAccuracyEvaluatorContext(capture.ToolDefinitions));
            contexts.Add(new TaskAdherenceEvaluatorContext(capture.ToolDefinitions));
        }

        var result = await scenarioRun.EvaluateAsync(messages, chatResponse, additionalContext: contexts);

        return (chatResponse, result);
    }

    // ─── Helper: run real RouterExecutor + evaluate ────────────────────

    /// <summary>
    /// Runs the real <see cref="RouterExecutor"/> pipeline which sends the user message
    /// through structured JSON routing with the actual system prompt, agent catalog,
    /// confidence thresholds, and fallback logic. Returns both the typed
    /// <see cref="AgentChoiceResult"/> and the evaluation result.
    /// </summary>
    protected async Task<(AgentChoiceResult RoutingResult, ChatResponse Response, EvaluationResult Evaluation)> RunRouterAndEvaluateAsync(
        string deploymentName,
        RouterExecutor router,
        string userMessage,
        ReportingConfiguration reportingConfig,
        string scenarioName,
        params string[] additionalTags)
    {
        var inputMessage = new ChatMessage(ChatRole.User, userMessage);
        var mockContext = A.Fake<IWorkflowContext>();

        var stopwatch = Stopwatch.StartNew();
        var routingResult = await router.HandleAsync(inputMessage, mockContext);
        stopwatch.Stop();

        // Convert the routing result to a ChatResponse for evaluation
        var jsonText = JsonSerializer.Serialize(routingResult, RouterExecutor.JsonSerializerOptions);
        var chatResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, jsonText)]);

        var tags = new List<string> { deploymentName };
        tags.AddRange(additionalTags);

        await using var scenarioRun = await reportingConfig.CreateScenarioRunAsync(
            $"{scenarioName}[{deploymentName}]",
            additionalTags: tags);

        var latencyContext = new LatencyEvaluatorContext(stopwatch.Elapsed);

        var result = await scenarioRun.EvaluateAsync(
            [inputMessage], chatResponse, additionalContext: [latencyContext]);

        return (routingResult, chatResponse, result);
    }

    // ─── Helper: run full LuciaOrchestrator pipeline + evaluate ────────

    /// <summary>
    /// Runs the full <see cref="LuciaOrchestrator.ProcessRequestAsync"/> pipeline
    /// (Router → AgentDispatch → ResultAggregator) and evaluates the aggregated response.
    /// Intermediate routing decisions and per-agent responses are captured by the
    /// supplied <see cref="OrchestratorEvalObserver"/>.
    /// </summary>
    protected async Task<(ChatResponse Response, EvaluationResult Evaluation)> RunOrchestratorAndEvaluateAsync(
        string deploymentName,
        LuciaOrchestrator orchestrator,
        OrchestratorEvalObserver observer,
        string userMessage,
        ReportingConfiguration reportingConfig,
        string scenarioName,
        params string[] additionalTags)
    {
        return await RunOrchestratorAndEvaluateAsync(
            deploymentName, orchestrator, observer, userMessage,
            reportingConfig, scenarioName, expectedAgentIds: [], additionalTags: additionalTags);
    }

    /// <summary>
    /// Runs the full <see cref="LuciaOrchestrator.ProcessRequestAsync"/> pipeline
    /// and evaluates the aggregated response including A2A workflow validation.
    /// When <paramref name="expectedAgentIds"/> is non-empty, an
    /// <see cref="A2AToolCallEvaluatorContext"/> is supplied so the
    /// <see cref="A2AToolCallEvaluator"/> can validate routing, agent dispatch,
    /// execution success, and response aggregation.
    /// </summary>
    protected async Task<(ChatResponse Response, EvaluationResult Evaluation)> RunOrchestratorAndEvaluateAsync(
        string deploymentName,
        LuciaOrchestrator orchestrator,
        OrchestratorEvalObserver observer,
        string userMessage,
        ReportingConfiguration reportingConfig,
        string scenarioName,
        IEnumerable<string> expectedAgentIds,
        params string[] additionalTags)
    {
        var stopwatch = Stopwatch.StartNew();
        var resultText = await orchestrator.ProcessRequestAsync(
            userMessage,
            taskId: null,
            sessionId: null,
            cancellationToken: default);
        stopwatch.Stop();

        var chatResponse = new ChatResponse(
        [
            new ChatMessage(ChatRole.Assistant, resultText)
        ]);

        var tags = new List<string> { deploymentName };
        tags.AddRange(additionalTags);

        await using var scenarioRun = await reportingConfig.CreateScenarioRunAsync(
            $"{scenarioName}[{deploymentName}]",
            additionalTags: tags);

        var contexts = new List<EvaluationContext>
        {
            new LatencyEvaluatorContext(stopwatch.Elapsed)
        };

        var expectedIds = expectedAgentIds as ICollection<string> ?? expectedAgentIds.ToList();
        if (expectedIds.Count > 0)
        {
            contexts.Add(new A2AToolCallEvaluatorContext(observer, expectedIds));
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, userMessage)
        };

        var result = await scenarioRun.EvaluateAsync(
            messages, chatResponse, additionalContext: contexts);

        return (chatResponse, result);
    }

    // ─── Assertion helpers ────────────────────────────────────────────

    /// <summary>
    /// Extracts all <see cref="FunctionCallContent"/> items from a <see cref="ChatResponse"/>.
    /// </summary>
    protected static IReadOnlyList<FunctionCallContent> GetToolCalls(ChatResponse response)
    {
        return response.Messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .ToList();
    }

    /// <summary>
    /// Asserts that the response contains at least one tool call to the named function.
    /// Handles models that prefix function names (e.g., "functions.FindLight" for "FindLight")
    /// and normalizes away the <c>Async</c> suffix that <see cref="AIFunctionFactory.Create"/> strips.
    /// </summary>
    protected static void AssertToolCalled(ChatResponse response, string functionName)
    {
        // AIFunctionFactory.Create strips the "Async" suffix from method names,
        // so normalize both the expected name and actual tool call names.
        var normalized = NormalizeFunctionName(functionName);

        var toolCalls = GetToolCalls(response);
        Assert.Contains(toolCalls, tc =>
        {
            var actualName = tc.Name is not null ? NormalizeFunctionName(tc.Name) : null;
            return string.Equals(actualName, normalized, StringComparison.OrdinalIgnoreCase) ||
                   (actualName is not null && actualName.EndsWith($".{normalized}", StringComparison.OrdinalIgnoreCase));
        });
    }

    /// <summary>
    /// Strips a trailing "Async" suffix to match the convention used by
    /// <see cref="AIFunctionFactory.Create"/>.
    /// </summary>
    private static string NormalizeFunctionName(string name)
    {
        return name.EndsWith("Async", StringComparison.Ordinal)
            ? name[..^5]
            : name;
    }

    /// <summary>
    /// Asserts that the response contains a text reply (no tool calls only).
    /// </summary>
    protected static void AssertHasTextResponse(ChatResponse response)
    {
        var hasText = response.Messages
            .Any(m => m.Contents.OfType<TextContent>().Any(t => !string.IsNullOrWhiteSpace(t.Text)));
        Assert.True(hasText, "Expected at least one text response from the model.");
    }

    /// <summary>
    /// Asserts that no evaluation metrics were rated as <see cref="EvaluationRating.Unacceptable"/>.
    /// </summary>
    protected static void AssertNoUnacceptableMetrics(EvaluationResult result)
    {
        foreach (var metric in result.Metrics)
        {
            if (metric.Value.Interpretation is { Failed: true })
            {
                Assert.Fail(
                    $"Metric '{metric.Key}' failed with rating " +
                    $"'{metric.Value.Interpretation.Rating}': {metric.Value.Interpretation.Reason}");
            }
        }
    }

}
