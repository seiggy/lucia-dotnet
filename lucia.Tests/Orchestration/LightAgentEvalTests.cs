#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

namespace lucia.Tests.Orchestration;

/// <summary>
/// Evaluation tests for the LightAgent. Exercises the real <see cref="lucia.Agents.Agents.LightAgent"/>
/// code path — including <c>ChatClientAgent</c> with <c>FunctionInvokingChatClient</c> — so tools
/// are actually invoked against faked Home Assistant dependencies.
///
/// Tests use <c>[MemberData]</c> to cross-product models × prompt variants so each
/// intent is evaluated with multiple phrasings (including STT artifacts) without
/// duplicating test methods.
/// </summary>
[Trait("Category", "Eval")]
[Trait("Agent", "Light")]
public sealed class LightAgentEvalTests : AgentEvalTestBase
{
    public LightAgentEvalTests(EvalTestFixture fixture) : base(fixture) { }

    // ─── Prompt variant datasets ──────────────────────────────────────

    /// <summary>
    /// Combines <see cref="AgentEvalTestBase.ModelIds"/> with an array of
    /// <c>(prompt, variantLabel)</c> tuples to produce <c>(modelId, prompt, variant)</c>
    /// rows for <c>[MemberData]</c>.
    /// </summary>
    private static IEnumerable<object[]> WithVariants(params (string Prompt, string Variant)[] variants)
    {
        foreach (var model in ModelIds)
        {
            var modelId = (string)model[0];
            foreach (var (prompt, variant) in variants)
            {
                yield return [modelId, prompt, variant];
            }
        }
    }

    public static IEnumerable<object[]> FindLightPrompts => WithVariants(
        ("Turn on the kitchen light", "exact"));

    public static IEnumerable<object[]> FindLightsByAreaPrompts => WithVariants(
        ("Turn off all the lights in the living room", "exact"));

    public static IEnumerable<object[]> GetLightStatePrompts => WithVariants(
        ("What is the status of the hallway light?", "exact"));

    public static IEnumerable<object[]> DimLightPrompts => WithVariants(
        ("Dim Zack's Light to 50%", "exact"),
        ("Dim Zach's Light to 50%", "stt-spelling"),
        ("Dim Sack's Light to 50%", "stt-lisp-sack"),
        ("Dim Sag's Light to 50%", "stt-lisp-sag"));

    public static IEnumerable<object[]> SetColorPrompts => WithVariants(
        ("Set the kitchen lights to blue", "exact"));

    public static IEnumerable<object[]> OutOfDomainPrompts => WithVariants(
        ("Play some jazz music in the living room", "music-request"));

    // ─── Tool Call Accuracy (via real agent execution) ─────────────────

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(FindLightPrompts))]
    public async Task FindLight_SingleLight_ProducesResponse(string modelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateLightAgentWithCaptureAsync(modelId);
        var reportingConfig = CreateReportingConfig();

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            prompt,
            reportingConfig,
            $"LightAgent.FindLight_SingleLight[{variant}]");

        AssertHasTextResponse(response);
        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(FindLightsByAreaPrompts))]
    public async Task FindLightsByArea_AreaRequest_ProducesResponse(string modelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateLightAgentWithCaptureAsync(modelId);
        var reportingConfig = CreateReportingConfig();

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            prompt,
            reportingConfig,
            $"LightAgent.FindLightsByArea_AreaRequest[{variant}]");

        AssertHasTextResponse(response);
        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(GetLightStatePrompts))]
    public async Task GetLightState_StatusQuery_ProducesResponse(string modelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateLightAgentWithCaptureAsync(modelId);
        var reportingConfig = CreateReportingConfig();

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            prompt,
            reportingConfig,
            $"LightAgent.GetLightState_StatusQuery[{variant}]");

        AssertHasTextResponse(response);
        AssertNoUnacceptableMetrics(result);
    }

    // ─── Intent Resolution ─────────────────────────────────────────────

    [Trait("Evaluator", "IntentResolution")]
    [SkippableTheory]
    [MemberData(nameof(DimLightPrompts))]
    public async Task DimLight_ProducesResponse(string modelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateLightAgentWithCaptureAsync(modelId);
        var reportingConfig = CreateReportingConfig();

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            prompt,
            reportingConfig,
            $"LightAgent.DimLight[{variant}]");

        AssertHasTextResponse(response);
        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "IntentResolution")]
    [SkippableTheory]
    [MemberData(nameof(SetColorPrompts))]
    public async Task SetColor_IntentRecognized_ProducesResponse(string modelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateLightAgentWithCaptureAsync(modelId);
        var reportingConfig = CreateReportingConfig();

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            prompt,
            reportingConfig,
            $"LightAgent.SetColor_IntentRecognized[{variant}]");

        AssertHasTextResponse(response);
        AssertNoUnacceptableMetrics(result);
    }

    // ─── Task Adherence ────────────────────────────────────────────────

    [Trait("Evaluator", "TaskAdherence")]
    [SkippableTheory]
    [MemberData(nameof(OutOfDomainPrompts))]
    public async Task OutOfDomain_MusicRequest_StaysInDomain(string modelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateLightAgentWithCaptureAsync(modelId);
        var reportingConfig = CreateReportingConfig();

        var (response, _) = await RunAgentAndEvaluateAsync(
            modelId,
            agent.GetAIAgent(),
            capture,
            prompt,
            reportingConfig,
            $"LightAgent.OutOfDomain_MusicRequest[{variant}]");

        // Agent should have a text response politely declining the out-of-domain request
        AssertHasTextResponse(response);
    }
}
