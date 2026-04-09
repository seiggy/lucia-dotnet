# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant
- **Stack:** .NET 10, C# 14, xUnit, Microsoft.Extensions.AI.Evaluation, AgentEval 0.6.0-beta, Ollama, Azure OpenAI
- **Created:** 2026-03-26

## Key Files I Own

- `lucia.EvalHarness/` — TUI eval runner, report generation, trace export
- `lucia.Tests/Orchestration/AgentEvalTestBase.cs` — shared eval test base
- `lucia.Tests/Orchestration/EvalTestFixture.cs` — fixture that builds real agents
- `lucia.Tests/Orchestration/EvalConfiguration.cs` — eval config model
- `lucia.EvalHarness/Providers/RealAgentFactory.cs` — agent construction for harness
- `lucia.EvalHarness/Providers/OllamaModelDiscovery.cs` — model discovery

## Current State

- EvalHarness works: TUI, YAML scenarios, metrics, HTML/MD/JSON reports, trace export
- xUnit eval tests exist for LightAgent, MusicAgent, Orchestrator
- AgentEvalTestBase provides ModelIds, reporting config, assertion helpers
- EvalTestFixture creates real agents with Ollama/Azure OpenAI backends
- Custom evaluators: SmartHomeToolCallEvaluator, A2AToolCallEvaluator, LatencyEvaluator

## Learnings

### 2025-01-26: Eval Infrastructure Multi-Agent Support

**What I learned:**
- EvalTestFixture and RealAgentFactory had partial agent coverage — both needed extension
- ClimateAgent requires both ClimateControlSkill and FanControlSkill (dual-skill agent pattern)
- DynamicAgent needs MongoDB definition at runtime — can be supported in harness but not xUnit parameterized tests
- ChatHistoryCapture is a generic middleware that works with all agents — no agent-specific changes needed
- All evaluators (SmartHomeToolCallEvaluator, A2AToolCallEvaluator, LatencyEvaluator) are agent-agnostic
- ScenarioValidator validates tool calls + entity state, works with any agent that calls HA tools

**Infrastructure patterns:**
- Agent factory methods follow consistent pattern: CreateXAgentAsync(deploymentName, embeddingModelName?)
- WithCapture variants insert ChatHistoryCapture in the pipeline for tool call inspection
- ExtractAgentCards() builds registry metadata for orchestrator testing
- Skills require IHomeAssistantClient, IEntityLocationService, IOptionsMonitor<SkillOptions>
- Climate/Fan skills also need IEmbeddingProviderResolver, IDeviceCacheService, IHybridEntityMatcher

**What I fixed:**
- Added ClimateAgent, ListsAgent, SceneAgent factories to EvalTestFixture (with/without capture)
- Added DynamicAgent factory to RealAgentFactory (requires agentId + MongoDB definition)
- Extended ExtractAgentCards() to support all 6 core agent types
- Added missing using directive for lucia.Agents.Configuration.UserConfiguration

**Build verification:**
- `dotnet build lucia-dotnet.slnx -v minimal` succeeded — no warnings or errors

**Next:** Lambert can now write eval test suites for Climate/Lists/Scene agents using the extended infrastructure.

### 2025-03-27: Eval Infrastructure Bug Fixes & Assertion Helpers

**What I found:**
- Critical bug: `AIFunctionFactory.Create` strips the `Async` suffix from C# method names when creating tool definitions (e.g., `ControlLightsAsync` → `ControlLights`). All 9 YAML scenario tool expectations in `light-agent.yaml` used the C# method name (`ControlLightsAsync`) instead of the tool name (`ControlLights`), causing ~50% false failure rate in ScenarioValidator.
- `LightControlSkill.SearchToolNames` already used the correct stripped names (`GetLightsState`, `ControlLights`), confirming the convention.
- `AgentEvalTestBase.AssertToolCalled()` already handled normalization, but the YAML-driven ScenarioValidator did not.
- xUnit tests only used `AssertHasTextResponse()` — never `AssertToolCalled()` or any argument-level assertions.

**What I fixed:**
1. **ScenarioValidator.cs**: Added `NormalizeFunctionName()` that strips `Async` suffix before tool name comparison. Both expected and actual names are normalized, so YAML written with either form matches correctly.
2. **light-agent.yaml**: Updated all 9 expected tool names from `ControlLightsAsync`→`ControlLights` and `GetLightsStateAsync`→`GetLightsState` for correctness.
3. **TestScenario.cs**: Updated `ExpectedToolCall.Tool` docstring to reflect correct naming convention.
4. **AgentEvalTestBase.cs**: Added 5 new assertion helpers:
   - `AssertToolCalledWithArgs()` — verify specific argument values with `*` and `contains:` matchers
   - `AssertToolNotCalled()` — verify a tool was NOT called (out-of-domain tests)
   - `GetToolCallArguments()` — extract arguments dict from a specific tool call
   - `AssertEntityResolved()` — verify searchTerms/entity contains expected entity pattern
   - `GetAllToolCalls()` — return all tool calls with names and arguments for inspection
