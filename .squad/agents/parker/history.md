# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant
- **Stack:** .NET 10, C# 14, ASP.NET Core, Aspire 13, Microsoft Agent Framework, Redis/InMemory, MongoDB/SQLite, OpenTelemetry
- **Created:** 2026-03-26

## Key Systems Owned

- lucia.AgentHost/ — Main host with 40+ API endpoint groups
- lucia.A2AHost/ — Satellite agent host for mesh mode
- lucia.Agents/ — 7 built-in agents (Light, Climate, Lists, Scene, General, Dynamic, Orchestrator)
- lucia.Data/ — Multi-backend data layer (Redis/InMemory cache, MongoDB/SQLite store)
- lucia.Wyoming/ — Speech runtime, command routing, Wyoming protocol

## Key Decisions & Fixes

### 2026-05-29: Hosts & Agent-Cores Health Review

Health findings from whole-solution review:
- Scalar/OpenAPI browsable pre-auth in both hosts (need IsDevelopment() gate)
- InternalTokenAuthenticationHandler uses non-constant-time string comparison (vs HmacSessionService's FixedTimeEquals)
- DateTime.Now vs UtcNow mixed comparisons in MusicAgent (line 199/213)
- HmacSessionService generates transient signing keys (breaks cross-instance sessions)
- ScheduledTaskService removes tasks before firing (loses exceptions on OCE)
- Positive patterns: AgentProxyApi SSRF allowlist, AgentRegistrationHealthCheck diagnostics, TimeProvider + ConcurrentDictionary scheduler

### 2026-05-25: Agent Timeout Handling for Bug #106

AgentInvokerOptions.Timeout defaults to **30 seconds** (not 9-10s hardcoded). Most likely failures are upstream cancellations (voice timeout, HTTP disconnect) propagating into orchestration.

**Fixes:**
1. Map OperationCanceledException to descriptive user-facing failures in both LocalAgentInvoker and RemoteAgentInvoker
2. Use CancellationToken.None in ResultAggregatorExecutor event recording so late request cancellation doesn't prevent graceful aggregation

### 2026-05-18: PR #116 Cleanup for Merge

Cherry-picking older PR required stripping accidental repo artifacts:
- Remove tracked .onnx model binaries
- Remove backup ite.config.ts.bak
- Drop malformed lucia-dashboard/obj path entry
- Discard duplicate HtmlReportGenerator (keep lucia.EvalHarness.Reports version)

### Previous Releases

- **EXCLUDE_SPEECH flag** — Jetson/ARM64 builds preserve command routing types while dropping ONNX/Sherpa voice runtime
- **Embedding provider switches** — force Redis cache bypass + rebuild from HA state when SensorControlSkill switches providers
- **Conversation API audit** — 3 skills have fast-path patterns (Light/Climate/Scene); pattern matcher is custom token engine, not regex
- **Config poll intervals** — increased default 5s→30s; Mongo provider uses volatile bool race condition (fixed to Interlocked)
- **SQLite NULL safety** — SUM()/AVG() return NULL on empty tables; guard all aggregate reads with IsDBNull

### Architecture Notes

- Deployment modes: Standalone (all in AgentHost) vs Mesh (separate A2A agents)
- Conversation fast-path: CommandPatternRouter → DirectSkillExecutor → LLM fallback with SSE
- Two-tier prompt caching (routing + chat)
- Model provider system: OpenAI/Azure/Ollama/Anthropic/Gemini/OpenRouter

- Participated in 2026-05-29 health review
---

**Update from Ripley (2026-05-30):** Inbox retriage complete. You have been assigned issues from the 2026-05-30 batch. Review .squad/decisions/decisions.md for details.

### 2026-05-30: Issue #176 — Validate agentId Uri returns 400 instead of 500

**File changed:** `lucia.AgentHost/Apis/AgentRegistryApi.cs`

**Fix approach:** Replaced both `new Uri(agentId)` calls (lines 71 and 140 in the original) in `RegisterAgentAsync` and `UpdateAgentAsync` with `Uri.TryCreate(agentId, UriKind.Absolute, out var agentUri)`. On failure the handlers return `TypedResults.BadRequest(...)` with a clear message. The guard in `RegisterAgentAsync` is placed immediately after the whitespace check; in `UpdateAgentAsync` it's placed before the registry lookup so we fail fast before any async I/O.

**Incidental fix:** `Nerdbank.MessagePack` bumped 1.1.62 → 1.2.4 in `Directory.Packages.props` to clear pre-existing NU1902 vulnerability audit errors that blocked `dotnet build` on the branch. PR #191.
## 2026-05-31 — PR #195 Personality Guardrail Narrowing

Narrowed personality guardrail C1 in ResultAggregatorExecutor.cs with task-scoped 'never decline rephrasing' rule. Updated test assertions. Consolidated with Ripley/Hicks into commit 9809a36.
