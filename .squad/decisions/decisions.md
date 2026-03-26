# Decision Archive

## Decision: Eval Infrastructure Bug Fixes & Assertion Enhancement

**Author:** Dallas (Eval Engineer)  
**Date:** 2025-03-27  
**Status:** Implemented  
**Decision Type:** Bug Fix + Infrastructure  

### Summary

Fixed a critical tool name mismatch bug that inflated eval failure rates by ~50%, added 5 new assertion helpers to AgentEvalTestBase, and created a ModelComparisonReporter for cross-model scoring.

### Problem

1. **Tool name mismatch (P0 bug):** `AIFunctionFactory.Create` strips the `Async` suffix from C# method names when creating tool definitions. The YAML scenarios and ScenarioValidator were comparing against the C# method name (e.g., `ControlLightsAsync`) instead of the actual tool name (`ControlLights`). Every control and query scenario was failing validation on tool name alone — a ~50% false failure rate.

2. **Weak assertion surface:** Tests only used `AssertHasTextResponse()` — verifying the agent said *something* but never *what tools it called* or *what arguments it passed*. The base class had `AssertToolCalled()` but it was unused, and there was no way to assert on arguments, entity resolution, or tool absence.

3. **No cross-model comparison:** Each model was tested in isolation with no way to compare scores or failure patterns across models.

### Changes

#### Files Modified
- `lucia.EvalHarness/Evaluation/ScenarioValidator.cs` — Added `NormalizeFunctionName()` to strip Async suffix before tool name comparison
- `lucia.EvalHarness/TestData/light-agent.yaml` — Fixed all 9 tool name references (removed Async suffix)
- `lucia.EvalHarness/Evaluation/TestScenario.cs` — Updated docstring for `ExpectedToolCall.Tool`
- `lucia.Tests/Orchestration/AgentEvalTestBase.cs` — Added 5 assertion helpers + private support methods

#### Files Created
- `lucia.Tests/Orchestration/ModelComparisonReporter.cs` — Markdown report generator with failure classification
- `lucia.Tests/Orchestration/FailureType.cs` — Failure type enum (WRONG_TOOL, WRONG_PARAMS, WRONG_ENTITY, NO_TOOL_CALL, HALLUCINATION)
- `lucia.Tests/Orchestration/ModelScenarioResult.cs` — Per-model per-scenario result data class

### Design Decisions

1. **Normalize in the validator, not just the YAML:** Both were fixed. The validator now normalizes both expected and actual names, making it resilient to future YAML written with either naming convention. Belt and suspenders.

2. **Assertion helpers on the base class, not a separate utility:** They access the same `NormalizeFunctionName()` and `GetToolCalls()` infrastructure already in `AgentEvalTestBase`. Adding a partial class or utility class would split logic unnecessarily.

3. **Failure classification heuristic order matters:** The classifier checks in priority order: NoToolCall → Hallucination → WrongTool → WrongEntity → WrongParams. This ensures the most specific diagnosis wins.

### Verification

- `dotnet build lucia-dotnet.slnx -v minimal` — 0 warnings, 0 errors
- All Async references removed from light-agent.yaml (verified with grep)

### Impact

- Eval accuracy for light agent scenarios should improve significantly (no more false negatives from name mismatch)
- Teams can now write stronger assertions in xUnit tests using the new helpers
- Model comparison reports enable data-driven model selection decisions

---

## Decision: Light Agent Eval Rewrite — Deep Assertions

**Author:** Lambert (QA / Eval Scenario Engineer)  
**Date:** 2026-03-26  
**Status:** Implemented  
**Decision Type:** Test Quality  

### Summary

Completely rewrote `lucia.Tests/Orchestration/LightAgentEvalTests.cs` to replace shallow `AssertHasTextResponse()`-only tests with deep assertions that verify specific tool calls, parameter values, and absence of hallucinated calls.

### What Changed

