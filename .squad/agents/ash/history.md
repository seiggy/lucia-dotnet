# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant
- **Stack:** .NET 10, C# 14, MongoDB/SQLite (trace storage), GitHub API, OpenTelemetry
- **Created:** 2026-03-26

## Key Files I Build On

- `lucia.Agents/Training/TraceCaptureObserver.cs` — captures conversation traces
- `lucia.Agents/Training/ConversationTrace.cs` — trace data model (includes RoutingDecision, TracedToolCall, TraceLabel)
- `lucia.Agents/Training/JsonlConverter.cs` — trace→JSONL export
- `lucia.Agents/Training/TraceRetentionService.cs` — trace cleanup
- `lucia.Data/Sqlite/SqliteTraceRepository.cs` — SQLite trace storage
- `lucia.Agents/Training/MongoTraceRepository.cs` — MongoDB trace storage
- `lucia.AgentHost/Apis/DatasetExportApi.cs` — dataset export REST API

## Current State

- Trace capture works end-to-end: observer → repository → JSONL export
- Traces have labels, routing decisions, tool call history, performance data
- No GitHub issue ingestion exists yet
- No trace→eval-scenario conversion exists yet
- Dataset export API exists but only for JSONL fine-tuning format

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-03-26: Data Pipeline Implementation

**What I Built:**
- Complete data pipeline architecture in `lucia.EvalHarness/DataPipeline/`
- `IEvalScenarioSource` — source abstraction for extensibility
- `GitHubIssueScenarioSource` — parses GitHub issues with trace reports into eval scenarios
- `TraceScenarioSource` — converts conversation traces from repository into scenarios
- `EvalScenarioExporter` — exports scenarios to YAML format matching existing test structure
- `EvalScenario` model — intermediate representation decoupling sources from export formats

