# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant, built on .NET 10/C# 14 with Microsoft Agent Framework
- **Stack:** .NET 10, C# 14, Aspire 13, Ollama (local LLMs), Azure OpenAI (judge), xUnit, AgentEval, MongoDB/SQLite, Redis/InMemory
- **Created:** 2026-03-26

## Key Architecture

- **Agents:** LightAgent, ClimateAgent, ListsAgent, SceneAgent, GeneralAgent, DynamicAgent, OrchestratorAgent, MusicAgent (separate project)
- **Orchestration:** RouterExecutor pattern with multi-agent routing, WorkflowFactory with custom executors
- **Evaluation:** lucia.EvalHarness (TUI + reports), lucia.Tests/Orchestration/ (xUnit eval tests)

## Learnings

### 2026-05-30: GitHub Issue Inbox Re-triage (50 issues, all incorrectly bulk-labeled `squad:lambert`)

All 50 open `squad`-labeled issues were found carrying `squad:lambert` after a bad bulk-triage. Correct labels applied by domain routing:

| Member    | Count | Issues |
|-----------|-------|--------|
| parker    | 17    | #176 #175 #174 #173 #172 #171 #170 #169 #168 #167 #166 #165 #158 #154 #153 #145 #140 |
| hicks     | 11    | #181 #164 #162 #161 #159 #155 #151 #147 #142 #138 #135 |
| brett     | 6     | #183 #182 #180 #179 #178 #177 |
| lambert   | 4     | #144 #148 #152 #156 (correctly kept) |
| dallas    | 4     | #134 #137 #141 #150 |
| kane      | 3     | #136 #139 #143 |
| bishop    | 3     | #149 #157 #160 |
| ripley    | 1     | #146 |
| ash       | 1     | #163 |

**Root cause:** Bulk triage without reading domain map; lambert's narrow scope (writing test scenarios, assertions, skill unit tests, provider-free coverage) was applied to all issues indiscriminately. 46 of 50 issues were wrong.

---

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
