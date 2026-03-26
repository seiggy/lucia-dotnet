# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant
- **Stack:** Python 3.x (aiohttp), .NET 10/C# 14, Home Assistant REST/WebSocket APIs
- **Created:** 2026-03-26

## Key Systems I Own

- `custom_components/lucia/` — HA custom component
  - `__init__.py` — integration setup
  - `config_flow.py` — UI config flow
  - `conversation.py` — conversation platform
  - `fast_conversation.py` — fast-path conversation
  - `conversation_tracker.py` — multi-turn context mapping
  - `a2a_payload.py` — A2A protocol payloads
  - `tests/` — component tests
- `lucia.HomeAssistant/` — .NET HA client
  - `IHomeAssistantClient` — full HA API surface (states, services, events, history, calendars, registry, media, todo, etc.)
  - `HomeAssistantOptions` — config with SSL toggle and bearer auth
  - Typed models for entities, states, services, areas, devices

## HA Integration Points

- Conversation API: HA → lucia `/api/conversation` (API key auth)
- Service: `lucia.send_message`
- Events: `lucia_conversation_input_required` (follow-up input)
- Entity registries: area, entity, device (for entity matching)
- Entity visibility/exposure filtering
- Test snapshots: `scripts/Export-HomeAssistantSnapshot.ps1`

## Learnings

<!-- Append new learnings below. -->

### 2026-03-27 — Cache Architecture Investigation

**Context:** Zack reported the system "feels frail" and suspected entity caching wasn't being used effectively.

**Key findings:**
1. **Entity location data (floors, areas, entity-to-area mapping) IS well-cached** via `IEntityLocationService` — Redis-backed with 24h TTL, immutable snapshots, startup warmup, and 30s version polling.
2. **Entity current state is NEVER cached.** Every skill calls `GetEntityStateAsync()` live to HA on every invocation. This is the #1 source of unnecessary HA load and fragility.
3. **No event-driven cache invalidation.** All refresh is manual or 30s polling. No HA WebSocket `state_changed` subscription exists in the backend.
4. **Cold-start race exists** — orchestrator path has no cache-readiness gate (only `DirectSkillExecutor` checks `IsCacheReady`).
5. **Cache failure mode is fail-open** — Redis failures are logged and the system serves in-memory data. HA failures with empty cache = hard fail with 30s retry.
6. **All cache services are singletons** with immutable snapshot + atomic swap pattern.
7. **SceneControlSkill bypasses cache** for scene listing, calling `GetStatesAsync()` directly.
8. **`EntityVisibilityConfig.EntityAgentMap`** is a mutable dictionary — theoretical race with concurrent reads/writes.

**Report:** `.squad/decisions/inbox/bishop-cache-investigation.md`

### 2025-07-23 — Prompt Cache Investigation

- The "two-tier prompt caching" system has two independent layers: **Routing Cache** (which agent to invoke) and **Chat/Agent Cache** (which tool calls to make). Both support exact SHA256 + semantic embedding similarity fallback.
- The routing cache key is derived only from the user's last utterance line (not HA context), while the chat cache key includes the full system instructions hash + all message content.
- **Primary finding:** There is NO automatic invalidation when agent definitions, model providers, or HA entity topology change. Stale entries persist for up to 48h (TTL). This is the most likely cause of the "frail" feeling Zack described.
- `PromptCachingChatClient` only caches tool-call plans, never text responses. Tool results rounds always bypass cache. Tools always execute fresh.
- `InMemoryPromptCacheService` lacks OTel metric instrumentation (counters, histograms) that the Redis implementation has — observability blind spot in non-Redis deployments.
- Report delivered to `.squad/decisions/inbox/bishop-prompt-cache-investigation.md`.