#### Before (7 tests, all shallow)
- Every test only called `AssertHasTextResponse()` and `AssertNoUnacceptableMetrics()`
- `AssertToolCalled()` existed in the base class but was never used
- No parameter verification at all — color-as-state bug would pass silently
- Out-of-domain test only checked for text response, not absence of tool calls

#### After (12 test methods, 30+ prompt variants, deep assertions)

| Test | Tool Asserted | Parameters Verified |
|------|--------------|---------------------|
| `ControlLight_TurnOnKitchen_CallsControlLightsWithStateOn` | ControlLights | state=on, searchTerms contains "kitchen" |
| `ControlLight_TurnOffLivingRoom_CallsControlLightsWithStateOff` | ControlLights | state=off, searchTerms contains "living room" |
| `ControlLight_ToggleBedroom_CallsControlLightsForBedroom` | ControlLights | state ∈ {on,off} (not "toggle"), searchTerms contains "bedroom" |
| `ControlLight_DimZacksLight_ExtractsBrightnessParam` | ControlLights | state=on, brightness ∈ [40,60] (near 50%) |
| `ControlLight_SttFuzzyDim_ExtractsBrightnessDespisteGarble` | ControlLights | state=on, brightness ∈ [40,60] despite garbled STT |
| `ControlLight_RelativeBrightness_CallsControlLightsWithoutAbsoluteValue` | Any light tool | At least one light tool invoked |
| `ControlLight_SetColorBlue_ColorNotStuffedIntoState` | ControlLights | state ∈ {on,off} (NOT "blue"), color contains "blue", searchTerms contains "kitchen" |
| `QueryLight_KitchenStatus_CallsGetLightsStateNotControlLights` | GetLightsState | searchTerms contains "kitchen", ControlLights NOT called |
| `QueryLight_GarageAreaListing_CallsGetLightsState` | GetLightsState | searchTerms contains "garage" |
| `ControlLight_BulkTurnOff_CallsControlLightsWithStateOff` | ControlLights | state=off |
| `OutOfDomain_NonLightRequest_NoLightToolsCalled` | NONE | Both ControlLights and GetLightsState NOT called |

### New Scenario Categories (from Ash's Pain Map)
- **Toggle semantics** — verifies agent resolves "toggle" to on/off
- **Bulk operations** — "turn off all the lights"
- **Relative brightness** — "make it brighter" (no absolute value)
- **STT heavy garble** — "Dimm the kichen lite to fifdy percent"
- **Color-as-state bug detector** — catches granite4's known failure mode
- **Query vs Control separation** — verifies status queries don't trigger ControlLights

### Implementation Notes
- Used existing helpers only: `AssertToolCalled`, `GetToolCalls`, `AssertHasTextResponse`, `AssertToolNotCalled` from `AgentEvalTestBase`
- Added private helpers for argument inspection (`FindToolCall`, `AssertArgumentEquals`, `AssertArgumentContains`, `GetArgumentStringValue`, `GetArgumentNumericValue`) — these operate on `FunctionCallContent.Arguments` dictionary
- Handles `JsonElement` boxing from the AI framework's serialization
- Does NOT depend on Dallas's new helpers (parallel work)

### Build Status

`dotnet build lucia.Tests/lucia.Tests.csproj -v minimal` — **0 errors, 0 warnings**

---

## Decision: Trace Data Integration into xUnit Test Suite

**Author:** Ash (Data Engineer)  
**Date:** 2026-03-26  
**Status:** Implemented  
**Relates to:** Light Agent Pain Map, Eval Harness Reports  

### Context

The eval harness has been running real model evaluations since 2026-03-21, accumulating 8 runs with 77 test executions across granite4:350m and gemma3:270m. This trace data contains actual model behavior — tool selections, parameter values, failure modes — but it was only accessible through markdown reports and raw JSON. The xUnit test suite had no way to consume this data for regression testing.

