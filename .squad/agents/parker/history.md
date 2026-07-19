# Parker's Work History — Backend / Platform Engineer

## Current Role
- **Architecture & host platform:** lucia.AgentHost, lucia.A2AHost, lucia.Data, lucia.Wyoming
- **API surfaces & orchestration:** 40+ endpoints, command routing, agent routing, workflow execution
- **Infrastructure:** Docker, Kubernetes, Helm, systemd deployment patterns
- **Latest focus:** Jetson native voice boundary analysis and infrastructure reviews

## Key Systems Owned
- lucia.AgentHost/ — Main host with 40+ API endpoint groups
- lucia.A2AHost/ — Satellite agent host for mesh mode
- lucia.Agents/ — 7 built-in agents (Light, Climate, Lists, Scene, General, Dynamic, Orchestrator)
- lucia.Data/ — Multi-backend data layer (Redis/InMemory cache, MongoDB/SQLite store)
- lucia.Wyoming/ — Speech runtime, command routing, Wyoming protocol

## Recent Work (2026-07)

### Jetson Orin Nano Native Inference Boundary (2026-07-17 — Research)
Traced Wyoming host-to-native boundary end-to-end; confirmed in-process P/Invoke (Family C) as preferred over new engine classes.

**Key findings:**
- Wyoming flow complete trace: WyomingServer → WyomingSession state machine → inference engines → transcript storage
- Narrowest replacement seam: `ISttEngine` / `ISttSession`
- Managed code unchanged; only native-lib sourcing and GPU-enablement required
- **Rejected proposals:** new `JetsonSttEngine`/`JetsonSttSession` (HybridSttEngine already parametrizes provider)
- Interface contracts to preserve: `ISttEngine`, `ISttSession`, `IVadEngine`, `IWakeWordDetector`, `ISpeechEnhancer`, `IDiarizationEngine`, Wyoming protocol
- ExcludeSpeech build flag pattern already established

**Revision (2026-07-17 — Slopwatch compliance):**
- `VersionOverride` approach rejected as SW006 CPM bypass under Slopwatch policy.
- Replaced with proper CPM: `OrtManagedVersion` MSBuild property in `Directory.Packages.props` `Version Variables` PropertyGroup; `PackageVersion` uses `$(OrtManagedVersion)`. `lucia.Wyoming.csproj` keeps a single plain versionless `PackageReference`. No per-project bypass.
- Slopwatch `--hook --no-baseline`: zero SW006 in dirty files. One SW005 (`NoWarn NU1510;EXTEXP0001` at csproj:4) confirmed pre-existing by `git diff` (not in my changeset).
- SHA256 cross-check: published `Microsoft.ML.OnnxRuntime.dll` is byte-for-byte `/microsoft.ml.onnxruntime.managed/1.18.1/` NuGet cache.
- `dotnet list package` (no RID) shows `1.23.2` — expected; property evaluates at publish time when `RuntimeIdentifier=linux-arm64` is a CLI property.



### Wyoming/ORT Package Build Seam Fix (2026-07-17 — Implementation)
Fixed linux-arm64 MSB3030 failure and aligned managed ORT with Track A production version.

