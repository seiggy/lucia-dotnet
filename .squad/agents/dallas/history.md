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

### 2025-10-13: Agent Registry Bug in EvalTestFixture

**What I found:**
- Critical bug: `EvalTestFixture.CreateRouterExecutor()` and `CreateLuciaOrchestratorAsync()` only registered 3 agent cards (light, music, general) in the mock `IAgentRegistry`, despite extracting 6 cards total in `ExtractAgentCards()`.
- Missing cards: `_climateAgentCard`, `_listsAgentCard`, `_sceneAgentCard` were extracted but never passed to the router/orchestrator.
- This caused routing eval tests to have an incomplete view of the agent catalog — the router LLM couldn't see climate, lists, or scene as routing targets.
- **Real-world impact:** A "turn off the lights in Zack's Office" request was routed to climate-agent at 85% confidence. Routing tests were never catching these cross-domain routing bugs because the missing agents weren't available as targets.

**What I fixed:**
1. `CreateRouterExecutor()` (line 612): Changed `allAgents` list to include all 6 cards:
   ```csharp
   var allAgents = new List<AgentCard>
   {
       _lightAgentCard, _musicAgentCard, _generalAgentCard,
       _climateAgentCard, _listsAgentCard, _sceneAgentCard
   };
   ```
2. `CreateLuciaOrchestratorAsync()` (line 642): Changed `allCards` list to include all 6 cards with same format.
3. Added TODO comment for future work: Building real agent instances for climate, lists, and scene agents. Currently only light, music, and general agents have instances built. For routing-only tests, the cards are sufficient, but full-pipeline execution tests that invoke agents will need instances.

**Key pattern — Agent registry vs. agent provider:**
- `IAgentRegistry.GetAllAgentsAsync()` returns `AgentCard` collection — used by the router to build the catalog LLM sees
- `EvalAgentProvider` holds actual `AIAgent` instances — used by the invoker to execute selected agents
- **For routing tests:** Only the registry matters — the router decision is based on card metadata
- **For full-pipeline tests:** Both registry and provider must align — agents in the catalog must have corresponding instances

**Build verification:**
- `dotnet build lucia.Tests/lucia.Tests.csproj --no-restore` — 0 warnings, 0 errors

### 2026-05-29: EvalHarness Health Review (whole-solution review)

**Scope:** Read-only review of `lucia.EvalHarness/` for eval correctness, determinism, scoring, and resource health. Findings written to session `review-eval.md`. No code changed.

**Durable observations about the eval framework:**
- **Determinism is not actually achieved.** `Seed` is `null` in all built-in profiles (`ModelParameterProfile.Default/Precise/Creative`), in `ParameterSweepConfig.GenerateCombinations()`, and is never defaulted. Even "precise" runs are stochastic.
- **Inference knobs are injected wrong.** `ParameterInjectingChatClient` writes `seed`/`num_predict`/`repeat_penalty` into `ChatOptions.AdditionalProperties` (untyped string keys). M.E.AI → OllamaSharp/OpenAI adapters map the *typed* properties (`ChatOptions.Seed`, `MaxOutputTokens`, `FrequencyPenalty`), so these knobs likely never reach the backend. Needs end-to-end verification with a request capture. Also uses `??=` so agent-set Temperature/TopP win over the eval profile.
- **Parameter sweep is statistically invalid as written:** each combination evaluated once against unseeded output, then `OrderByDescending(AverageScore).First()` picks the winner — best-of-N noise.
- **Resource leak:** `RealAgentFactory` creates a new `OllamaApiClient`/`OpenAIClient`-backed `IChatClient` per `CreateXAgentAsync`; `DisposeAsync` only disposes `_haClient`. Sweeps recreate agents per (model × combo × agent) → hundreds of undisposed HttpClients.
- **No LLM-call timeouts anywhere** (no `CancellationTokenSource`/`CancelAfter`); a hung local model stalls the whole run.
- **Silent constant scores:** `NoOpChatClient` returns `{"score":50}` when judge unconfigured and feeds `task_completion` → OverallScore + `avgScore>=70` gate. Judge/model failures return hard `0` (PersonalityJudge/PersonalityEvalRunner), conflating infra failure with real scores.
- **Scenario path duplicates metrics:** `EvaluateScenariosAsync` sets all four sub-scores to the same aggregate scenario score (EvalRunner.cs:455-459) — the per-dimension breakdown is fabricated for scenario agents.
- **Pass criteria inconsistent/hardcoded:** TestCase mode `avgScore >= 70` (magic number) vs scenario mode `issues.Count == 0`.
- **`num_ctx` never set** — Ollama default 2048 can truncate long agent prompts/tool defs and cause false tool-selection failures.

**Things that are correct (don't re-flag):** ScenarioValidator `Async`-suffix normalization (lines 142-145, 270-275); tracing-required guard in EvaluateScenariosAsync (364-369); defensive judge JSON parse+clamp; per-test exception isolation; clean `InferenceBackend`/`BackendChatClientFactory` abstraction.

<!-- Append new learnings below. Each entry is something lasting about the project. -->

- Participated in 2026-05-29 health review

## Learnings

### 2026-07-10: Multi-run sweep aggregation (issue #134)

**What I implemented:**
- `ParameterSweepConfig.RunsPerCombination` (default 3) — configures how many times each sweep combination is evaluated before a winner is selected.
- `SweepRunAggregator` — pure static class with no external dependencies: `ComputeMean`, `ComputeVariance`, `ComputeMinRunMean`, `SelectWinner`, `DeriveRunSeed`.
- `SweepEntry` updated: `AllRunResults` (all N run results), `MeanScore`, `ScoreVariance`, `MinRunMean`. `AverageScore` is now an alias for `MeanScore` for backward compatibility with report generators.
- `ParameterSweepRunner.RunAsync` loops N times per combination; each run gets `baseSeed + runIndex` when a seed is configured.
- New `lucia.EvalHarness.Tests` project (18 fast provider-free tests).

**Aggregation strategy:**
- Primary criterion: highest mean score across N runs
- Tie-breaker: lower score variance (stable config beats volatile one on equal means)
- This prevents a single lucky run from being misreported as the best configuration.

**Architectural patterns:**
- `lucia.EvalHarness` (Exe) cannot be a test project itself — created `lucia.EvalHarness.Tests` as a separate csproj in the solution's Tests folder.
- `lucia.EvalHarness.Tests` references `lucia.EvalHarness` but NOT `lucia.Tests` — avoids circular dependency.
- Global using `<Using Include="Xunit" />` needed in the new test project (not inherited from Directory.Build.props).
- Collection expression syntax `[item1, item2]` inside `new List<T> { ... }` initializers is parsed as indexer access, NOT a collection expression — use explicit `new List<T> { item1, item2 }` instead.

**Build verification:**
- `dotnet build lucia-dotnet.slnx -v minimal` — 0 warnings, 0 errors
- `dotnet test lucia.EvalHarness.Tests` — 18/18 passed, 154ms
---

**Update from Ripley (2026-05-30):** Inbox retriage complete. You have been assigned issues from the 2026-05-30 batch. Review .squad/decisions/decisions.md for details.
