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
