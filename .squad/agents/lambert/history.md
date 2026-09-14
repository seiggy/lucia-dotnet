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

### Health Flood / Aspire Redis Startup Regression Tests (2026-07-20)

**Context:** Hicks implemented HTTP output-cache (10 s TTL, `.CacheOutput("health-checks")`) for `/health` and `/alive` endpoints in `Extensions.AddServiceDefaults`. Parker's `.WithCertificateTrustScope(CertificateTrustScope.None)` fix already in AppHost.cs.

**Delivered:**

- `lucia.Tests/Diagnostics/HealthCheckCachingTests.cs` — active `[Fact]` that asserts `IOutputCacheStore` is registered after `AddServiceDefaults`. Fails immediately if `AddOutputCache` is stripped. CI count: 1173 → 1173 + 1 active.
- `lucia.Tests/Diagnostics/AspireRedisHealthSmokeTests.cs` — permanently-skipped `[Trait("Category","Integration")]` SkippableFact documenting the manual assertion (`dotnet run --project lucia.AppHost` → redis=Healthy). No existing DistributedApplicationFixture, so no automated test.

**Durable learnings:**

- `IHealthCheckService` is NOT directly importable in the test project despite `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. `IOutputCacheStore`, `HealthCheckResult`, `IHealthCheck` ARE importable. Avoid `IHealthCheckService` in unit tests unless an explicit assembly reference is added.
- For pending-impl contract tests, `[SkippableFact]` + env-var gate (`Skip.IfNot(env == "enabled", ...)`) is the right pattern to avoid both slopwatch SW001 flags and CI breakage.
- Behavioral (HTTP-level) health cache tests require `Microsoft.AspNetCore.TestHost` NuGet (not in framework ref). Configuration-level tests via `IOutputCacheStore` are the cheapest compile-safe regression guard.
- Aspire smoke tests are too integration-heavy without an existing `DistributedApplicationFixture`. Document as `[Trait("Category","Integration")]` + Skip, identify manual assertion in comment.
- `Skip.Always` does not exist in `Xunit.SkippableFact` 1.5.x — use `Skip.If(true, reason)`.
- Slopwatch installed as a local tool in this project (via `dotnet tool restore`); `slopwatch analyze` runs cleanly with 0 issues.
- CI test count: 1173 passing, 0 failing, new test in CI filter (`Category!=Eval&Category!=Integration&Category!=LiveEval`) passes.

