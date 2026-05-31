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

## Learnings

### 2026-05-31: .env → AppHost → AgentHost Config Flow

**Problem:** `DASHBOARD_API_KEY` set in repo-root `.env` never reached the `lucia-agenthost` process, so `SeedSetupFromEnvAsync` never seeded the Dashboard key and login failed.

**Root cause:** The Aspire AppHost had no `.env` loading — `DistributedApplication.CreateBuilder(args)` only reads system env vars and `appsettings*.json`. It does NOT auto-load `.env`. Without `DotNetEnv` (or equivalent), values from `.env` are invisible to both `builder.Configuration` and `Environment.GetEnvironmentVariable()` inside the AppHost process.

**Fix — three components:**
1. **DotNetEnv 3.2.0** added to `Directory.Packages.props` and `lucia.AppHost.csproj`. Called at the very top of `AppHost.cs` before `CreateBuilder`:
   `Env.NoClobber().TraversePath().Load();`
   `TraversePath()` walks up from the AppHost's working directory until it finds a `.env`. `NoClobber()` ensures real process-env values are never overwritten by the file.
2. **Forwarding block** added to `AppHost.cs` after `registryApi` is built. Reads five seed vars via `Environment.GetEnvironmentVariable()` (not `builder.Configuration[...]` — the latter normalizes `__` to `:` breaking double-underscore key names) and conditionally chains `.WithEnvironment(name, value)` only when non-empty.
3. **AgentHost side** was already correct: `WebApplication.CreateBuilder(args)` includes `AddEnvironmentVariables()` by default, so Aspire-injected env vars land in `IConfiguration`. `SeedSetupFromEnvAsync` at `Program.cs:413` reads `configuration["DASHBOARD_API_KEY"]` (single underscores → no normalization issue).

**Seed re-entrance caveat:** `MongoApiKeyService.CreateKeyFromPlaintextAsync` guards with `existingKeys.Any(k => k.Name == name && !k.IsRevoked)`. If a non-revoked "Dashboard" key already exists in the MongoDB volume, a new env-provided key will NOT be seeded. The log line "Seeded Dashboard API key from DASHBOARD_API_KEY" will NOT appear. To force re-seed, the existing Dashboard key must be revoked via API (`DELETE /api/keys/{id}`) or the MongoDB volume cleared (`docker volume rm <volume>`).

**Key file paths:**
- `.env` loading: `lucia.AppHost/AppHost.cs` (top, before CreateBuilder)
- Forwarding wiring: `lucia.AppHost/AppHost.cs` (after registryApi definition)
- Seed logic: `lucia.Agents/Extensions/SetupSeedExtensions.cs` → `SeedSetupFromEnvAsync`
- Seed call site: `lucia.AgentHost/Program.cs:413`
- Key storage/validation: `lucia.Agents/Auth/MongoApiKeyService.cs`
- Login endpoint: `lucia.AgentHost/Apis/AuthApi.cs` → `LoginAsync` → `ValidateKeyAsync` (SHA-256 hash match)

### 2026-05-31: Dashboard API Key Override / Reset Semantics

**Problem:** When `DASHBOARD_API_KEY` was set in `.env` but a stale (different-plaintext) Dashboard key already existed in MongoDB, `CreateKeyFromPlaintextAsync` silently returned null (gate: `existingKeys.Any(k => k.Name == name && !k.IsRevoked)`). Login with the env value failed with no helpful log.

**Fix:** Added `OverrideKeyFromPlaintextAsync(string name, string plaintextKey)` to `IApiKeyService`. When `DASHBOARD_API_KEY` is set, `SeedSetupFromEnvAsync` calls this instead of `CreateKeyFromPlaintextAsync`. The override logic:
1. Hash the plaintext and check if a non-revoked same-name key already has that exact hash → no-op (already correct).
2. If not: `UpdateMany` all non-revoked same-name keys to `isRevoked=true` (bypasses lockout — we are always creating a replacement), then inserts the new key.
3. Concurrent calls: Mongo catches `MongoWriteException.DuplicateKey` on the insert; SQLite uses `INSERT OR IGNORE`; Postgres uses `ON CONFLICT DO NOTHING`. All converge to exactly one active Dashboard key.

**IApiKeyService methods involved:**
- NEW: `OverrideKeyFromPlaintextAsync(name, plaintextKey, ct)` → `(ApiKeyCreateResponse? Created, int RevokedCount)`
  - `(null, 0)` = no-op (env key already matched)
  - `(response, 0)` = first-time create
  - `(response, N)` = reset (revoked N prior keys)
- Existing `ListKeysAsync` and `ValidateKeyAsync` unchanged; lockout-guarded `RevokeKeyAsync` is NOT used (we bypass via direct DB update in the override method).

**Idempotency:** Two concurrent calls with the same plaintext will both revoke old keys (safe — each UpdateMany targets the same rows), and both try to insert the same hash. The second insert fails with DuplicateKey / ignored row, returning `(null, 0)`. Net effect: exactly one active Dashboard key, no throw.

**Log lines (greppable from AgentHost):**
- `Dashboard API key already matches DASHBOARD_API_KEY; no reset needed` — env matches existing
- `Reset Dashboard API key from DASHBOARD_API_KEY (revoked {Count} prior key(s))` — override happened
- `Seeded Dashboard API key from DASHBOARD_API_KEY` — first-time create

**Key file paths:**
- Interface: `lucia.Agents/Abstractions/IApiKeyService.cs`
- Mongo impl: `lucia.Agents/Auth/MongoApiKeyService.cs` → `OverrideKeyFromPlaintextAsync`
- Cache decorator: `lucia.Agents/Auth/CachedApiKeyService.cs` (delegates + invalidates)
- SQLite impl: `lucia.Data/Sqlite/SqliteApiKeyService.cs`
- Postgres impl: `lucia.Data/PostgreSQL/PostgresApiKeyService.cs`
- Seed logic: `lucia.Agents/Extensions/SetupSeedExtensions.cs` (now `partial`, uses `[LoggerMessage]`)
- Tests: `lucia.Tests/Auth/SetupSeedExtensionsTests.cs` (6 tests, real SQLite backing)
