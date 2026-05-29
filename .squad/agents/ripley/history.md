# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant, built on .NET 10/C# 14 with Microsoft Agent Framework
- **Stack:** .NET 10, C# 14, Aspire 13, Ollama (local LLMs), Azure OpenAI (judge), xUnit, AgentEval, MongoDB/SQLite, Redis/InMemory
- **Created:** 2026-03-26

## Key Architecture

- **Agents:** LightAgent, ClimateAgent, ListsAgent, SceneAgent, GeneralAgent, DynamicAgent, OrchestratorAgent, MusicAgent (separate project)
- **Orchestration:** RouterExecutor pattern with multi-agent routing, WorkflowFactory with custom executors
- **Evaluation:** lucia.EvalHarness (TUI + reports), lucia.Tests/Orchestration/ (xUnit eval tests)

## Durable Learnings (Condensed)

### 2026-05-29: Whole-Solution Health Review — Systemic Intent-Enforcement Gaps

The solution's biggest risk is **intent-vs-enforcement drift**, not localized bugs. Aspirational guarantees (observability, reproducibility, CI gating, UTC discipline) silently fail at the seams:

- **CI is non-functional on repo default branch (master)** — squad workflows trigger on main/dev/preview but not the real default; build/test steps are cho stubs. Treat green CI as meaningless until infra issues #1-#3 fixed.
- **OTel source/meter names are ordinal case-sensitive** — Lucia.* in code never matches registered lucia.*; drops orchestration spans + most skill/service/task meters. Use shared name constants.
- **DateTime.Now vs UtcNow is cross-cutting bug** — appears in 4 domains (agent refresh gates, MusicAgent, SQLite text timestamps, test naming). UTC-behind timezones cause per-request rebuilds; non-UTC offsets corrupt range queries.
- **Routing brain has near-zero deterministic tests** — RouterExecutor/ResultAggregatorExecutor/LuciaEngine coverage is provider-gated eval suites that skip in CI.
- **Voice pipeline is the observability model citizen** — cite it when arguing OTel patterns are achievable.

**Decision:** Three-wave remediation sequence. Wave 1 blocks all later work: fix CI gate, restore telemetry naming consistency, apply UTC fixes, close unauthenticated API surfaces.

### 2026-03-27 to 2026-05-25: Architecture & Config Decisions (Archived)

**Synthesized insights:**
- **Entity matching cascading elimination** — replaces heuristic scoring; deterministic pipeline (location→domain→entity) for simple commands, LLM fallback for complex.
- **MAF Workflows v2 feasibility** — consolidate WorkflowFactory into DynamicOrchestrationWorkflow (10-15% complexity reduction, no risk).
- **Config poll intervals** — increase SQLite/Mongo from 5s to 30s (API writes already provide instant reload).
- **ONNX thread pool idle spin** — set ORT_THREADPOOL_SPIN_CONTROL=0 (highest-impact, zero code change); reduce NumThreads 4→2 (Phase 2).
- **Agent timeout handling** — map OperationCanceledException to user-readable failures; use CancellationToken.None for aggregation to survive upstream cancellation.

### Previous Releases & Decisions
- Prompt cache architecture: two-tier system (routing + chat) with SHA256 + semantic fallback
- Embedding provider changes: force rebuild on provider switch to avoid vector-space drift
- Jetson Nano ARM64: ExcludeSpeech=true preserves command routing while dropping voice runtime
- Personality response pipeline: production-ready with IPersonalityResponseRenderer

- Participated in 2026-05-29 health review
