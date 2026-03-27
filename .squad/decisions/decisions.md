# Lucia Orchestration & Caching Decisions Log

## 2026-03-27: Entity Matching Root Cause & Fix Proposal

**Author:** Parker (Backend / Platform Engineer)  
**Date:** 2025-07-17  
**Status:** Proposed  
**Decision Type:** Bug Fix / Architecture

### Summary
Both "turn off the bedroom lights" → bathroom lights and "play music in the office" → wrong speaker bugs trace to `EntityLocationService.SearchHierarchyAsync()` → `HybridEntityMatcher.FindMatchesAsync()` → `StringSimilarity.HybridScore()`.

**Root Causes:**
1. Embedding similarity computed from unstemmed query ("bedroom lights") instead of stop-word-stripped version ("bedroom")
2. Path selection in SearchHierarchyAsync biases toward entities over areas (margin check favors entity wins)
3. No query decomposition step — single-tool approach passes full natural language, relying entirely on hybrid matcher

**Recommendation:** Apply stop-word stripping before embedding query + swap path priority to area-first selection in close races. Both fixes working together.

**Test Plan:** Add unit tests for area→entity resolution, entity name matching, regression guards in existing EmbeddingMatchingTests.cs.

**Risk Assessment:** Low risk for embedding normalization (extends proven string scoring). Medium risk for path priority swap, mitigated by margin check — if entity clearly dominates, it still wins.

---

## 2026-03-27: Prompt Cache Investigation

**Author:** Bishop (HA Integration Engineer)  
**Date:** 2025-07-23  
**Requested by:** Zack Way  
**Status:** Investigation complete

### Architecture
Two-tier caching system: Tier 1 (routing cache for agent selection), Tier 2 (chat/agent cache for tool-call plans).

Both support SHA256 matching + semantic similarity fallback (embeddings + cosine similarity).

### Critical Issues

**Issue 1: No Automatic Cache Invalidation (PRIMARY "FRAILTY" SOURCE)**
- Agent definitions change → stale routing entries served (mitigated by IsKnownAgent guard, but not proactive)
- Model provider changes → cached entries from different model served (ModelId stored but not checked)
- HA entity data changes → HA context stale, but 48h TTL means entries persist indefinitely until manual eviction

**Issue 2: Cold-Start Problem (InMemory Only)**
- In-memory implementation starts empty, no cache warming
- Redis survives restarts

**Issue 3: Routing Cache Key Uses Last Line Only**
- HA component prepends context above user command
- Intentional but can't differentiate if agent catalog changes

**Issue 4: Chat Cache Requires Embedding Provider**
- Semantic fallback disabled if no embedding provider
- Users without embedding model get exact-match only

**Issue 5: InMemory Implementation Lacks OTel Metrics**
- RedisPromptCacheService has Counter/Histogram instruments
- InMemoryPromptCacheService only has internal fields, no OTel registration

### Recommendations (Priority)

**P0:** Auto-invalidate on config changes. When agent definitions/model providers/entity location cache change, call EvictAllAsync(). This is the primary "frailty" source.

**P1:** Add OTel metrics to InMemoryPromptCacheService (Counter/Histogram matching Redis impl).

**P2:** Show cache bypass rate in dashboard (activity tags already captured).

**P3:** Add auto-refresh to PromptCachePage (10-30s polling).

**P4:** Consider shorter TTL for routing cache (48h is generous, suggest 12-24h configurable).

---

## 2026-03-27: Cache Architecture Investigation

**Author:** Bishop (HA Integration Engineer)  
**Date:** 2026-03-27  
**Status:** Investigation Complete  
**Requested by:** Zack Way — "the system feels like it's not using the cache and is just a tad frail sometimes"

### Executive Summary
Cache architecture well-designed in principle, but THREE concrete gaps:

1. **Entity resolution cached; entity state never cached** — Every "is this light on?" check makes live HA REST call even if answer fetched seconds ago
2. **No event-driven cache invalidation** — 30s polling + manual invalidate. Up to 30s staleness window when entities change in HA
3. **Cold-start race condition** — DirectSkillExecutor has cache-ready gate, but orchestrator/LLM routing paths don't

### Cache Services Overview
- **EntityLocationService** — Floors, areas, entities, embeddings, visibility config, area→entity map (24h TTL Redis, 30s polling)
- **DeviceCacheService** — Climate, fans, music players, per-device embeddings (caller-supplied TTL)
- **SessionCacheService** — Conversation history per session (configurable sliding TTL)
- **PromptCacheService** — Routing decisions, chat responses (48h TTL)
- **IHomeAssistantClient** — NOT a cache, every call = live HA REST/WebSocket

### Identified Gaps (Prioritized)

**P0: Short-TTL Entity State Cache**
Add 5-15s read-through cache for GetEntityStateAsync() results. Eliminates most common live HA calls, resilient to brief HA hiccups.

**P1: WebSocket Event-Driven Invalidation**
Subscribe to HA state_changed events. When registry-level changes detected, trigger InvalidateAndReloadAsync() automatically. Eliminates 30s staleness window.

