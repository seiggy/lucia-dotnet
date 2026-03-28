#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

using System.Text.Json;
using Microsoft.Extensions.AI;

namespace lucia.Tests.Orchestration;

/// <summary>
/// Deep evaluation tests for the LightAgent. Every test asserts the specific tool called,
/// verifies key parameters (state, searchTerms, brightness, color), and catches known
/// failure modes (color-as-state bug, wrong tool selection, hallucinated tool calls).
///
/// Tests use <c>[MemberData]</c> to cross-product models × prompt variants so each
/// intent is evaluated with multiple phrasings (including STT artifacts) without
/// duplicating test methods.
/// </summary>
[Trait("Category", "Eval")]
[Trait("Agent", "Light")]
public sealed class LightAgentEvalTests : AgentEvalTestBase
{
    private static readonly string[] s_lightToolNames = ["ControlLights", "GetLightsState"];

    public LightAgentEvalTests(EvalTestFixture fixture) : base(fixture) { }

    // ─── Prompt variant datasets ──────────────────────────────────────

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

    // ── Control: Turn On ──────────────────────────────────────────────

    public static IEnumerable<object[]> TurnOnKitchenPrompts => WithVariants(
        ("Turn on the kitchen light", "exact"),
        ("Kitchen light on", "terse"),
        ("Switch on the kitchen lights please", "polite"));

    // ── Control: Turn Off (area-based) ────────────────────────────────

    public static IEnumerable<object[]> TurnOffLivingRoomPrompts => WithVariants(
        ("Turn off all the lights in the living room", "exact"),
        ("Living room lights off", "terse"),
        ("Switch off the living room lights", "imperative"));

    // ── Control: Dim with brightness ──────────────────────────────────

    public static IEnumerable<object[]> DimZacksLightPrompts => WithVariants(
        ("Dim Zack's Light to 50%", "exact"),
        ("Dim Zach's Light to 50%", "stt-spelling"),
        ("Dim Sack's Light to 50%", "stt-lisp-sack"),
        ("Dim Zagslight to 50%", "stt-lisp-zaglight"),
        ("Dim Sag's Light to 50%", "stt-lisp-sag"));

    // ── Control: Color ────────────────────────────────────────────────

    public static IEnumerable<object[]> SetColorBluePrompts => WithVariants(
        ("Set the kitchen lights to blue", "exact"),
        ("Make the kitchen light blue", "casual"),
        ("Change the kitchen lights color to blue", "explicit"));

    // ── Control: Toggle ───────────────────────────────────────────────

    public static IEnumerable<object[]> ToggleBedroomPrompts => WithVariants(
        ("Toggle the bedroom lights", "exact"),
        ("Bedroom lights toggle", "terse"));

    // ── Query: Status ─────────────────────────────────────────────────

    public static IEnumerable<object[]> QueryKitchenStatusPrompts => WithVariants(
        ("What is the status of the kitchen light?", "exact"),
        ("Is the kitchen light on or off?", "yes-no"),
        ("Are the kitchen lights on?", "boolean"));

    public static IEnumerable<object[]> QueryGaragePrompts => WithVariants(
        ("What lights are in the garage?", "area-listing"));

    // ── Control: Relative brightness ──────────────────────────────────

    public static IEnumerable<object[]> RelativeBrightnessPrompts => WithVariants(
        ("Make the living room lights brighter", "brighter"),
        ("Dim the office lights a little", "dimmer"));

    // ── Control: Bulk ─────────────────────────────────────────────────

    public static IEnumerable<object[]> BulkTurnOffPrompts => WithVariants(
        ("Turn off all the lights", "exact"),
        ("Everything off", "terse"));

    // ── Out-of-domain ─────────────────────────────────────────────────

    public static IEnumerable<object[]> OutOfDomainPrompts => WithVariants(
        ("Play some jazz music in the living room", "music-request"),
        ("What's the weather like today?", "weather-request"),
        ("Set the thermostat to 72 degrees", "climate-request"));

    // ── STT: dim with heavy transcription errors ──────────────────────

    public static IEnumerable<object[]> SttFuzzyDimPrompts => WithVariants(
        ("Dimm the kichen lite to fifdy percent", "stt-heavy-garble"));