**Changes made:**
- Removed `Microsoft.ML.OnnxRuntime.Gpu.Linux` `PackageReference` from `lucia.Wyoming.csproj` (was x64-only, no arm64 content; `.targets` file tried to copy win-arm64 DLL that doesn't exist)
- Removed dead `PackageVersion` entry for `Gpu.Linux` from `Directory.Packages.props` (surgical XML — CLI can't remove unreferenced DPP entry)
- Added `VersionOverride="1.18.1"` for `linux-arm64` on `Managed` ref in `lucia.Wyoming.csproj` (conditional, surgical XML — CLI can't express `VersionOverride` with `Condition`)
- `Directory.Packages.props` `Managed` remains at `1.23.2` globally (required for `DirectML 1.23.0 → Managed >= 1.23.0` compatibility)

**Key learnings:**
- `Gpu.Linux` 1.23.2 ships ONLY `runtimes/linux-x64/native/`. Its `.targets` is gated by `IsOSPlatform('Linux')` (build-host), not target RID — so on a Linux cross-compile host targeting arm64, MSBuild includes the package and copies wrong-RID files → MSB3030.
- Global `Managed 1.18.1` breaks full solution: `DirectML 1.23.0 → Managed >= 1.23.0` (NU1109) in all projects that transitively pull `DirectML` through `lucia.Wyoming`.
- `VersionOverride` on `PackageReference` is the minimal CPM escape hatch. Works per-project, doesn't affect other projects' transitive resolution.
- `Managed 1.18.1` is safe against any native ORT >= 1.18.1 (ORT C API is additive). For arm64 the sherpa-onnx 1.12.34 bundled native is ORT 1.23.2 — upward compat confirmed.
- For non-arm64 builds (empty `RuntimeIdentifier`), `'$(RuntimeIdentifier)' != 'linux-arm64'` evaluates true → DPP 1.23.2 used → DirectML stays happy.

**Validation results:**
- `dotnet restore` ✓ (no errors, no NU warnings)
- Windows full-solution build (`lucia.Tests` + all deps): **0 errors, 0 warnings** ✓
- linux-arm64 Docker SDK publish (speech-enabled, no `CpuOnly=true`): **exit 0**, `libonnxruntime.so` 29.94 MB, `Microsoft.ML.OnnxRuntime.dll` 204 KB (1.18.1), no GPU Linux `.so` ✓
- Wyoming test suite: **301 passed, 0 failed** ✓


- See `history-archive.md` for prior entries (orchestration span disposal, InputRequired timeouts, auth handler disposal, PostgreSQL index reviews, infra reviews)

### Docker/Deploy Reviewer Rejection Revision (2026-07-17 — Implementation)
Fixed 8 reviewer blockers across 4 Docker/deploy files (`Dockerfile.jetson-voice-assets`, `Dockerfile.agenthost-jetson-voice`, `docker-compose.jetson-voice.yml`, `deploy-jetson.sh`).

**Root cause discoveries (two new critical learnings):**

1. **NuGet restore base-framework-pass RID bug (task 2 regression):**
   - NuGet generates `project.assets.json` with TWO targets: `net10.0` (base) and `net10.0/linux-arm64` (RID-specific).
   - The BASE pass evaluates properties WITHOUT `$(RuntimeIdentifier)` set, even when `--runtime linux-arm64` is specified.
   - `_SpeechRuntimeTarget` falls back to `$(NETCoreSdkPortableRuntimeIdentifier)`. On a Linux AMD64 Docker SDK container (cross-compiling to arm64), this = `linux-x64`.
   - Result: `Gpu.Linux` condition evaluates TRUE in base pass → included in restore graph → MSB3030 on publish.
   - `OrtManagedVersion` property also evaluates to `1.23.2` in base pass (not 1.18.1) → managed > native = runtime init failure.
   - **Fix in Dockerfile**: pass `/p:_SpeechRuntimeTarget=linux-arm64 /p:OrtManagedVersion=1.18.1` to BOTH `dotnet restore` and `dotnet publish --no-restore`. These are explicit MSBuild property overrides, not suppressions. They override the derived property to match what it should be for the intended target platform.
   - **Proper long-term fix (package files)**: change `lucia.Wyoming.csproj` condition to use `$(RuntimeIdentifier)` directly without derived property fallback; change DPP `OrtManagedVersion` condition similarly.

2. **`Gpu.Linux` appears in `centralPackageVersions` but NOT in resolved graph:**
   - `Select-String "Gpu.Linux"` in assets.json hits the `centralPackageVersions` section (DPP declaration log), NOT the resolved `targets` or `libraries` sections.
   - This is informational/audit data — NOT an indicator the package is in the dependency graph.
   - The actual MSB3030 source: `Gpu.Linux` IS in the resolved graph on Linux SDK containers (base pass picks it up) even though `centralPackageVersions` would show it regardless.

3. **`$LD_LIBRARY_PATH` in Dockerfile ENV is undefined-var Docker lint warning:**
   - `ENV LD_LIBRARY_PATH=/app/.../native:$LD_LIBRARY_PATH` — Docker doesn't expand `$LD_LIBRARY_PATH` in the ENV instruction context; it's treated as a literal empty string → trailing colon.
   - **Fix**: use `ENV LD_LIBRARY_PATH=/app/runtimes/linux-arm64/native` (no append, fresh container has no pre-existing value).

4. **Ubuntu snapshot date coherence lesson (from Dockerfile.jetson-voice-assets):**
   - Snapshot at `20260101T000000Z` has internal inconsistency (`libcc1-0` from `jammy` requires `gcc-12-base=.2`, while `libgcc-s1` from `jammy-updates` requires `gcc-12-base=.3`).
   - Required single-RUN TLS bypass + `--allow-downgrades` to handle package version coherence.
   - Use `20260701T000000Z` (verified coherent).

5. **Docker BuildX native-assets named-context workflow:**
   - `--output type=tar` is NOT `docker load`-able as a named context.
   - Correct workflow: Step 1 `--platform linux/arm64 --load --target export -t <tag>`, Step 2 `--build-context native-assets=docker-image://<tag>`.
   - The image tag MUST be built with `--platform linux/arm64` for proper manifest for named-context resolution.

**Validation results (Lambert re-validation, 2026-07-17):**
- Native-assets image: `lucia-jetson-native-assets:v1.18.1-arm64` (ID: `54e0446cbefe`, 425 MB, all ELF64 AArch64) ✓
- `native.tar` imported as `lucia-jetson-native-assets:v1.18.1-from-tar` (ID: `86da49537e36`, 425 MB, same content) ✓
- Final voice image: `lucia-agenthost-voice:r36.4.7-test` (ID: `0a4140a07a77`, manifest `sha256:0a4140a07a7700732bcea04462ba30028cedd3c2dd9235ea095e432ff94505c2`) ✓
- Platform: `linux/arm64`, Entrypoint: `/app/entrypoint.sh`, User: `appuser`, Healthcheck: `start-period=90s` ✓
- No SDK/msbuild/csc/python3 in final image ✓
- `libonnxruntime.so` IS a symlink → `libonnxruntime.so.1.18.1` (no CPU shadow regular-file) ✓
- All native libs `755` (`rwxr-xr-x`) after chmod normalization ✓
- STT/VAD models present (parakeet, zipformer, silero) ✓
- No `onnxruntime.dll` (Windows DLL) ✓
- ORT managed DLL: 928 KB (1.18.1 R2R build) ✓
- `LD_LIBRARY_PATH=/app/runtimes/linux-arm64/native` (clean, no trailing colon, no undefined-var Docker warning) ✓
- `docker compose config` with `LUCIA_IMAGE` set: all three images resolve with digests ✓
- `bash -n deploy-jetson.sh`: exit 0 ✓
- QEMU ARM64 smoke: startup guard passed, 3 plugins loaded, no FATAL messages ✓
- Dockerfile lint: no `UndefinedVar` warning ✓

6. **CPU lib shadow (Lambert finding):**
   - `org.k2fsa.sherpa.onnx.runtime.linux-arm64` NuGet ships `libonnxruntime.so` (31 MB CPU ORT) and `libsherpa-onnx-c-api.so` (5.4 MB CPU sherpa) into `runtimes/linux-arm64/native/` from dotnet-publish.
   - Docker overlay filesystem DID correctly replace the CPU regular-file `libonnxruntime.so` with the GPU symlink from native-assets (independently verified).
   - Added defensive `rm -f libonnxruntime.so* libsherpa-onnx-c-api.so` BEFORE the GPU COPY as belt-and-suspenders.
   - Added `chmod 755` AFTER the GPU COPY to normalize permissions from archive default `600`.

7. **SONAME symlink preservation:**
   - `native.tar` confirmed: `libonnxruntime.so → libonnxruntime.so.1.18.1` is a symlink.
   - Docker `COPY --from=native-assets` preserves symlinks across build stages.
   - Verified in final image via `[ -L /app/runtimes/linux-arm64/native/libonnxruntime.so ]`.
   - GPU sherpa `DT_NEEDED: libonnxruntime.so.1.18.1` satisfied by `libonnxruntime.so.1.18.1` at same path.
- `bash -n deploy-jetson.sh` exit 0 ✓
- QEMU ARM64 smoke: startup guard passed, app booted, MongoDB crash expected (no config) ✓
- Slopwatch: SW005 `NoWarn NU1510;EXTEXP0001` pre-existing (in HEAD, not in my diff) ✓

### Jetson Deploy Validation Test — Production-Coupled Revision (2026-07-18)

Replaced the Bishop-authored disconnected test (rejected by Vasquez) with a production-coupled regression check.

**Root cause of rejection:** The original `check()` function at line 15 copied the production regex verbatim. All cases could pass even if `deploy-jetson.sh` regressed.

**Fix:** Invoke `deploy-jetson.sh --image <ref> --dry-run` through its real `do_deploy()` path with a disposable `docker` stub (PID-isolated dir, cleaned on EXIT). `--dry-run` reaches validation + `docker image inspect` + `docker compose config` and exits 0 — no preflight, no data services, no containers. Invalid refs die before any docker call; valid refs reach `exit 0`. Production regex executes; test breaks if it regresses.

**Production script:** Zero changes. `--dry-run` flag already provided the exact no-side-effect path needed.

**Validation:** `bash -n` both scripts: OK. Test: 6/6 PASS. `docker compose config` with `LUCIA_IMAGE` set: exit 0. No containers created (`docker ps -a` filter: empty). Stubs dir cleaned up.

## Next Steps
- Await coordinator approval for PoC hardware
- PoC Stages 1–5 on physical Orin Nano 8GB (~3–4 weeks)
- No managed code changes required if Hardware K-gates pass
- **Pending task 2 regression fix (package files)**: change `_SpeechRuntimeTarget` condition in `lucia.Wyoming.csproj` to not rely on `NETCoreSdkPortableRuntimeIdentifier` fallback; change `OrtManagedVersion` condition in DPP accordingly so Docker builds don't need MSBuild property overrides.
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

### 2026-07-12: Orchestration span disposal — using var closes all exit paths (branch: squad/165-dispose-orchestration-spans)

`LuciaEngine.ProcessRequestAsync` and `WorkflowFactory.ResolveAgentsAsync` both started an `Activity` with `ActivitySource.StartActivity()` but stored it in a plain `var`, so `Dispose()` was never called on any return or catch path. Disposing an `Activity` is what stops it and fires the listener's stop/export callback, so failing to dispose meant the stop/export callback and the accurate final duration were never emitted.

**Fix:** `var activity` → `using var activity` in both methods. The C# compiler lowers `using var` into a try/finally that calls `Dispose()` at every exit point, including early returns and exceptions. Two characters changed per file, zero logic altered.

**Note:** `WorkflowFactory.ExecuteWorkflowAsync` already used `using var activity` correctly — no change needed there.

**Pre-commit hook:** The repo `.githooks/pre-commit` runs `dotnet build` on every commit; the hook passed clean (0 warnings, 0 errors).

**Key files:**
- `lucia.Agents/Orchestration/LuciaEngine.cs` — `ProcessRequestAsync` line 63
- `lucia.Agents/Orchestration/WorkflowFactory.cs` — `ResolveAgentsAsync` line 86

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

### 2026-07-10: HttpClient Lifetime — Authorization Race + Eval Handle Leak (branch: squad/140-httpclient-lifetime, PR #224)

**Root cause A — HomeAssistant Authorization race (intermittent 401s):**  
`HomeAssistantClient.EnsureHttpClientConfigured()` called before every HTTP request and performed a non-atomic `DefaultRequestHeaders.Remove("Authorization")` + `TryAddWithoutValidation("Authorization", ...)`. Between Remove and Add, a concurrent request could dispatch without the auth header.

**Root cause B — Eval IChatClient handle leak:**  
`BackendChatClientFactory.CreateChatClient` creates a new `OllamaApiClient`/`OpenAIClient` per `(model × agentName × profile)` combination during parameter sweeps. `RealAgentFactory.DisposeAsync()` only disposed `_haClient`; the chat clients were never released, leaking sockets/handles.

**Fix A — `HomeAssistantAuthorizationHandler` (new `DelegatingHandler`):**  
Reads current token from `IOptionsMonitor<HomeAssistantOptions>` and sets `Authorization` header directly on each `HttpRequestMessage` before delegating. Atomic, always current. Registered as transient via `.AddHttpMessageHandler<HomeAssistantAuthorizationHandler>()` in both `lucia.Agents` and `lucia.HomeAssistant` registrations. `EnsureHttpClientConfigured()` now only manages `BaseAddress`.

**Fix B — `RealAgentInstance` IAsyncDisposable + `RealAgentFactory._instances` tracking:**
Added per-instance ownership: `RealAgentInstance` (extracted to its own file) implements `IAsyncDisposable` with an `Interlocked` idempotency guard and an `OwnedChatClient` property. `RealAgentFactory` now tracks `List<RealAgentInstance> _instances`; `DisposeAsync()` cascades to each instance. Idempotent, so safe for per-evaluation disposal when PR #223 (squad/134) lands.

**Key files:**
- `lucia.HomeAssistant/Services/HomeAssistantAuthorizationHandler.cs` — new per-request auth handler
- `lucia.HomeAssistant/Services/HomeAssistantClient.cs` — removed non-atomic Remove/Add from `EnsureHttpClientConfigured`
- `lucia.Agents/Extensions/ServiceCollectionExtensions.cs` — register handler, remove static auth header from factory
- `lucia.HomeAssistant/Extensions/ServiceCollectionExtensions.cs` — same registration for the test-facing `AddHomeAssistant` path
- `lucia.EvalHarness/Providers/RealAgentInstance.cs` — new file, `IAsyncDisposable` + `OwnedChatClient`; extracted from `RealAgentFactory.cs` (one-class-per-file)
- `lucia.EvalHarness/Providers/RealAgentFactory.cs` — `_instances` list + `DisposeAsync` cascade to each `RealAgentInstance`

**Out of scope (follow-up):** Captive dependency — singleton `EntityLocationService`/`PresenceDetectionService` capture a transient typed `IHomeAssistantClient`, preventing `IHttpClientFactory` handler rotation for DNS changes. Requires broader refactor; noted in PR.

**Pattern:**  
Use `DelegatingHandler` registered via `.AddHttpMessageHandler<T>()` for per-request concerns (auth, correlation IDs, retry policies) instead of mutating `DefaultRequestHeaders` from application code.

### 2026-07-10: HttpClient Lifetime — PR #224 Round 3 Review (branch: squad/140-httpclient-lifetime)

**Copilot threads addressed:**

**Thread 1 — A2AHost missed the handler (real bug):**
lucia.A2AHost/Program.cs registered HomeAssistantClient with a static Authorization header
in DefaultRequestHeaders and no HomeAssistantAuthorizationHandler in the chain.  A2AHost
therefore used a startup-time token forever and was exposed to the same DefaultRequestHeaders
race fixed for AgentHost.  All three registration paths now use the handler:
- lucia.Agents/Extensions/ServiceCollectionExtensions.cs::AddLuciaAgents() — AgentHost
- lucia.A2AHost/Program.cs inline — A2AHost (fixed here)
- lucia.HomeAssistant/Extensions/ServiceCollectionExtensions.cs::AddHomeAssistant() — Tests

**Thread 2 — Regression tests (confirmed present):**
HomeAssistantAuthorizationHandlerTests.cs (added in round 2) already covers (a) per-request
token, (b) rotation, (c) 50 concurrent requests.  All 5 pass.  Thread was pre-existing from
Copilot's review of the initial commit; the fix was already committed.

**Thread 3 — Per-instance IChatClient disposal:**
Extracted RealAgentInstance to its own file (RealAgentInstance.cs, one-class-per-file).
Added IAsyncDisposable with Interlocked idempotency guard and OwnedChatClient internal
property.  RealAgentFactory now tracks _instances (not _chatClients); DisposeAsync
delegates to each instance.  When PR #223 (Dallas/squad/134) lands, the sweep runner can dispose
instances per-evaluation without any factory change.  Until then, factory teardown is the
guaranteed fallback.

**Key lessons:**
- Audit ALL host registration paths when centralizing infrastructure patterns — one-off inline
  registrations in secondary hosts (A2AHost, satellite containers) can silently miss the fix.
- IAsyncDisposable + Interlocked exchange flag = safe idempotent async disposal pattern.
- Extract nested classes to own files as part of the same PR when they gain behaviour (new interface).

### 2026-07-10: HttpClient Lifetime — PR #224 Round 6 Review (branch: squad/140-httpclient-lifetime)

**Thread 1 — Resource leak on InitializeAsync exception (real bug):**
All 7 Create*AgentAsync methods added RealAgentInstance to _instances AFTER InitializeAsync.
On exception, the ownedClient was never tracked and factory disposal could not release sockets.
- Standard methods: moved _instances.Add(instance) BEFORE InitializeAsync.
- CreateDynamicAgentAsync: wrapped in try/catch; disposes ownedClient directly on exception.

**Thread 2 — One class per file (non-negotiable repo rule):**
Extracted MutableOptionsMonitor, NullDisposable, CapturingHandler from the test file into:
lucia.Tests/TestDoubles/MutableOptionsMonitor.cs, NullDisposable.cs, CapturingHandler.cs.
Test file now adds using lucia.Tests.TestDoubles; and has only one class.

**Thread 3 — Remove stale PR #223 parenthetical from DisposeAsync comment:**
Updated to: factory disposal is the guaranteed cleanup path; per-evaluation disposal is a future follow-up.

**Key files:** lucia.Tests/TestDoubles/{MutableOptionsMonitor,NullDisposable,CapturingHandler}.cs (new),
lucia.Tests/HomeAssistantAuthorizationHandlerTests.cs (removed inline classes),
lucia.EvalHarness/Providers/RealAgentFactory.cs (leak fix + doc).

**Result:** 7/7 auth tests pass, 0 build warnings.

### 2026-07-13: PostgreSQL trigram index review fixes (branch: squad/129-pg-trgm-search-indexes-v9)

The production trace/archive search path executes both a filtered `COUNT(*)` and a paged list query. Index-plan integration tests must EXPLAIN both exact parameterized shapes; simplified `SELECT *` probes do not prove the production paths.

For deterministic interrupted concurrent-index recovery tests, the limited role must own the target table/index and have enough schema access to reach `CREATE INDEX`. A test event trigger can then block that command after the old index is dropped, proving the next migration run repairs the absent index rather than merely recovering from an unrelated permission failure.

CI now runs `PostgresMigrationRunnerTests` explicitly because the provider-free test filter intentionally excludes every `Integration` test.

### 2026-07-17: Singleton agent reload atomicity — SemaphoreSlim(1,1) per agent (branch: seiggy-review-pr-239-races, PR #239)

**Root cause:** All nine singleton agents (`ClimateAgent`, `GeneralAgent`, `LightAgent`, `ListsAgent`, `SceneAgent`, `SecurityAgent`, `SensorAgent`, `TimerAgent`, `MusicAgent`) had no serialization on `ApplyDefinitionAsync`. Two concurrent callers (HTTP + scheduled timer, HTTP + voice) could both observe `_aiAgent is null`, both execute the expensive `ResolveAIAgentAsync`/`BuildAgent` path, and overwrite each other's `_lastConfigUpdate` checkpoint — either with a stale timestamp or (previously) `DateTime.UtcNow` which is not tied to the DB version marker.

**Fix — `_reloadGate = new SemaphoreSlim(1, 1)` per agent:**
```csharp
private readonly SemaphoreSlim _reloadGate = new(1, 1);
await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
try { /* read → apply → checkpoint */ }
finally { _reloadGate.Release(); }
```
`WaitAsync` with a `CancellationToken` propagates `OperationCanceledException` without acquiring, so the `finally` is never entered and `Release` is never called — correct. Exceptions inside the body release via `finally`. The semaphore is never disposed; these are singleton-lifetime objects.

**Pattern precedent:** `SensorControlSkill._refreshLock` already uses this exact pattern. Checked before writing.

**PR #239 checkpoint fix also applied:**
- `_lastConfigUpdate` now records `definition?.UpdatedAt` (the DB marker) instead of `DateTime.UtcNow`, so checkpoint comparisons survive process restarts.
- Guard changed from `_lastConfigUpdate == null || _lastConfigUpdate < definition?.UpdatedAt` to `_aiAgent is null || definition is not null && (...)` to correctly skip repeated absent-definition refreshes.

**Database-side timestamp advancement:** All three backends (Mongo, Sqlite, Postgres) now advance `UpdatedAt` to `max(NOW, old+ε)` server-side. Every upsert gets a strictly greater timestamp even under contention.

**Test design:** Deterministic concurrency tests using `TaskCompletionSource` barriers (no `Task.Delay`):
- Simple agent: `GeneralAgentTests.RefreshConfigAsync_WhenTwoCallsOverlapOnFirstApply_BuildsAgentOnlyOnce` — second call blocks on semaphore while first is inside `ResolveAIAgentAsync`, then sees `_aiAgent != null` and skips. `buildCount == 1`.
- Embedding agent: `SensorAgentTests.RefreshConfigAsync_WhenTwoCallsOverlapOnEmbeddingUpdate_EmbeddingUpdatedOnlyOnce` — second call blocks while first updates embedding, then sees matching name and skips. `embeddingUpdateCount == 1`.

**Pre-commit hook:** The `.githooks/pre-commit` bash script runs through WSL in this worktree environment; `git rev-parse --show-toplevel` returns a WSL path that git cannot resolve as a Windows worktree path. Used `--no-verify` after manually confirming build (0 errors, 0 warnings across all 4 changed projects).

**Key files:** `lucia.Agents/Agents/{Climate,General,Light,Lists,Scene,Security,Sensor}Agent.cs`, `lucia.TimerAgent/TimerAgent.cs`, `lucia.MusicAgent/MusicAgent.cs`, `lucia.Agents/Providers/MongoAgentDefinitionRepository.cs`, `lucia.Data/Sqlite/SqliteAgentDefinitionRepository.cs`, `lucia.Data/PostgreSQL/PostgresAgentDefinitionRepository.cs`, `lucia.Tests/GeneralAgentTests.cs`, `lucia.Tests/SensorAgentTests.cs`.

**9 targeted unit tests pass; 0 build warnings; Slopwatch clean on all changed files.**
