# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant
- **Stack:** .NET 10, C# 14, ASP.NET Core, Aspire 13, Microsoft Agent Framework, Redis/InMemory, MongoDB/SQLite, OpenTelemetry
- **Created:** 2026-03-26

## Key Systems I Own

- `lucia.AgentHost/` — Main host with 40+ API endpoint groups
- `lucia.A2AHost/` — Satellite agent host for mesh mode
- `lucia.Agents/` — LightAgent, ClimateAgent, ListsAgent, SceneAgent, GeneralAgent, DynamicAgent, OrchestratorAgent
- `lucia.Data/` — Multi-backend data layer (Redis/InMemory cache, MongoDB/SQLite store)
- `plugins/` — Roslyn C# script plugin system (metamcp, brave-search, searxng)

## Architecture Notes

- Deployment modes: Standalone (all in AgentHost) vs Mesh (separate A2A agents)
- Agent orchestration: RouterExecutor pattern with multi-agent routing
- Conversation fast-path: CommandPatternRouter → DirectSkillExecutor → LLM fallback with SSE
- Two-tier prompt caching
- Model provider system: OpenAI/Azure/Ollama/Anthropic/Gemini/OpenRouter
- Plugin lifecycle: ConfigureServices → ExecuteAsync → MapEndpoints → OnSystemReadyAsync

## Learnings

<!-- Append new learnings below. -->

### 2025-07-17 — Conversation API Pipeline Audit

Completed full code audit of the Conversation API pipeline at Zack's request. Key findings:

1. **Pipeline flow**: `ConversationApi.cs` → `ConversationCommandProcessor` → `CommandPatternRouter` (Wyoming) → `CommandPatternMatcher` → `DirectSkillExecutor` → skill methods. LLM fallback via `LuciaEngine.ProcessRequestAsync()`.

2. **Pattern matcher is NOT regex** — it's a custom recursive token-matching engine in `lucia.Wyoming/CommandRouting/CommandPatternMatcher.cs` with segment types: Literal, OptionalLiteral, Capture, ConstrainedCapture, OptionalAlternatives.

3. **Only 3 skills have fast-path patterns**: LightControlSkill (toggle, brightness), ClimateControlSkill (set_temperature, adjust), SceneControlSkill (activate). Total of 5 pattern groups with ~12 templates.

4. **Climate entity resolution is broken in fast-path** — `ResolveEntityId()` expects `route.ResolvedEntityId` which is never set by the matcher; raw capture text gets passed as entity ID.

5. **Confidence formula**: `0.5 + 0.3*(constrained captures) + 0.1*(no leftovers) - 0.05*(leftover count)`. Leftover tokens from complex commands accidentally cause LLM fallback — useful but fragile.

6. **Fast-path is NOT sticky** — execution failures cascade to LLM orchestrator correctly (ConversationCommandProcessor.cs line 122).

7. **Multiple entity matches are all acted upon** — no disambiguation prompt exists in fast-path. LightControlSkill iterates all `allEntities.Values`.

8. **Report delivered to**: `.squad/decisions/inbox/parker-conversation-audit.md`

### 2025-07-17 — Exact-Match Entity Resolution Refactor

Refactored the conversation fast-path to eliminate fuzzy entity resolution per Zack's architecture decision. Key changes:

1. **Added `IsCacheReady`, `ExactMatchEntities()`, `ExactMatchArea()` to `IEntityLocationService`** — synchronous, cache-only lookups that never trigger a load. Implemented in `EntityLocationService` (Redis), `InMemoryEntityLocationService`, and the test double `SnapshotEntityLocationService`.

2. **`DirectSkillExecutor` now gates on cache readiness** — if the entity cache isn't loaded, the executor returns a `Bail` result immediately (`cache_miss`). If cache is loaded but no exact match is found, it bails with `no_exact_match`. Zero fuzzy search in the executor.

3. **`CommandPatternMatcher` now rejects transcripts with bail-signal tokens** — temporal words (`in`, `at`, `when`, `after`, `minutes`, etc.), color words (`red`, `blue`, `green`, `warm`, `cool`, `color`), and multi-step conjunctions (`and`, `then`, `also`) cause immediate `NoMatch` return before any template matching begins. This prevents the pattern matcher from silently ignoring components it can't handle (e.g., "turn off lights in 5 minutes" matching but ignoring the timer).

4. **`SkillExecutionResult` gained `BailReason` property and `Bail()` factory** — enables the processor to distinguish between execution errors and intentional fast-path deferrals.

5. **`ConversationCommandProcessor` tags `fast_path_bail_reason`** on the conversation activity when a bail occurs, enabling observability on why the fast-path deferred to LLM.

6. **Climate entity resolution fixed** — `ResolveEntityIdFromCache()` validates captured entity text against the cache instead of blindly passing raw capture text as a HA entity ID.

7. **New exception type `EntityResolutionBailException`** — clean internal signal for bail conditions that get caught and converted to `SkillExecutionResult.Bail()` results.

**Design principle**: The fast-path now follows "instant if certain, orchestrator if not" — it never acts on an entity it isn't 100% sure about.

### 2025-07-17 — Entity Matching Bug Investigation (Bedroom Lights / Office Speaker)

Investigated two bugs where entity resolution picks the wrong device: "bedroom lights" controlling bathroom lights, and music playing on the wrong speaker. Traced the full pipeline: `LightControlSkill.ControlLightsAsync` → `EntityLocationService.SearchHierarchyAsync` → `HybridEntityMatcher.FindMatchesAsync` → `StringSimilarity.HybridScore`.

Key findings:

1. **Embedding mismatch is the primary root cause.** Stop words ("light", "lights", "lamp") are stripped for token-core string similarity but NOT for embedding generation. The embedding of "bedroom lights" is computed from the full phrase, making it semantically closer to entity "Bathroom Light" than to area "Bedroom". This reverses the string-score advantage that area "Bedroom" has.

2. **Path-selection logic in `SearchHierarchyAsync` (line 398–401) biases toward entities.** Entity path wins unless area beats it by `EmbeddingResolutionMargin` (0.10 for lights). In close races, entity always wins — the margin acts as a handicap against areas.

3. **Old `FindLightsByAreaAsync` / `FindLightAsync` approach was removed** — stubs exist at `LightControlSkill.cs:132–157` throwing `NotSupportedException`. The old design had the LLM explicitly split area from entity name. The current single-tool design passes raw user words ("bedroom lights") as a single search term.

4. **Music agent uses the exact same pipeline** via `MusicPlaybackSkill.ResolvePlayerAsync` → `SearchHierarchyAsync`. Same vulnerability, though `EmbeddingResolutionMargin = 0.30` provides slightly more area bias.

5. **Proposed fix** (in `.squad/decisions/inbox/parker-entity-matching-fix.md`): (A) Strip stop words before generating embeddings, and (B) swap path priority to area-first in `SearchHierarchyAsync`. Both changes are minimal and apply to all agents using the shared matcher.

### 2026-03-27 — Cascading Entity Resolver Implementation

Implemented the cascading entity resolution pipeline with deterministic query decomposition, location grounding, domain filtering, and exact/phonetic/token matching. Added the UseCascadingResolver feature flag (FeatureManagement) to gate the new path, and wired speaker identity expansions ("my X" → "{SpeakerId}'s X") plus caller-area grounding for fast-path resolution.