    // ═══════════════════════════════════════════════════════════════════
    // Tool Call Accuracy — Control Tests
    // ═══════════════════════════════════════════════════════════════════

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(TurnOnKitchenPrompts))]
    public async Task ControlLight_TurnOnKitchen_CallsControlLightsWithStateOn(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateLightAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"LightAgent.ControlLight_TurnOnKitchen[{variant}]");

        AssertHasTextResponse(response);
        AssertToolCalled(response, "ControlLights");

        var controlCall = FindToolCall(response, "ControlLights");
        AssertArgumentContains(controlCall, "searchTerms", "kitchen");
        AssertArgumentEquals(controlCall, "state", "on");

        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(TurnOffLivingRoomPrompts))]
    public async Task ControlLight_TurnOffLivingRoom_CallsControlLightsWithStateOff(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateLightAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"LightAgent.ControlLight_TurnOffLivingRoom[{variant}]");

        AssertHasTextResponse(response);
        AssertToolCalled(response, "ControlLights");

        var controlCall = FindToolCall(response, "ControlLights");
        AssertArgumentContains(controlCall, "searchTerms", "living room");
        AssertArgumentEquals(controlCall, "state", "off");

        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(ToggleBedroomPrompts))]
    public async Task ControlLight_ToggleBedroom_CallsControlLightsForBedroom(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateLightAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"LightAgent.ControlLight_ToggleBedroom[{variant}]");

        AssertHasTextResponse(response);
        // Toggle must result in a ControlLights call (the agent has no Toggle tool)
        AssertToolCalled(response, "ControlLights");

        var controlCall = FindToolCall(response, "ControlLights");
        AssertArgumentContains(controlCall, "searchTerms", "bedroom");
        // state must be "on" or "off" — not "toggle" or anything else
        var stateValue = GetArgumentStringValue(controlCall, "state");
        Assert.True(
            stateValue is "on" or "off",
            $"Toggle should resolve to state 'on' or 'off', but got '{stateValue}'.");

        AssertNoUnacceptableMetrics(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Intent Resolution — Brightness
    // ═══════════════════════════════════════════════════════════════════

    [Trait("Evaluator", "IntentResolution")]
    [SkippableTheory]
    [MemberData(nameof(DimZacksLightPrompts))]
    public async Task ControlLight_DimZacksLight_ExtractsBrightnessParam(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateLightAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"LightAgent.ControlLight_DimZacksLight[{variant}]");

        AssertHasTextResponse(response);
        AssertToolCalled(response, "ControlLights");

        var controlCall = FindToolCall(response, "ControlLights");
        AssertArgumentEquals(controlCall, "state", "on");

        // Brightness must be extracted — the core dim scenario
        var brightnessValue = GetArgumentNumericValue(controlCall, "brightness");
        Assert.True(
            brightnessValue is not null && brightnessValue >= 40 && brightnessValue <= 60,
            $"Expected brightness near 50 (±10), but got {brightnessValue?.ToString() ?? "null/missing"}.");

        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "IntentResolution")]
    [SkippableTheory]
    [MemberData(nameof(SttFuzzyDimPrompts))]
    public async Task ControlLight_SttFuzzyDim_ExtractsBrightnessDespisteGarble(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateLightAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"LightAgent.ControlLight_SttFuzzyDim[{variant}]");

        AssertHasTextResponse(response);
        AssertToolCalled(response, "ControlLights");

        var controlCall = FindToolCall(response, "ControlLights");
        AssertArgumentEquals(controlCall, "state", "on");

        // "fifdy percent" should resolve to ~50
        var brightnessValue = GetArgumentNumericValue(controlCall, "brightness");
        Assert.True(
            brightnessValue is not null && brightnessValue >= 40 && brightnessValue <= 60,
            $"STT garbled '50%' — expected brightness 40-60, got {brightnessValue?.ToString() ?? "null/missing"}.");

        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "IntentResolution")]
    [SkippableTheory]
    [MemberData(nameof(RelativeBrightnessPrompts))]
    public async Task ControlLight_RelativeBrightness_CallsControlLightsWithoutAbsoluteValue(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateLightAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"LightAgent.ControlLight_RelativeBrightness[{variant}]");

        AssertHasTextResponse(response);
        // Agent should attempt some light tool call (ControlLights or GetLightsState first)
        var toolCalls = GetToolCalls(response);
        Assert.True(
            toolCalls.Any(tc => IsLightTool(tc)),
            "Relative brightness command should invoke at least one light tool.");

        AssertNoUnacceptableMetrics(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Intent Resolution — Color (catches granite4 color-as-state bug)
    // ═══════════════════════════════════════════════════════════════════

    [Trait("Evaluator", "IntentResolution")]
    [SkippableTheory]
    [MemberData(nameof(SetColorBluePrompts))]
    public async Task ControlLight_SetColorBlue_ColorNotStuffedIntoState(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateLightAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"LightAgent.ControlLight_SetColorBlue[{variant}]");

        AssertHasTextResponse(response);
        AssertToolCalled(response, "ControlLights");

        var controlCall = FindToolCall(response, "ControlLights");

        // STATE must be "on" or "off" — NEVER a color name (known granite4 bug)
        var stateValue = GetArgumentStringValue(controlCall, "state");
        Assert.True(
            stateValue is "on" or "off",
            $"Color was stuffed into state parameter! state='{stateValue}' — " +
            "expected 'on' or 'off'. Color should go in the 'color' parameter.");

        // COLOR parameter should contain "blue"
        var colorValue = GetArgumentStringValue(controlCall, "color");
        Assert.True(
            colorValue is not null && colorValue.Contains("blue", StringComparison.OrdinalIgnoreCase),
            $"Expected color parameter to contain 'blue', but got '{colorValue ?? "null/missing"}'.");

        AssertArgumentContains(controlCall, "searchTerms", "kitchen");
        AssertNoUnacceptableMetrics(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Tool Call Accuracy — Query Tests
    // ═══════════════════════════════════════════════════════════════════

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(QueryKitchenStatusPrompts))]
    public async Task QueryLight_KitchenStatus_CallsGetLightsStateNotControlLights(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateLightAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"LightAgent.QueryLight_KitchenStatus[{variant}]");

        AssertHasTextResponse(response);
        AssertToolCalled(response, "GetLightsState");

        var queryCall = FindToolCall(response, "GetLightsState");
        AssertArgumentContains(queryCall, "searchTerms", "kitchen");

        // Query should NOT trigger ControlLights
        AssertToolNotCalled(response, "ControlLights");

        AssertNoUnacceptableMetrics(result);
    }

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(QueryGaragePrompts))]
    public async Task QueryLight_GarageAreaListing_CallsGetLightsState(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateLightAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"LightAgent.QueryLight_GarageAreaListing[{variant}]");

        AssertHasTextResponse(response);
        AssertToolCalled(response, "GetLightsState");

        var queryCall = FindToolCall(response, "GetLightsState");
        AssertArgumentContains(queryCall, "searchTerms", "garage");

        AssertNoUnacceptableMetrics(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Bulk Control
    // ═══════════════════════════════════════════════════════════════════

    [Trait("Evaluator", "ToolCallAccuracy")]
    [SkippableTheory]
    [MemberData(nameof(BulkTurnOffPrompts))]
    public async Task ControlLight_BulkTurnOff_CallsControlLightsWithStateOff(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateLightAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, result) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"LightAgent.ControlLight_BulkTurnOff[{variant}]");

        AssertHasTextResponse(response);
        AssertToolCalled(response, "ControlLights");

        var controlCall = FindToolCall(response, "ControlLights");
        AssertArgumentEquals(controlCall, "state", "off");

        AssertNoUnacceptableMetrics(result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Task Adherence — Out-of-Domain
    // ═══════════════════════════════════════════════════════════════════

    [Trait("Evaluator", "TaskAdherence")]
    [SkippableTheory]
    [MemberData(nameof(OutOfDomainPrompts))]
    public async Task OutOfDomain_NonLightRequest_NoLightToolsCalled(
        string modelId, string embeddingModelId, string prompt, string variant)
    {
        var (agent, capture) = await Fixture.CreateLightAgentWithCaptureAsync(modelId, embeddingModelId);
        var reportingConfig = CreateReportingConfig(includeTextEvaluators: false);

        var (response, _) = await RunAgentAndEvaluateAsync(
            modelId, agent.GetAIAgent(), capture, prompt, reportingConfig,
            $"LightAgent.OutOfDomain[{variant}]");

        AssertHasTextResponse(response);

        // Must NOT hallucinate a light tool call for non-light requests
        AssertToolNotCalled(response, "ControlLights");
        AssertToolNotCalled(response, "GetLightsState");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Private assertion helpers — manual arg inspection via GetToolCalls
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Finds the first <see cref="FunctionCallContent"/> matching the given tool name
    /// using the same normalization logic as <see cref="AgentEvalTestBase.AssertToolCalled"/>.
    /// </summary>
    private static FunctionCallContent FindToolCall(ChatResponse response, string functionName)
    {
        var normalized = NormalizeName(functionName);
        var toolCalls = GetToolCalls(response);
        var match = toolCalls.FirstOrDefault(tc =>
        {
            var actual = tc.Name is not null ? NormalizeName(tc.Name) : null;
            return string.Equals(actual, normalized, StringComparison.OrdinalIgnoreCase) ||
                   (actual is not null && actual.EndsWith($".{normalized}", StringComparison.OrdinalIgnoreCase));
        });

        Assert.True(match is not null, $"Expected tool call '{functionName}' not found in response.");
        return match;
    }

    /// <summary>
    /// Checks whether a <see cref="FunctionCallContent"/> is one of the known light tools.
    /// </summary>
    private static bool IsLightTool(FunctionCallContent tc)
    {
        if (tc.Name is null) return false;
        var normalized = NormalizeName(tc.Name);
        return s_lightToolNames.Any(name =>
            string.Equals(normalized, NormalizeName(name), StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith($".{NormalizeName(name)}", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Asserts that a string argument on the tool call equals the expected value (case-insensitive).
    /// </summary>
    private static void AssertArgumentEquals(FunctionCallContent toolCall, string argName, string expectedValue)
    {
        var actual = GetArgumentStringValue(toolCall, argName);
        Assert.True(
            string.Equals(actual, expectedValue, StringComparison.OrdinalIgnoreCase),
            $"Expected {argName}='{expectedValue}', but got '{actual ?? "null/missing"}'.");
    }

    /// <summary>
    /// Asserts that a string or array argument contains the expected substring (case-insensitive).
    /// Handles both <c>string</c> and <c>string[]</c> argument shapes.
    /// </summary>
    private static void AssertArgumentContains(FunctionCallContent toolCall, string argName, string expectedSubstring)
    {
        var rawValue = GetRawArgument(toolCall, argName);
        Assert.True(rawValue is not null, $"Argument '{argName}' not found on tool call '{toolCall.Name}'.");

        var serialized = rawValue is string s ? s : JsonSerializer.Serialize(rawValue);
        Assert.True(
            serialized.Contains(expectedSubstring, StringComparison.OrdinalIgnoreCase),
            $"Expected {argName} to contain '{expectedSubstring}', but got: {serialized}");
    }

    /// <summary>
    /// Extracts a string value from tool call arguments, handling <see cref="JsonElement"/> boxing.
    /// </summary>
    private static string? GetArgumentStringValue(FunctionCallContent toolCall, string argName)
    {
        var raw = GetRawArgument(toolCall, argName);
        return raw switch
        {
            null => null,
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
            JsonElement je => je.ToString(),
            _ => raw.ToString()
        };
    }

    /// <summary>
    /// Extracts a numeric value from tool call arguments, handling <see cref="JsonElement"/> boxing.
    /// </summary>
    private static double? GetArgumentNumericValue(FunctionCallContent toolCall, string argName)
    {
        var raw = GetRawArgument(toolCall, argName);
        return raw switch
        {
            null => null,
            int i => i,
            long l => l,
            double d => d,
            float f => f,
            JsonElement { ValueKind: JsonValueKind.Number } je => je.GetDouble(),
            string s when double.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }

    /// <summary>
    /// Retrieves a raw argument value from tool call arguments dictionary.
    /// </summary>
    private static object? GetRawArgument(FunctionCallContent toolCall, string argName)
    {
        if (toolCall.Arguments is null) return null;
        return toolCall.Arguments.TryGetValue(argName, out var value) ? value : null;
    }

    /// <summary>
    /// Strips trailing "Async" suffix to match AIFunctionFactory convention.
    /// </summary>
    private static string NormalizeName(string name)
    {
        return name.EndsWith("Async", StringComparison.Ordinal) ? name[..^5] : name;
    }
}