**P2: Startup Readiness Gate**
Return HTTP 503 from /api/conversation endpoint until AgentInitializationService completes. Prevents cold-start race.

**P3: Scene Cache Alignment**
Move SceneControlSkill.GetScenesAsync() to use IEntityLocationService instead of live GetStatesAsync().

**P4: Immutable Visibility Config**
Replace EntityVisibilityConfig.EntityAgentMap mutable Dictionary with ImmutableDictionary.

### Conclusion
Cache architecture fundamentally sound — "frail" feeling comes from **entity state never cached** (every state check goes live) and **no event-driven refresh**. Fixing P0 alone would dramatically reduce live HA calls and improve reliability.

---

## 2026-03-27: MAF Workflows Deep-Dive v2

**Author:** Ripley (Eval/Lead Architect)  
**Date:** 2025-10-13  
**Status:** Recommendation to proceed with Phase 1 refactor

### Question
Can we simplify orchestration by consolidating workflow construction into DynamicOrchestrationWorkflow service?

### Answer
**YES**, but complexity reduction is modest (10-15%, ~100-150 lines) because:
1. We're ALREADY using MAF Workflows — current implementation IS a proper MAF workflow
2. Business logic must stay — RouterExecutor (432 lines), AgentDispatchExecutor (299 lines), ResultAggregatorExecutor (269 lines) contain orchestration intelligence
3. Real complexity source is A2A infrastructure (~154 lines RemoteAgentInvoker + mesh config), not workflow construction

### What MAF Workflows Provides
- Graph execution (DAG), type safety, event system, conditional routing, fan-out/fan-in, nested workflows, durable workflows, streaming
- Does NOT provide: routing logic, agent execution, response aggregation, session management, agent resolution

### Current Architecture (2,753 total lines)
- Core Orchestration (1,377 lines): RouterExecutor, WorkflowFactory, AgentDispatchExecutor, LuciaEngine
- Aggregation (316 lines): ResultAggregatorExecutor + options
- Agent Invocation (340 lines): LocalAgentInvoker + RemoteAgentInvoker
- Session Management (145 lines): SessionManager
- Observer Infrastructure (206 lines)
- Configuration (369 lines)

### Proposed DynamicOrchestrationWorkflow Service
Consolidates agent resolution + executor caching into single service. WorkflowFactory (~348 lines) → DynamicOrchestrationWorkflow (~250 lines), net -98 lines.

**What Changes:** WorkflowFactory removed, agent discovery/invoker creation centralized, executor caching (Router/Aggregator singletons, Dispatch per-request)

**What Stays:** All three custom Executors, LocalAgentInvoker, SessionManager, LuciaEngine, Observer infrastructure

### Complexity Breakdown
| Component | Lines | Delete? |
|-----------|-------|---------|
| RouterExecutor | 431 | ❌ Contains routing logic |
| WorkflowFactory | 348 | ✅ Phase 1 consolidation |
| AgentDispatchExecutor | 299 | ❌ Parallel execution + clarification |
| LuciaEngine | 277 | ❌ Session lifecycle |
| ResultAggregatorExecutor | 269 | ❌ Response aggregation + personality |
| LocalAgentInvoker | 186 | ❌ In-process execution + NeedsInput |
| RemoteAgentInvoker | 154 | ✅ Phase 3 A2A removal |
| SessionManager | 145 | ❌ Redis session/task lifecycle |

**Phase 1 savings:** ~98 lines (3.5%)  
**Phase 1 + Phase 3:** ~252 lines (9%)

### Recommendation
**Proceed with Phase 1** — DynamicOrchestrationWorkflow refactor (3-5 days)

**Why:**
- 10-15% complexity reduction with no risk
- Foundation for Phase 2 executor caching
- Single-responsibility principle (workflow service vs session lifecycle)
- No architectural changes (custom executors stay intact)

**Why NOT full rewrite:**
- Custom executors contain business logic (must stay)
- A2A infrastructure is real complexity source (separate decision, Phase 3)
- Incremental value meaningful but not transformative

---

## 2026-03-27: User Directive — Entity Resolution Architecture Migration

**By:** Zack Way (via Copilot)  
**What:** Migrate entity resolution from heuristic scoring (HybridEntityMatcher) to cascading elimination architecture based on EMNLP 2024 research. Pipeline: location grounding → domain filtering → entity linking → coreference → disambiguation. Keep smart LLM fallback for: (1) high WER% STT transcriptions that fail deterministic matching, (2) complex/compound commands like "turn off the bedroom lights in 5 minutes" that need temporal/multi-step parsing. The deterministic pipeline handles simple commands; anything outside its confidence goes to the LLM orchestrator.

**Why:** Current global scoring with biases/margins is mathematically unsound — produces wrong results in real user scenarios (bedroom→bathroom, wrong speaker). Research shows cascading elimination outperforms global scoring for voice command disambiguation.

