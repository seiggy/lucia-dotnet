# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant, built on .NET 10/C# 14 with Microsoft Agent Framework
- **Stack:** .NET 10, C# 14, Aspire 13, Ollama (local LLMs), Azure OpenAI (judge), xUnit, AgentEval, MongoDB/SQLite, Redis/InMemory
- **Created:** 2026-03-26

## Key Architecture

- **Agents:** LightAgent, ClimateAgent, ListsAgent, SceneAgent, GeneralAgent, DynamicAgent, OrchestratorAgent, MusicAgent (separate project)
- **Eval Infrastructure:** lucia.EvalHarness (TUI + reports), lucia.Tests/Orchestration/ (xUnit eval tests)
- **Eval Base:** AgentEvalTestBase provides shared helpers, DiskBasedReportingConfiguration, assertion methods
- **Test Fixture:** EvalTestFixture creates real agents backed by Ollama or Azure OpenAI
- **Trace Pipeline:** TraceCaptureObserver → MongoTraceRepository/SqliteTraceRepository → JsonlConverter for fine-tuning exports
- **YAML Datasets:** light-agent.yaml, climate-agent.yaml, lists-agent.yaml, scene-agent.yaml, general-agent.yaml, orchestrator.yaml

## Current Eval Coverage Gaps

- No eval suites for: ClimateAgent, ListsAgent, SceneAgent, GeneralAgent, DynamicAgent
- No GitHub issue ingestion pipeline
- No trace→eval-scenario conversion
- No eval spec document

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2025-10-13: Eval Expansion Architecture

**Standard Eval Suite Pattern:**
- Each agent needs 3 files: `{AgentName}EvalTests.cs`, factory method in `EvalTestFixture`, factory method in `RealAgentFactory`
- Test methods follow trait-based categorization: `[Trait("Evaluator", "ToolCallAccuracy|IntentResolution|TaskAdherence")]`
- Naming convention: `{ToolName}_{Scenario}_{ExpectedBehavior}` for methods, `{AgentName}.{ToolName}_{Scenario}[{variant}]` for scenario names
- `WithVariants` pattern enables STT robustness testing without duplicating test logic
- `ChatHistoryCapture` wrapper required for `ToolCallAccuracyEvaluator` to see intermediate tool calls consumed by `FunctionInvokingChatClient`

**DynamicAgent Special Considerations:**
- Tools resolved from MCP registry at initialization, not compile-time — requires different eval approach
- Use in-memory `IAgentDefinitionRepository` fake to avoid MongoDB container dependency in tests
- Eval focus: tool resolution correctness, hot-reload, definition loading — NOT specific tool call validation
- Test data is agent definitions (as `[MemberData]`), not user prompts like other agents

**YAML Dataset Formats:**
- Format 1 (structured scenarios): Complex state-dependent tests with `initial_state`, `expected_final_state`, `expected_tool_calls` (light-agent.yaml)
- Format 2 (simple): Simpler `input`/`expected`/`criteria` structure for most agents (climate, scene, lists, general, orchestrator)
- Format 2 recommended for new datasets — easier to generate from traces/issues, TUI harness supports both

**Trace Scoring Heuristics (for eval-worthiness):**
- User feedback (thumbs up/down): 1.0/0.8 — explicit signal of quality
- Execution errors (!success): 0.7 — regression prevention
- Low routing confidence (<0.6): 0.5 — ambiguous cases worth capturing
- Multi-turn corrections (>1 turn): 0.4 — user had to rephrase
- Threshold: 0.5 minimum score for trace → scenario conversion

**GitHub Issue → Scenario Quality Gates:**
- LLM confidence must be ≥ 0.7 for auto-generated scenarios
- Human review required for all generated scenarios before merge (via PR workflow)
- Scenarios should test expected behavior, not just reproduce the bug
- Add `metadata.category: regression` and `metadata.github_issue: "{number}"` for traceability

**Infrastructure Stability Wins:**
- `AgentEvalTestBase` provides all common helpers — no duplication across test suites
- `EvalTestFixture` and `RealAgentFactory` mirror each other (xUnit vs. TUI) — same construction pattern ensures consistency
- `DiskBasedReportingConfiguration` enables `dotnet aieval report` integration without custom tooling
- Stable execution name (`s_executionName`) groups all scenarios in single report run

**Coverage Gaps Identified:**
- 2/8 agents with full coverage (Light, Music), 5/8 with datasets only (Climate, Scene, Lists, General, Orchestrator), 1/8 with no coverage (Dynamic)
- No systematic GitHub issue → eval scenario pipeline (bugs likely to recur)
- No production trace → eval scenario conversion (missing real-world usage patterns)
- MusicAgent has xUnit suite but no YAML dataset (inverse of other agents)

### 2026-03-26: LightAgentEvalTests Deep Audit

**Key Finding: xUnit eval tests are smoke tests, not real evals.**

Every test in `LightAgentEvalTests.cs` only asserts `AssertHasTextResponse()` + `AssertNoUnacceptableMetrics()`. No test uses the available `AssertToolCalled()` helper. No test verifies entity resolution, parameter extraction, or state changes. The LLM judge (SmartHomeToolCallEvaluator) provides a 1-5 score but `AssertNoUnacceptableMetrics` only catches scores ≤1, meaning "Poor" (2/5) passes.

**Contrast with EvalHarness:** The TUI harness (`lucia.EvalHarness`) tests ARE specific — they check exact tool names, parameters, and expected state. The xUnit suite is dramatically less useful for model debugging.

**Infrastructure issues found:**
- Azure API key in appsettings.json is placeholder — all Azure-backed tests fail with 401
- `ControlLightsAsync` vs `ControlLights` name mismatch appears in harness failures (test infra bug, not model bug)
- Judge model requires Azure OpenAI — Ollama-only environments can't score

**EvalHarness results (most recent):** granite4:350m scored 37.1/100 on LightAgent (18% pass), gemma3:270m scored 18.2/100. GeneralAgent (no tools) scores 84-87 across both models.

**Full audit written to:** `.squad/decisions/inbox/ripley-light-eval-audit.md`