**Key Learnings:**
1. GitHub issues with embedded trace reports follow consistent markdown structure
   - Trace reports contain: user input, agent selection, tool calls, errors
   - Regex extraction works for current template format
   - 10+ actionable issues found with trace data (issues #103-107, others)

2. Conversation traces have rich data for eval generation:
   - Routing decisions provide expected agent mapping
   - Tool calls from successful executions become expected API interactions
   - Errored traces automatically become regression test cases
   - Metadata (timestamp, duration, confidence) preserved for debugging

3. YAML export format matches existing TestData structure:
   - Scenarios have: id, description, category, user_prompt, expected_agent
   - Optional fields: tool calls, response assertions, state expectations
   - YamlDotNet handles serialization with underscore naming convention

4. Design allows for easy extension:
   - New sources implement `IEvalScenarioSource` interface
   - New export formats only need new exporter class
   - Filter criteria support category, agent, source type, errors-only

**Build Status:** ✅ Clean build, zero warnings

**Next Steps for Pipeline:**
- Add automation: scheduled dataset generation from new data
- Implement deduplication to avoid redundant scenarios
- Add confidence thresholds for trace inclusion
- Create human-in-the-loop approval workflow

### 2026-03-26: Light Agent Pain Map Analysis

**What I Analyzed:**
- 100+ GitHub issues (6 directly light-agent-related: #105, #103, #84, #83, #38, #71)
- 8 eval trace files across 2 models (granite4:350m and gemma3:270m), 77 test executions
- 11 YAML scenarios in light-agent.yaml
- xUnit tests in LightAgentEvalTests.cs and FindLightSkillEvalTests.cs
- LightAgent.cs system prompt and LightControlSkill.cs tool definitions

**Key Findings:**
1. granite4:350m consistently picks GetLightsState instead of ControlLights for "turn on" commands — wrong tool selection is the #1 failure mode
2. Color parameter extraction is 100% broken — models put color name in `state` field instead of `color` field
3. gemma3:270m is too small for tool-calling — 0 tool calls on 9/11 scenarios
4. Real users report entity resolution failures: "front room" and "dining room" don't match despite areas existing (#105, #103)
5. Non-English users are blocked — orchestrator translates entity names, breaking entity resolution (#84)
6. Eval infrastructure has a tool-name mismatch bug: YAML expects `ControlLightsAsync` but models emit `ControlLights`, inflating failure counts by ~50%
7. Major testing gaps: no multi-room, relative brightness, color temperature, toggle, non-English, bulk operation, or error recovery scenarios

**Deliverable:** `.squad/decisions/inbox/ash-light-agent-pain-map.md`

### 2026-03-26: Trace Data → xUnit Test Integration

**What I Built:**
- `lucia.Tests/TestData/light-agent-traces.json` — 22 structured eval scenarios derived from 8 real eval trace runs (77 executions across granite4:350m and gemma3:270m)
- `lucia.Tests/TestData/light-agent-user-issues.json` — 6 scenarios from real GitHub issues (#105, #103, #84)
- `lucia.Tests/Orchestration/TraceScenarioLoader.cs` — static loader with filtering by failure type, model, category, regression status; returns `[MemberData]`-compatible rows for xUnit
- `lucia.Tests/Orchestration/TraceScenario.cs` — model for trace-derived scenarios
- `lucia.Tests/Orchestration/UserIssueScenario.cs` — model for issue-derived scenarios
- `lucia.Tests/Orchestration/TraceScenarioCollection.cs` — root deserialization container for traces
- `lucia.Tests/Orchestration/UserIssueScenarioCollection.cs` — root deserialization container for issues
- `lucia.Tests/Orchestration/TraceScenarioMetadata.cs` — metadata header model

**Key Learnings:**
1. Failure type distribution from real traces: WRONG_TOOL (14%), NO_TOOL_CALL (32%), WRONG_PARAMS (14%), STATE_ERROR (9%), CORRECT (27%), WRONG_RESPONSE (5%)
2. gemma3:270m produces zero tool calls on 9/11 scenarios — it's a baseline-only model, not viable for production
3. granite4:350m has a consistent WRONG_TOOL pattern for control commands (calls GetLightsState instead of ControlLights), reproducible across all 8 runs
4. ~50% of eval failures in later runs are inflated by the Async suffix mismatch bug in eval harness — real failures are lower than reported scores suggest
5. GitHub issues #105/#103 reveal entity resolution failures that no eval scenario covers — colloquial room names and area matching are production-critical gaps
6. The `TraceScenarioLoader` resolves test data from both output directory (CI) and project root (IDE) — portable across test runners

**Build Status:** ✅ New code compiles cleanly (pre-existing error in ModelComparisonReporter.cs is unrelated)

**Deliverable:** `.squad/decisions/inbox/ash-trace-data-integration.md`

### 2026-03-26: Conversation Pipeline Issue Analysis

**What I Analyzed:**
- 100 GitHub issues (12 directly relevant to conversation routing/command parsing/handoff)
- ConversationCommandProcessor.cs fast-path architecture
- All registered command patterns (LightControlSkill, ClimateControlSkill, SceneControlSkill)
- Prior light agent pain map findings for conversation-routing overlap

**Key Findings:**
1. Fast-path is too aggressive: it matches commands it can't properly resolve. Issues #105 and #103 show entity resolution matching the wrong room despite HA areas existing — "front room" → Guest Room, "dining room" not matched at all.
2. Of 12 relevant issues: 6 are fast-path failures, 3 are orchestrator failures, 3 are handoff/pipeline failures.
3. The fast-path's entity resolution is purely string-matching with no semantic understanding of aliases, colloquial names, or area hierarchies. It matches syntax but fails semantics.
4. Orchestrator has its own problems: routes garbled STT at 95% confidence (#106), translates non-English entity names breaking resolution (#84), and drops multi-turn conversation state (#58).
5. Commands that consistently fail in fast-path: colloquial room names, color, fan/HVAC mode, temporal ("in 5 minutes"), multi-room groups, non-English, relative adjustments.
6. Commands that succeed in fast-path: exact-name on/off, exact-area thermostat, simple scene activation.
7. Zack's own comment on #103 confirms the direction: make fast-path "exact" matching only, let orchestrator handle fuzzy/ambiguous cases.

**Recommendations:**
- Fast-path should require exact area/entity name match; any fuzzy match → orchestrator
- Remove color, fan, HVAC mode, temporal, multi-room from fast-path scope
- Add STT quality gate before orchestrator routing
- Add entity name preservation rule to orchestrator prompt (fix #84)
- Add entity resolution quality signal — low-quality matches should not produce `commandHandled`

**Deliverable:** `.squad/decisions/inbox/ash-conversation-issues.md`

### 2026-05-29: Data Layer Health Review (whole-solution review)

**What I Reviewed:** `lucia.Data/` three-database pattern (InMemory/SQLite/PostgreSQL) plus `MongoMemoryStore` — reader/connection lifetime, query correctness, indexing, migrations, TTL.

**Key Learnings about the data layer:**
1. The two flagged commit bugs are genuinely fixed and sound. Npgsql has no MARS, so Postgres repos MUST close a reader before reusing the connection — `PostgresCommandTraceRepository.GetStatsAsync` and `PostgresConfigurationProvider.PollForChanges` now call `CloseAsync`. The SQLite twins can keep two readers open on one connection because Microsoft.Data.Sqlite supports it. This asymmetry is intentional and correct — do not "fix" the SQLite versions to match Postgres.
2. Memory stores cap `GetAllAsync` at 200; `InMemoryCommandTraceRepository` is FIFO-capped at 500. Bounded result sets are an established convention.
3. Biggest latent risk: SQLite stores timestamps as ISO-8601 strings and compares them lexically. Correct only while every value is UTC. `SqliteScheduledTaskRepository` stores `DateTimeOffset.ToString("O")` (offset-bearing) and compares it in `PurgeCompletedAsync` — a non-UTC offset silently corrupts purge results. Postgres uses native `timestamptz` and is safe. New SQLite time columns should normalize to UTC or use epoch integers.
4. `SqliteApiKeyService.ValidateKeyAsync` writes `last_used_at` via fire-and-forget `Task.Run` on every auth — a write on the hot path against single-writer SQLite (`Cache=Shared`, `busy_timeout=5000`). Scaling/contention risk.
5. Free-text search uses `ILIKE '%x%'` over JSONB/`data::text` (archive + command traces) — unindexed full scans on the tables most likely to grow.
6. Conventions confirmed: 100% parameterized SQL, versioned transactional migrations (`CREATE TABLE IF NOT EXISTS` + rollback, run per-DB), keyed connection factories isolate config/traces/tasks, `MongoMemoryStore` uses a real TTL index while SQL stores emulate TTL with explicit delete sweeps + `expires_at > now` predicates.
7. Minor: both config providers seed `_lastLoadRowVersion` with `COUNT(*)` in `Load()` but compute `count ^ MAX(updated_at).GetHashCode()` in the poll, causing one phantom reload on first tick.

**Deliverable:** `review-data.md` in the session files folder.

- Participated in 2026-05-29 health review
---

**Update from Ripley (2026-05-30):** Inbox retriage complete. You have been assigned issues from the 2026-05-30 batch. Review .squad/decisions/decisions.md for details.

### 2026-07-10: UTC Timestamp Normalization (Issue #163, PR #221)

**What I Fixed:**
Nine built-in agents (`LightAgent`, `ClimateAgent`, `SceneAgent`, `GeneralAgent`, `ListsAgent`, `SecurityAgent`, `SensorAgent`, `MusicAgent`, `TimerAgent`) used `DateTime.Now` (machine-local) for `_lastConfigUpdate`, compared against MongoDB-stored `UpdatedAt` (UTC). In US Eastern time (UTC-4), every request triggered a full agent rebuild. Changed all to `DateTime.UtcNow`.

SQLite data layer normalized: `SqliteScheduledTaskRepository` stores `FireAt.ToUniversalTime()` so purge text comparisons are correct. `SqliteCommandTraceRepository` normalizes `Timestamp` and filter date params to UTC. `SqliteConfigStoreWriter` now parses `updated_at` via `DateTimeOffset.TryParse(AssumeUniversal | AllowWhiteSpaces)` + `.UtcDateTime`, correctly handling both offset-less legacy `datetime('now')` values (treated as UTC) and explicit-offset strings (shifted to UTC).

**Key Learnings:**
1. `DateTime.Now` vs `DateTime.UtcNow` comparison bugs are silent — the wrong kind is always a valid `DateTime`, so no runtime exception. The only symptom is behavioral (always-rebuild or never-rebuild depending on timezone).
2. SQLite text comparison of ISO-8601 timestamps is only safe when ALL stored values use the same UTC offset (`+00:00` or `Z`). `DateTimeOffset.ToString("O")` preserves the original offset, so normalization at write time is essential.
3. `DateTimeStyles.RoundtripKind` in `DateTime.TryParse` does NOT guarantee `Kind=Utc` for all UTC strings: strings with an explicit `+00:00` offset parse as `Kind=Local`, and offset-less `datetime('now')` values (e.g. `"2025-06-01 12:00:00"`) parse as `Kind=Unspecified`. The correct read approach for stored timestamps is `DateTimeOffset.TryParse` with `DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces`, then take `.UtcDateTime` — offset-less legacy values are treated as UTC, and explicit-offset values are normalized to UTC. For filter date bounds, `Unspecified`-kind `DateTime` inputs should be reinterpreted as UTC via `DateTime.SpecifyKind(dt, DateTimeKind.Utc)` (not `ToUniversalTime()` which shifts by host timezone); `Local`-kind inputs must use `ToUniversalTime()` for correct conversion; `Utc`-kind inputs pass through unchanged.
4. MongoDB's .NET driver always returns `DateTime` with `Kind=Utc` — safe to compare with `DateTime.UtcNow` directly without conversion.
5. Display-only `DateTime.Now` usages (e.g., report folder names) are intentional and should not be changed — scope control matters.

**Build:** 0 errors, 0 warnings. 1047 tests passing (3 pre-existing failures in deprecated API, unrelated).

