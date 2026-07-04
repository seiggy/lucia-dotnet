# Parker's Work History — Active & Recent

## Project Context

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

## Recent Learnings (June 2026+)

## Learnings

### 2026-06-01: InputRequired Task Timeout — Background Sweeper

**Task system location:** `TaskState.InputRequired` is set in `lucia.Agents/Orchestration/LuciaEngine.cs:188` when `workflowResult.NeedsInput == true`. State is persisted via `ITaskStore` (A2A package) — backed in production by `ArchivingTaskStore → RedisTaskStore` (`lucia.Agents/Integration/RedisTaskStore.cs`).

**Root cause:** Before this fix, a task in `InputRequired` had no timeout. It sat indefinitely until the 24h Redis TTL expired (line 158 of `RedisTaskStore.cs`). `TaskArchivalService` only sweeps terminal states (Completed, Failed, Canceled).

**Fix — background sweeper pattern:** `InputRequiredTimeoutService : BackgroundService` in `lucia.Agents/Services/`. A per-task `CancellationTokenSource` was rejected because Redis-backed tasks survive process restarts — a CTS would be lost. The sweeper loops on a configurable interval, reads all task IDs from `ITaskIdIndex`, and cancels any `InputRequired` tasks older than the configured timeout.

**Config options:** `InputRequiredTimeoutOptions` in `lucia.Agents/Configuration/`, section key `InputRequiredTimeout`.
- `Timeout` — default `00:00:30` (30 seconds)
- `SweepInterval` — default `00:00:10` (10 seconds)
Override via appsettings: `"InputRequiredTimeout": { "Timeout": "00:05:00" }`.

**TimeProvider seam:** Sweeper calls `_timeProvider.GetUtcNow()` for both elapsed calculation and for the Canceled timestamp written to the task. `A2A.TaskStatus.Timestamp` is `DateTimeOffset?` — pattern match `if (task.Status.Timestamp is not { } enteredAt) continue;` guards tasks saved without a timestamp.

**Idempotency / concurrency:** Double-check re-read (`GetTaskAsync` a second time) before writing Canceled. If input arrived concurrently between first and second read, state will differ → skip. Subsequent sweeps skip already-Canceled tasks (`State != InputRequired`).

**Key files:**
- `lucia.Agents/Configuration/InputRequiredTimeoutOptions.cs` — options, defaults, section name
- `lucia.Agents/Services/InputRequiredTimeoutLogMessages.cs` — compile-time `[LoggerMessage]`
- `lucia.Agents/Services/InputRequiredTimeoutService.cs` — sweeper; `SweepAsync` is `internal` for direct test access
- `lucia.Agents/Extensions/ServiceCollectionExtensions.cs` — registration (`Configure<>` + `AddHostedService<>`)
- `lucia.Tests/Services/InputRequiredTimeoutServiceTests.cs` — 12 tests; `FakeTimeProvider` + `lucia.Data.InMemory.InMemoryTaskStore`; no real sleeps

### 2026-07-01: Aspire 13.4 Redis — Server-HTTPS vs Client-Trust Are Separate APIs (branch: fix/package-updates-build)

Aspire 13.4 separated Redis certificate handling into two independent APIs:
- **Server HTTPS cert** → `.WithoutHttpsCertificate()` — disables TLS on the server endpoint.
- **Client cert trust** → `.WithCertificateTrustScope(CertificateTrustScope.None)` — disables CA certificate injection into the container.

Disabling **only** the server side still injects `--tls-ca-cert-file /usr/lib/ssl/aspire/cert.pem` into the container command, leaving the endpoint flagged for TLS. The built-in `redis_check` health check then attempts a TLS handshake against the plaintext server → EOF → UNHEALTHY. Full plaintext opt-out requires **both** `.WithoutHttpsCertificate()` + `.WithCertificateTrustScope(CertificateTrustScope.None)`.