5. **ModelComparisonReporter.cs** (new): Generates markdown comparison reports across models × scenarios with failure classification (WRONG_TOOL, WRONG_PARAMS, WRONG_ENTITY, NO_TOOL_CALL, HALLUCINATION).
6. **FailureType.cs** (new): Enum for failure classification.
7. **ModelScenarioResult.cs** (new): Data class for individual model×scenario results.

**Key pattern — AIFunctionFactory naming:**
- C# methods: `ControlLightsAsync`, `GetLightsStateAsync` (with Async suffix)
- Tool names after AIFunctionFactory: `ControlLights`, `GetLightsState` (stripped)
- YAML scenarios and validators must use the stripped form
- The `NormalizeFunctionName()` pattern is now in 3 places: AgentEvalTestBase, ScenarioValidator, ModelComparisonReporter

**Build verification:**
- `dotnet build lucia-dotnet.slnx -v minimal` — 0 warnings, 0 errors

### 2025-07-15: Personality-Rendered Fast-Path Responses

**What I implemented:**
- Opt-in personality response mode for the conversation fast-path pipeline. When `Wyoming:CommandRouting:UsePersonalityResponses` is `true` and a `PersonalityPrompt` is configured, canned template responses are rephrased through an LLM call using the personality prompt before returning to the user.

**Key files created/modified:**
1. `lucia.AgentHost/Conversation/Templates/IPersonalityResponseRenderer.cs` — new interface
2. `lucia.AgentHost/Conversation/Templates/PersonalityResponseRenderer.cs` — implementation using `IChatClientResolver` for LLM calls, with fallback to canned response on failure
3. `lucia.Wyoming/CommandRouting/CommandRoutingOptions.cs` — added `UsePersonalityResponses`, `PersonalityPrompt`, `PersonalityModelConnectionName`
4. `lucia.AgentHost/Conversation/ConversationCommandProcessor.cs` — injected `IPersonalityResponseRenderer?` (optional) and `IOptionsMonitor<CommandRoutingOptions>`; personality rendering applied after template rendering when enabled
5. `lucia.Tests/Conversation/PersonalityResponseTests.cs` — removed all `Skip` attributes, wired real mocks for the personality renderer

**Design decisions:**
- Personality renderer is optional DI injection (`= null` default) so existing test code only needs the `IOptionsMonitor<CommandRoutingOptions>` addition
- Fallback to canned response is handled both in the renderer (LLM failure) and in the processor (feature disabled / renderer not registered)
- The existing `PersonalityPromptOptions` in `lucia.Agents.Orchestration` is for orchestrator-level personality rewriting; this new feature is specifically for command fast-path responses
- `IChatClientResolver` is reused (not a new resolver) — same pattern as `ResultAggregatorExecutor.ApplyPersonalityAsync`

**Build verification:**
- `dotnet build lucia-dotnet.slnx -v minimal` — 0 warnings, 0 errors
- `dotnet test --filter PersonalityResponse` — 4/4 tests passed

### 2025-07-15: Climate Scenario Entity Resolution Fix

**What I found:**
- Climate eval scenarios all failed with "No climate devices available in the system" from `FindClimateDeviceAsync`.
- **Root cause 1:** `IEmbeddingProviderResolver` was faked to return null. `ClimateControlSkill.InitializeAsync` and `RefreshCacheAsync` both short-circuit when `_embeddingService is null`, so `_cachedDevices` was never populated.
- **Root cause 2:** `SnapshotEntityLocationService` was built only from the static HA snapshot file (which has zero climate entities). Even if the cache populated, the fallback search path through `SearchHierarchyAsync` would find nothing.
- **Root cause 3:** `FakeHomeAssistantClient.CallServiceAsync` had no climate service handlers, so `SetClimateTemperature` calls wouldn't update entity state for validation.

**What I fixed:**
1. `SnapshotEntityLocationService.RegisterEntity()` — new method for dynamic entity registration from YAML scenario `initial_state`
2. `FakeEmbeddingGenerator` — new TestDouble returning constant vectors so `RefreshCacheAsync` completes and populates `_cachedDevices` from `FakeHomeAssistantClient`
3. `RealAgentFactory.CreateClimateAgentAsync` — wired fake embedding resolver, set `CacheRefreshMinutes=0` so cache refreshes on every search (picks up entities injected after init)
4. `ScenarioValidator.SetupInitialStateAsync` — extended with optional `IEntityLocationService` param; registers scenario entities in both HA client and location service
5. `FakeHomeAssistantClient` — added climate.set_temperature, climate.set_hvac_mode, climate.set_fan_mode handlers
6. Threaded `EntityLocationService` through EvalRunner, ParameterSweepRunner, EvalProgressDisplay

**Key pattern — ClimateControlSkill search fallback:**
- Primary path: `_entityMatcher.FindMatchesAsync` using embeddings against `_cachedDevices`
- Fallback path: `_locationService.SearchHierarchyAsync` (substring match) intersected with `_cachedDevices`
- In eval with constant-value embeddings, primary path returns nothing → fallback kicks in → works via substring matching

**Build verification:**
- `dotnet build lucia.EvalHarness/lucia.EvalHarness.csproj --no-restore -v minimal` — 0 warnings, 0 errors

<!-- Append new learnings below. Each entry is something lasting about the project. -->
