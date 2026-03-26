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