Additionally, 3 high-severity GitHub issues (#105, #103, #84) describe production failures from real users that no existing eval scenario covers.

### Decision

Convert eval trace data and GitHub issue reports into structured JSON test data files that the xUnit suite can consume via `[MemberData]` data-driven tests.

### What Was Created

| File | Purpose | Count |
|------|---------|-------|
| `lucia.Tests/TestData/light-agent-traces.json` | Trace-derived scenarios from eval runs | 22 scenarios |
| `lucia.Tests/TestData/light-agent-user-issues.json` | Scenarios from real GitHub issues | 6 scenarios |
| `lucia.Tests/Orchestration/TraceScenarioLoader.cs` | Static loader with filtering support | — |
| `lucia.Tests/Orchestration/TraceScenario.cs` | Trace scenario model | — |
| `lucia.Tests/Orchestration/UserIssueScenario.cs` | Issue scenario model | — |
| `lucia.Tests/Orchestration/TraceScenarioCollection.cs` | Deserialization root (traces) | — |
| `lucia.Tests/Orchestration/UserIssueScenarioCollection.cs` | Deserialization root (issues) | — |
| `lucia.Tests/Orchestration/TraceScenarioMetadata.cs` | Metadata header model | — |

### Failure Type Coverage

| Failure Type | Count | Example |
|-------------|-------|---------|
| CORRECT | 6 | Out-of-domain rejection, query state |
| NO_TOOL_CALL | 7 | gemma3:270m empty responses, granite4 color failure |
| WRONG_TOOL | 3 | GetLightsState instead of ControlLights |
| WRONG_PARAMS | 3 | Color in state field, brightness missing |
| STATE_ERROR | 2 | Reported wrong light state |
| WRONG_RESPONSE | 1 | "I can't dim" despite having the tool |
| ENTITY_MISMATCH | 5 | "front room" / "dining room" / German names (issues) |

### How to Use in Tests

```csharp
// All trace scenarios
[Theory]
[MemberData(nameof(TraceScenarioLoader.TraceScenarioData), MemberType = typeof(TraceScenarioLoader))]
public void TraceScenario_ShouldSelectCorrectTool(TraceScenario scenario)
{
    // Use scenario.Prompt, scenario.ExpectedTool, etc.
}

// Only regressions
[Theory]
[MemberData(nameof(TraceScenarioLoader.RegressionScenarios), MemberType = typeof(TraceScenarioLoader))]
public void RegressionScenario_ShouldNotRegress(TraceScenario scenario) { ... }

// By failure type
TraceScenarioLoader.TracesByFailureType("WRONG_TOOL")
TraceScenarioLoader.TracesByModel("granite4:350m")
TraceScenarioLoader.TracesByCategory("control")

// Issue scenarios
TraceScenarioLoader.IssuesByCategory("entity-resolution")
```

### Key Insights

1. **50% of reported failures are inflated** — the eval harness tool-name mismatch (`ControlLights` vs `ControlLightsAsync`) makes scores appear worse than actual behavior. The trace JSON differentiates between eval-infra bugs and real model failures.

2. **Entity resolution is the #1 production gap** — none of the 11 YAML eval scenarios test colloquial room names, area matching, or non-English input. All 3 high-severity user issues are entity resolution problems.

3. **gemma3:270m is not viable** — 0 tool calls on 9/11 scenarios. Useful only as a "minimum viable model" baseline to ensure the test framework handles total model failure gracefully.

### Next Steps

- [ ] Wire `TraceScenarioLoader` into actual xUnit test methods (Lambert's eval rewrite work)
- [ ] Add automation to regenerate trace JSON after each eval harness run
- [ ] Implement deduplication — scenarios from different runs of the same prompt should merge
- [ ] Add entity resolution scenarios to eval YAML (covers #105, #103, #84)
- [ ] Fix the Async suffix mismatch in eval harness to unblock accurate scoring