`CertificateTrustScope` lives in `Aspire.Hosting.ApplicationModel`; in practice it's resolved via the Aspire.AppHost.Sdk global usings so no explicit `using` is needed in `AppHost.cs`.

**Key file:** `lucia.AppHost/AppHost.cs` — Redis `AddRedis("redis")` chain.

### 2026-07-01: Postgres Image Pinned to Tag "17" to Prevent Volume Incompatibility (branch: fix/package-updates-build)

`Aspire.Hosting.PostgreSQL` 13.4.2 changed the default container image to `postgres:18.3`. The existing dev data volume (`lucia.apphost-43be2f4b46-postgres-data`) was created under Postgres 17's on-disk format and is incompatible with Postgres 18, causing the container to exit with code 1 on startup.

**Fix:** Added `.WithImageTag("17")` to the `AddPostgres` chain in `lucia.AppHost/AppHost.cs` (immediately after `AddPostgres("postgres")`). This pins the image to `postgres:17` regardless of which tag the Aspire integration defaults to.

**Why pinning matters:** Without an explicit tag, any future bump to `Aspire.Hosting.PostgreSQL` that changes the default image tag can silently pull a new Postgres major version and break existing persistent data volumes. Always pin `.WithImageTag(...)` when using `WithDataVolume()` and `WithLifetime(ContainerLifetime.Persistent)`.

**Key file:** `lucia.AppHost/AppHost.cs` — `builder.AddPostgres("postgres").WithImageTag("17")`

### 2026-07-01: MessagePack / Microsoft.OpenApi / SQLitePCLRaw Transitive Pins (branch: fix/package-updates-build)

Three transitive packages produced NU1902/NU1903 vulnerability errors that became build errors via `TreatWarningsAsErrors`. Fixed all three via `CentralPackageTransitivePinningEnabled` in `Directory.Packages.props`:

- **MessagePack 2.5.198 → 2.5.302**: Transitive via `StreamJsonRpc 2.24.84` ← `GitHub.Copilot.SDK 0.2.1-preview.1` / `Microsoft.Agents.AI.GitHub.Copilot`. Stayed in the 2.5.x line for StreamJsonRpc compatibility (do NOT use 3.x here).
- **Microsoft.OpenApi 2.0.0 → 2.7.5**: Transitive via `Microsoft.AspNetCore.OpenApi 10.0.8`. GHSA-v5pm-xwqc-g5wc (stack overflow on circular refs). Patched at 2.7.5 per advisory.
- **SQLitePCLRaw.lib.e_sqlite3 2.1.11 → 3.50.3**: Transitive via `Microsoft.Data.Sqlite 10.0.8` → `SQLitePCLRaw.bundle_e_sqlite3 2.1.11`. Author switched versioning scheme to track SQLite version; 3.50.3 is the first patched release above 2.1.11. Runtime safe: SQLite has stable C API across versions.

All three pins added to the `Misc` ItemGroup (SQLitePCLRaw, MessagePack) and `Microsoft Extensions` ItemGroup (OpenApi). Build result: **0 errors, 0 warnings**.

### 2026-06-01: InputRequired Timeout Default Changed to 1 Minute (parker-8 follow-up)

**Changed from:** 30 seconds (parker-7 initial default)  
**Changed to:** 1 minute (TimeSpan.FromMinutes(1))  
**Rationale (per Zack voice-engine reasoning):**
- Typical human voice reply after LLM output: 6–10 seconds
- Long speech-to-text utterance: rarely exceeds 15–20 seconds
- 30 seconds was too tight; slow speaker or noisy environment could lose race
- 5 minutes too lenient; sessions hang too long in voice loop
- **1 minute is the safe margin** that won't cut off realistic voice interactions

**File updated:** `lucia.Agents/Configuration/InputRequiredTimeoutOptions.cs` (default parameter changed)  
**Tests:** All 12 tests still pass; they use explicit overrides, not defaults  
**Build:** 0 warnings, 0 errors  

**Deployment guidance:** Non-voice deployments (chat UI, async input) should override via configuration to suit their response cadence.
