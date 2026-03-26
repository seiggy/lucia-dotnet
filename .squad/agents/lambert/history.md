# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant
- **Stack:** .NET 10, C# 14, xUnit, AgentEval, Ollama, Azure OpenAI (judge)
- **Created:** 2026-03-26

## Existing Eval Patterns to Follow

- `lucia.Tests/Orchestration/LightAgentEvalTests.cs` — 10+ scenarios including STT variants, out-of-domain
- `lucia.Tests/Orchestration/MusicAgentEvalTests.cs` — play/stop/shuffle scenarios
- `lucia.Tests/Orchestration/OrchestratorEvalTests.cs` — routing tests across agents
- All extend `AgentEvalTestBase` which provides ModelIds, reporting, assertions
- YAML scenarios in `lucia.EvalHarness/TestData/` follow a consistent format

## Agents Needing Eval Suites

- ClimateAgent — temperature, HVAC mode, fan speed controls
- ListsAgent — shopping lists, todo management
- SceneAgent — scene activation, multi-device coordination
- GeneralAgent — catch-all queries, knowledge, fallback
- DynamicAgent — dynamic tool/skill registration

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-03-26: ClimateAgent Eval Suite Implementation

**Pattern Fidelity Is Critical**
- Following the exact structure of LightAgentEvalTests.cs was essential for consistency
- The pattern includes: sealed class, AgentEvalTestBase inheritance, MemberData cross-products, SkippableTheory attributes, proper trait organization
- Small deviations (like different assertion patterns) would break eval report aggregation

**EvalTestFixture Architecture Is Well-Designed**
- The fixture already had CreateClimateAgentWithCaptureAsync implemented (forward planning!)
- Configuration monitors for ClimateControlSkillOptions and FanControlSkillOptions were already in place
- Only missing piece was the `using lucia.Agents.Configuration.UserConfiguration;` statement
- This suggests fixture updates happen in parallel with agent development

**STT Variant Testing Strategy**
- Every command should have STT variants: "thermometer" for "thermostat", "seventy two" for "72"
- The WithVariants() helper makes it easy to cross-product prompts with model IDs
- STT artifacts are often the hardest edge cases for intent recognition

**Home Assistant Snapshot Limitations**
- The ha-snapshot.json is currently limited to lights and media_players
- Climate entities exist in the live HA instance but aren't in the snapshot
- Tests will run but won't validate against realistic climate entity data
- Solution: Re-export snapshot with `Export-HomeAssistantSnapshot.ps1`

**Error Scenarios Are Missing**
- Current eval suites focus on happy paths (valid temps, known rooms, supported modes)
- Error handling (invalid temps, unknown rooms, unsupported modes) is a gap across all agents
- This is acceptable for initial eval baseline but needs follow-up iteration

**YAML Dataset Quality**
- The climate-agent.yaml dataset is comprehensive and well-structured
- It covers: tool accuracy, intent resolution, parameter extraction, multi-step, out-of-domain
- Each scenario has clear expected_tools and criteria for evaluation
- Format is consistent with light-agent.yaml and music-agent.yaml

**Build Verification Is Essential**
- Always run `dotnet build lucia.Tests/lucia.Tests.csproj -v minimal` after creating eval tests
- Compilation errors (missing usings, wrong namespaces) are easier to catch early
- The build must succeed before considering the task complete

### 2026-03-26: Light Agent Eval Rewrite — Deep Assertions

**Shallow Tests Mask Real Failures**
- The original LightAgentEvalTests only called `AssertHasTextResponse()` — the granite4 color-as-state bug (putting "blue" into `state` instead of `color`) would pass every test silently
- `AssertToolCalled` and `GetToolCalls` existed in the base class but were never used — the helpers work perfectly once you actually call them

**FunctionCallContent.Arguments Uses JsonElement Boxing**
- When extracting arguments from tool calls, values arrive as `JsonElement` (not raw strings/ints) because the AI framework serializes them through System.Text.Json
- Must handle `JsonElement.ValueKind` checks (String, Number) alongside raw types for robust assertion helpers
- Pattern: `raw switch { string s => s, JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(), ... }`

**Ash's Pain Map Is Gold for Scenario Design**
- The data-driven failure taxonomy (wrong tool 40%, param extraction 25%, entity resolution 20%) directly maps to test categories
- The color-as-state bug (Category B) and GetLightsState-for-control bug (Category A) are the two highest-value assertions to add
- Out-of-domain tests must assert NO tool calls, not just "has text response" — hallucinated tool calls are a real failure mode

**Query vs Control Tool Separation Is Critical**
- Status queries ("is the kitchen light on?") must call `GetLightsState`, NOT `ControlLights`
- Control commands ("turn on the kitchen light") must call `ControlLights`, NOT `GetLightsState`
- Small models (granite4) consistently confuse these — the test must verify the exact tool, not just "any tool called"

**AssertToolNotCalled Already Exists in Base Class**
- AgentEvalTestBase has `AssertToolNotCalled` (added alongside `AssertToolCalled`) — don't duplicate it in derived test classes
- Always check the base class before adding new helpers — it's more complete than it appears from usage

