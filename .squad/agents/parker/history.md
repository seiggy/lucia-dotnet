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

### 2025-07-18 — Microsoft Agent Framework RC4 / A2A 1.0 Migration

Migrated the entire solution from A2A SDK 0.3.4-preview to 1.0.0-preview and MAF packages to RC4. The A2A 1.0 spec introduced breaking changes across the board:

## Learnings

**Type renames (A2A 0.3.x → 1.0):**
- `AgentMessage` → `Message`
- `AgentTaskStatus` (struct) → `TaskStatus` (class) — use `A2A.TaskStatus` to avoid ambiguity with `System.Threading.Tasks.TaskStatus`
- `MessageSendParams` → `SendMessageRequest`
- `MessageRole` (enum) → `Role` (enum, values: `Unspecified=0, User=1, Agent=2`)
- `ITaskManager` / `TaskManager` → **removed** — replaced by `IA2ARequestHandler` / `A2AServer`
- `TaskQueryParams` → `GetTaskRequest`
- `TextPart` → **removed** — `Part` is now a unified type with `ContentCase` discriminator; use `new Part { Text = "..." }` and `p.ContentCase == PartContentCase.Text`

**AgentCard changes:**
- `AgentCard.Url` → **removed** — agent URL now lives in `AgentCard.SupportedInterfaces[0].Url` via `AgentInterface`
- `AgentCard.SupportsAuthenticatedExtendedCard` → removed
- `AgentCard.ProtocolVersion` → removed
- `AgentCapabilities.StateTransitionHistory` → removed
- `AgentCapabilities.PushNotifications` and `.Streaming` now `bool?` (nullable)

**ITaskStore interface (A2A 1.0):**
- `SetTaskAsync(AgentTask)` → `SaveTaskAsync(string taskId, AgentTask task, CancellationToken)`
- `UpdateStatusAsync(...)` → removed
- Push notification methods → removed from ITaskStore
- New: `DeleteTaskAsync(string taskId, CancellationToken)`, `ListTasksAsync(ListTasksRequest, CancellationToken)` → `Task<ListTasksResponse>`

**A2AServer replaces TaskManager:**
- Constructor: `A2AServer(IAgentHandler, ITaskStore, ChannelEventNotifier, ILogger<A2AServer>, A2AServerOptions)`
- `IAgentHandler.ExecuteAsync(RequestContext, AgentEventQueue, CancellationToken)` replaces `OnMessageReceived` event
- Use `TaskUpdater` / `MessageResponder` to write responses to `AgentEventQueue`

**Routing:**
- `A2ARouteBuilderExtensions.MapA2A(endpoints, IA2ARequestHandler, path)` replaces the old `MapA2A(endpoints, ITaskManager, path)`
- `MapHttpA2A(endpoints, IA2ARequestHandler, AgentCard, path)` for combined card + handler
- `MapWellKnownAgentCard(endpoints, AgentCard, path)` for standalone card endpoints

**SessionManager** now depends on `ITaskStore` directly instead of `ITaskManager`, since task CRUD is all it needs. Task creation uses direct `new AgentTask { ... }` + `SaveTaskAsync()`.

### 2025-07-18 — SupportVoiceTags & Config API for Personality Settings

Added `SupportVoiceTags` flag to `CommandRoutingOptions` and wired it through the personality pipeline:

1. **`CommandRoutingOptions.SupportVoiceTags`** — new `bool` property (default `false`). When enabled, the personality renderer instructs the LLM to include SSML tags (`<break>`, `<emphasis>`, prosody) in its output for TTS rendering.

2. **`PersonalityResponseRenderer`** — now injects a voice-tag instruction into the LLM user message. When `SupportVoiceTags` is true, it asks for SSML; when false, it explicitly requests plain text only. This keeps the rendering deterministic regardless of LLM tendencies.

3. **`ConfigurationApi` schema** — added a `Wyoming:CommandRouting` section to the schema endpoint, exposing all 7 personality-related settings (Enabled, ConfidenceThreshold, FallbackToLlm, UsePersonalityResponses, PersonalityPrompt, PersonalityModelConnectionName, SupportVoiceTags). The existing generic GET/PUT section endpoints already serve this section — no new endpoints were needed.

4. **`appsettings.json`** — added `SupportVoiceTags: false` to the `Wyoming:CommandRouting` block.

**Key insight:** The existing `ConfigurationApi` is section-generic — `GET /api/config/sections/Wyoming:CommandRouting` and `PUT /api/config/sections/Wyoming:CommandRouting` already work for any section. The missing piece was the schema definition for the dashboard to render a proper form, which was the only code addition needed in ConfigurationApi.
