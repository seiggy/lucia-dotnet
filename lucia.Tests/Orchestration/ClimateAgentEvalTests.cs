#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

namespace lucia.Tests.Orchestration;

/// <summary>
/// Evaluation tests for the ClimateAgent. Exercises the real <see cref="lucia.Agents.Agents.ClimateAgent"/>
/// code path — including <c>ChatClientAgent</c> with <c>FunctionInvokingChatClient</c> — so tools
/// are actually invoked against faked Home Assistant dependencies.
///
/// Tests use <c>[MemberData]</c> to cross-product models × prompt variants so each
/// intent is evaluated with multiple phrasings (including STT artifacts) without
/// duplicating test methods.
/// </summary>
[Trait("Category", "Eval")]
[Trait("Agent", "Climate")]
public sealed class ClimateAgentEvalTests : AgentEvalTestBase
{
    public ClimateAgentEvalTests(EvalTestFixture fixture) : base(fixture) { }

    // ─── Prompt variant datasets ──────────────────────────────────────

    /// <summary>
    /// Combines <see cref="AgentEvalTestBase.ModelIds"/> with an array of
    /// <c>(prompt, variantLabel)</c> tuples to produce
    /// <c>(modelId, embeddingModelId, prompt, variant)</c> rows for <c>[MemberData]</c>.
    /// </summary>
    private static IEnumerable<object[]> WithVariants(params (string Prompt, string Variant)[] variants)
    {
        foreach (var model in ModelIds)
        {
            var modelId = (string)model[0];
            var embeddingModelId = (string)model[1];
            foreach (var (prompt, variant) in variants)
            {
                yield return [modelId, embeddingModelId, prompt, variant];
            }
        }
    }

    public static IEnumerable<object[]> SetTemperaturePrompts => WithVariants(
        ("Set the thermostat to 72 degrees", "exact"),
        ("Set the thermostat to 72", "without-unit"),
        ("Set the thermometer to seventy two", "stt-thermometer"),
        ("Set the Therma stat to 72", "stt-therma-stat"));

    public static IEnumerable<object[]> SetHvacModePrompts => WithVariants(
        ("Turn on the heater", "heat-mode"),
        ("Turn on the AC", "cool-mode"),
        ("Turn off the thermostat", "off-mode"));

    public static IEnumerable<object[]> ComfortAdjustmentPrompts => WithVariants(
        ("I'm cold", "cold"),
        ("I'm hot", "hot"),
        ("It's too warm in here", "too-warm"),
        ("It's chilly", "chilly"));

    public static IEnumerable<object[]> GetClimateStatePrompts => WithVariants(
        ("What's the temperature in the living room?", "exact"),
        ("What's the current temperature?", "current"));

    public static IEnumerable<object[]> SetFanSpeedPrompts => WithVariants(
        ("Set the bedroom fan to 50%", "exact"),
        ("Set the bedroom fan to medium speed", "medium"));

    public static IEnumerable<object[]> MultiStepPrompts => WithVariants(
        ("Turn on the heat and set it to 70", "multi-step"));

    public static IEnumerable<object[]> OutOfDomainPrompts => WithVariants(
        ("Play some music in the living room", "music-request"),
        ("Turn on the kitchen lights", "light-request"));

    // ─── Tool Call Accuracy (via real agent execution) ─────────────────

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(SetTemperaturePrompts))]
    public async Task SetTemperature_TemperatureRequest_ProducesResponse(string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateClimateAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(
            includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            prompt,
            reportingConfig,
            $"ClimateAgent.SetTemperature_TemperatureRequest[{variant}]");

        AssertHasTextResponse(response);
        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(SetHvacModePrompts))]
    public async Task SetHvacMode_ModeRequest_ProducesResponse(string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateClimateAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(
            includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            prompt,
            reportingConfig,
            $"ClimateAgent.SetHvacMode_ModeRequest[{variant}]");

        AssertHasTextResponse(response);
        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(GetClimateStatePrompts))]
    public async Task GetClimateState_StatusQuery_ProducesResponse(string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateClimateAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(
            includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            prompt,
            reportingConfig,
            $"ClimateAgent.GetClimateState_StatusQuery[{variant}]");

        AssertHasTextResponse(response);
        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(SetFanSpeedPrompts))]
    public async Task SetFanSpeed_SpeedRequest_ProducesResponse(string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateClimateAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(
            includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            prompt,
            reportingConfig,
            $"ClimateAgent.SetFanSpeed_SpeedRequest[{variant}]");

        AssertHasTextResponse(response);
        AssertNoUnacceptableMetrics(result);
    }

    // ─── Intent Resolution ─────────────────────────────────────────────

    [Trait("Evaluator", "IntentResolution")]
    [SkippableTheory]
    [MemberData(nameof(ComfortAdjustmentPrompts))]
    public async Task ComfortAdjustment_NaturalLanguage_ProducesResponse(string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateClimateAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(
            includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            prompt,
            reportingConfig,
            $"ClimateAgent.ComfortAdjustment_NaturalLanguage[{variant}]");

        AssertHasTextResponse(response);
        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "IntentResolution")]
    [SkippableTheory]
    [MemberData(nameof(MultiStepPrompts))]
    public async Task MultiStep_CombinedRequest_ProducesResponse(string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateClimateAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(
            includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            prompt,
            reportingConfig,
            $"ClimateAgent.MultiStep_CombinedRequest[{variant}]");

        AssertHasTextResponse(response);
        AssertNoUnacceptableMetrics(result);
    }

    // ─── Task Adherence ────────────────────────────────────────────────

    [Trait("Evaluator", "TaskAdherence")]
    [SkippableTheory]
    [MemberData(nameof(OutOfDomainPrompts))]
    public async Task OutOfDomain_NonClimateRequest_StaysInDomain(string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateClimateAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(
            includeTextEvaluators: false);

        var (response, _) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            prompt,
            reportingConfig,
            $"ClimateAgent.OutOfDomain_NonClimateRequest[{variant}]");

        // Agent should have a text response politely declining the out-of-domain request
        AssertHasTextResponse(response);
    }
}
