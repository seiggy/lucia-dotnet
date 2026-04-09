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


### 2026-03-26: Conversation Fast-Path Refactor Test Suite

**Parker/Dallas Are Already Shipping — Adapt, Don't Stub**
- When I started, the `ConversationCommandProcessor` constructor only took 9 params. By the time I built, it had 11 (adding `IOptionsMonitor<CommandRoutingOptions>` and `IPersonalityResponseRenderer?`).
- The `IEntityLocationService` interface already had `ExactMatchEntities`, `ExactMatchArea`, and `IsCacheReady` methods with real implementations in `InMemoryEntityLocationService`.
- Lesson: Always `dotnet build` before writing any test to catch in-flight constructor changes from parallel dev.

**Personality Pipeline Is Real, Not Theoretical**
- `IPersonalityResponseRenderer` interface already exists at `lucia.AgentHost/Conversation/Templates/IPersonalityResponseRenderer.cs`.
- `CommandRoutingOptions` has `UsePersonalityResponses`, `PersonalityPrompt`, and `PersonalityModelConnectionName`.
- The processor's `HandleCommandMatchAsync` already has the `if (opts.UsePersonalityResponses && _personalityRenderer is not null)` guard.
- All 4 personality tests compile and run against the REAL pipeline — no stubs needed.

**MeterListener Cross-Test Interference Is Real**
- `MeterListener` picks up events from ALL `Meter` instances with the same instrument name across parallel tests.
- Fix: Scope the listener to the test's specific `Meter` instance via `instrument.Meter == meter` check.
- The existing `ConversationCommandProcessorTests.ProcessAsync_RecordsTelemetry_ForCommandPath` has this bug and fails intermittently.

**Pre-Existing Test Breakage From In-Flight Changes**
- `DirectSkillExecutorTests` has 2 failures from Dallas/Parker's entity cache changes (error message changed from "No executor registered" to "Entity location cache not loaded").
- These are NOT regressions from my work — they were broken before I started.

**Test Categorization Makes Intent Explicit**
- 5 test files × 1 class each (one class per file rule) across 5 behavioral categories.
- 10 tests run immediately (happy-path + existing telemetry), 13 skipped pending full implementation.
- Skip messages reference specific pending work ("by Parker/Dallas") so activation is self-documenting.

### 2025-10-13: Orchestrator Routing Coverage Expansion

**Bug-Driven Eval Design Is Most Effective**
- Real failure case ("turn off the lights in Zack's Office" → climate-agent @ 85%) became the regression test anchor
- Cross-domain confusion tests (light vs climate) are now explicitly asserted with `DoesNotContain` checks
- Room-specific light requests are separated into their own test category to catch similar mis-routings

**YAML Metadata Enables Dataset Slicing**
- Each scenario now has `metadata.category` (basic, room-specific, cross-domain, multi-agent, ambiguous, stt-variant)
- Each scenario has `metadata.difficulty` (easy, medium, hard)
- This allows filtering eval runs to specific failure modes or complexity levels
- Pattern matches light-agent.yaml and climate-agent.yaml metadata structure

**Negative Assertions Are Critical for Routing**
- Positive assertion: `Assert.Contains("light", observer.RoutingDecision.AgentId)`
- Negative assertion: `Assert.DoesNotContain("climate", observer.RoutingDecision.AgentId)`
- Both are required to prove the router chose the RIGHT agent and REJECTED wrong agents
- Without negative assertions, a multi-agent routing (light + climate) would pass a light-only test

**OrchestratorEvalObserver Is Perfectly Designed**
- Captures `RoutingDecision` (agent ID, confidence, reasoning, additional agents)
- Captures `AgentResponses` (per-agent execution results)
- Captures `AggregatedResponse` (final composed response)
- No changes needed — the observer was already comprehensive

**Multi-Agent Routing Uses AdditionalAgents Array**
- Primary agent goes in `RoutingDecision.AgentId`
- Secondary agents go in `RoutingDecision.AdditionalAgents`
- Test pattern: Combine both into `allAgents` list and assert ALL expected agents are present
- This pattern already existed in `RouteMultiAgent_LightAndMusic_RoutesToBoth`

**Coverage Gap: STT Variants and Ambiguous Cases**
- Only 4 STT variant scenarios added (lites, lamp, AC, temp)
- Only 4 ambiguous scenarios added (cozy, mood, goodnight, romantic)
- These are the hardest categories to get right — need more iteration
- Consider adding phonetic confusion tests (lights/lights, too/two, for/four)

**Test File Organization Follows Existing Pattern**
- All orchestrator tests in ONE file: `OrchestratorEvalTests.cs`
- Tests grouped by comment sections: Basic Routing, Cross-Domain, Scene Agent, Lists Agent, etc.
- Each test method follows EXACT pattern from existing tests (traits, observer, reporting, assertions)
- One class per file rule maintained

**Dataset Expansion: 7 → 41 Scenarios (486% growth)**
- Original: 7 scenarios (1 per agent type + 2 edge cases)
- Expanded: 41 scenarios across 6 categories
- Critical regression test explicitly documented with note field
- All scenarios follow the same structure for consistency

**File Paths for Orchestrator Tests**
- YAML dataset: `lucia.EvalHarness/TestData/orchestrator.yaml`
- Test class: `lucia.Tests/Orchestration/OrchestratorEvalTests.cs`
- Observer: `lucia.Tests/Orchestration/OrchestratorEvalObserver.cs` (no changes needed)
- Base class: `lucia.Tests/Orchestration/AgentEvalTestBase.cs` (no changes needed)

