# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant
- **Stack:** .NET 10, C# 14, xUnit, AgentEval, Ollama, Azure OpenAI (judge)
- **Created:** 2026-03-26

## Test Coverage Responsibility

- Eval infrastructure: lucia.Tests/Orchestration/, lucia.EvalHarness (TUI + reports)
- Agent eval suites: LightAgentEvalTests, MusicAgentEvalTests, OrchestratorEvalTests, PersonalityTests
- Integration test patterns: WyomingSession, ConversationCommandProcessor, DirectSkillExecutor
- Evaluation base: AgentEvalTestBase, EvalTestFixture, real agent factories

## Durable Test Patterns

### Test Organization
- **One class per file** rule maintained across all test files
- **Trait-based categorization** — [Trait("Evaluator", "ToolCallAccuracy|IntentResolution|TaskAdherence")]
- **Naming convention** — {ToolName}_{Scenario}_{ExpectedBehavior} for methods, {AgentName}.{ToolName}_{Scenario}[{variant}] for scenarios

### YAML Dataset Structure
- Light agent: metadata.category (basic, room-specific, cross-domain, STT-variant)
- Climate agent: temperature, HVAC mode, fan speed controls
- Orchestrator: 59 scenarios across routing, cross-domain, timer, scene, lists, general categories
- All scenarios include difficulty metadata (easy/medium/hard) for slicing eval runs

### Eval Expansion Architecture (2025-10-13)
- **ClimateAgent eval suite** — sealed class, AgentEvalTestBase inheritance, MemberData cross-products
- **STT variant pattern** — WithVariants helper for robustness testing without duplication
- **EvalTestFixture** — Pre-implemented factory methods (forward-planning architecture)
- Regex is NOT used in command pattern matching — custom recursive token engine instead

### 2026-05-29: Whole-Solution Test Health Review

**Coverage blind spots:**
- **Orchestration core:** RouterExecutor ONE unit test (metadata only); ResultAggregatorExecutor ONE (timeout only); LuciaEngine/WorkflowFactory/RemoteAgentInvoker = zero deterministic unit tests
- **Routing brain coverage is gated** — eval suites skip in CI due to Azure endpoint/HA credentials missing; appsettings.json ships placeholder ApiKey
- **Skill coverage gaps** — SceneControlSkill and ListSkill have no dedicated unit tests; Climate/Fan only via gated eval
- **No-assert benchmark anti-pattern** — SpeechEnhancementValidationTests benchmark methods pass unconditionally (should have threshold assertions)
- **E2E is TypeScript only** — lucia-playwright/e2e/*.spec.ts covers wizard/cache/optimizer/impersonation/plugins; no conversational or voice happy-path

**Heavy runtime gating:**
- Zero hard-disabled [Fact(Skip=...)] tests
- All skips via SkippableFact (Skip.If/IfNot) gates on: HA creds, Docker/Redis, Ollama/Azure, local models+WAV files
- Aggregate effect: behavior-critical slice absent from CI

### Enhanced Clip Pipeline Tests (2026-04-14)

WyomingSession integration test pattern for feature flag validation:
- Extracted RunPipelineAndGetTranscriptAsync helper (wake→audio→stop→transcript)
- Use distinguishable audio (amplitude transforms) to validate feature flag execution path
- QueuedSttEngine dequeues sessions in order for streaming vs re-transcription verification
- Guard against edge cases: no enhancer, empty buffer, not-ready → all handled by existing buffers

### Orchestrator Routing Coverage Expansion

**Bug-driven eval design:**
- Real failure case ("turn off lights in Zack's Office" → climate @85%) became regression test anchor
- Cross-domain confusion tests (light vs climate) use DoesNotContain negative assertions
- Domain inference hints added to RouterExecutorOptions Rule 8 for timer/schedule language

**Multi-agent routing pattern:**
- Primary agent in RoutingDecision.AgentId
- Secondary agents in RoutingDecision.AdditionalAgents
- Combine both into allAgents list for assertion

- Participated in 2026-05-29 health review

### Issue #148: Provider-free eval opt-in (2026-07-12)

**Problem:** Behavior-critical routing/aggregation paths were only covered by `Category=Eval` tests that skip under CI placeholder credentials, so regressions in `RouterExecutor` fallback/clarification/normalization and `ResultAggregatorExecutor` composition were invisible to ordinary CI.

**What changed:**
- `RouterExecutorFallbackTests.cs` (new, 7 tests) — deterministic coverage of: no-agents fallback, unknown-agent fallback, confidence-below-threshold clarification, max-retry exhaustion, NormalizeAdditionalAgents filtering/dedup, OriginalUserText propagation.
- `ResultAggregatorExecutorTests.cs` (new, 13 test cases) — deterministic coverage of: single success, empty-content template, single failure format, multi-failure format, mixed success+failure, empty-responses fallback, NeedsInput flag, multi-success join, priority ordering, agent name title-casing.
- `HomeAssistantApiTests.cs` — added `[Trait("Category","LiveEval")]` at class level to make the HA-live opt-in explicit (previously silently skipped).
- `squad-ci.yml` — added `&Category!=LiveEval` to the CI filter so HA live tests are excluded by policy, not just luck.

**Key decisions:**
- Reused `StubChatClient`, `AgentsTelemetrySource`, `FakeItEasy` fakes — no new infrastructure.
- `DurableTaskPersistenceTests` left as-is (Docker/Redis available in CI, not credential-gated, tests pass).
- All 20 new tests pass in < 1 second total; CI test count 1124 → 1130, skipped 21 → 7.
- Slopwatch: not installed as local tool; manual checks confirmed no disabled tests, warning suppressions, empty catch blocks, or arbitrary delays.
- One class per file maintained.
