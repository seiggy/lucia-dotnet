# lucia-dotnet Comprehensive Code Review

**Date:** 2026-02-20  
**Reviewer:** Copilot (automated, with human review)  
**Scope:** All 246 C# files across 9 projects  
**Build Status:** ✅ Clean (0 warnings, 0 errors on `dotnet build lucia-dotnet.slnx`)

---

## Remediation Status (Updated 2026-02-20)

### Summary

- **Fixed:** 35 findings across 6 commits
- **Deferred:** 2 (planned for future stages)
- **Won't Fix:** 1 (intentional behavior)
- **Remaining:** ~60 low-severity items (naming, dead code, minor cleanup)

### Commits

| Commit | Round | Scope |
|--------|-------|-------|
| `8071cea` | 1 | Security, thread safety, error handling, code quality |
| `9c495da` | 2 | Correctness and resilience |
| `d5aa2f2` | 3a | LuciaEngine decomposition |
| `a45c578` | 3b | Options validation, MongoClient reuse, Redis MGET batching |
| `f18d492` | 4 | CPM versions, log levels, docker-compose, CORS, sealed classes, CancellationToken |
| `237c54b` | 5 | docker-compose.yml relocated to infra/docker/ |

### Critical Findings

| # | Finding | Status |
|---|---------|--------|
| C1 | No auth on any endpoint | 📋 Deferred (planned future stage) |
| C2 | OTel body logging sync-over-async deadlock | ✅ Fixed (round 1) — body logging kept intentionally |
| C3 | SSRF via unvalidated agentUrl | ✅ Fixed (round 1) — loopback allowlist |
| C4 | Hardcoded HA access tokens in tests | ✅ Fixed (round 1) — moved to UserSecrets/env vars |
| C5 | showSecrets=true exposes config values | 📋 Deferred (requires auth — see C1) |
| C6 | Connection string in exception message | ⚠️ Not yet fixed (low risk behind auth) |
| C7 | One-class-per-file violations | ✅ Fixed (round 1) — 3 files split into 8 |
| C8 | LightControlSkill unsynchronized state | ✅ Fixed (round 1) — volatile snapshot swap + SemaphoreSlim |
| C9 | MusicPlaybackSkill._cachedPlayers race | ✅ Fixed (round 1) — volatile snapshot swap |
| C10 | ContextExtractor check-then-act race | ✅ Fixed (round 1) — Interlocked.CompareExchange |
| C11 | HomeAssistantClient mutates shared HttpClient | ✅ Fixed (round 2) — config moved to IHttpClientFactory |
| C12 | ServiceCallRequest inherits Dictionary | ⚠️ Not yet fixed (design issue, needs API change) |
| C13 | `?? default!` null masking | ✅ Fixed (round 2) — replaced with `?? throw` |
| C14 | Sync-over-async GetAwaiter().GetResult() | ✅ Fixed (round 1) — same as C2 |

### High Findings

| # | Finding | Status |
|---|---------|--------|
| H1 | Empty catch in MongoConfigurationProvider | ✅ Fixed (round 1) — added logging |
| H2 | Empty catch in PluginLoader | ✅ Fixed (round 1) — added logging |
| H3 | Empty catch in FindMusicAssistantInstanceAsync | ✅ Fixed (round 1) — added logging |
| H4 | Empty catch in GetRandomTrackUrisAsync | ✅ Fixed (round 1) — added logging |
| H5 | Bare catch in ContextExtractor | ✅ Fixed (round 2) — narrowed to JsonException |
| H6 | Fire-and-forget PersistTraceAsync | ⚠️ Not yet fixed (low impact) |
| H7 | AgentRegistryClient no status checks, copy-paste log | ✅ Fixed (round 2) |
| H8 | IHomeAssistantClient only 6 of 18 methods | ✅ Fixed (round 1) — expanded to 24 methods |
| H9 | Zero logging in HomeAssistantClient | ✅ Fixed (round 1) — [LoggerMessage] structured logging |
| H10 | No IValidateOptions\<HomeAssistantOptions\> | ✅ Fixed (round 3) — startup validation |
| H11 | RedisTaskStore ignores CancellationToken | ✅ Fixed (round 4) — .WaitAsync(ct) wrapper |
| H12 | O(N) Redis scan + N+1 GETs in prompt cache | ✅ Fixed (round 3) — MGET batching |
| H13 | server.Keys full keyspace scan | ✅ Fixed (round 1) — lucia:task-ids SET index |
| H14 | New MongoClient on every config poll | ✅ Fixed (round 3) — Lazy\<MongoClient\> |
| H15 | NPE on response.Text substring | ✅ Fixed (round 2) — null guard |
| H16 | DiagnosticChatClientWrapper null substring | ✅ Fixed (round 2) — null guard |
| H17 | LuciaEngine 16 constructor params | ✅ Fixed (round 3) — decomposed to 6 (SessionManager + WorkflowFactory) |
| H18 | AgentDispatchExecutor.SetUserMessage coupling | ⚠️ Not yet fixed (architectural) |
| H19 | AgentHostService one failure aborts all | ✅ Fixed (round 2) — individual try/catch per agent |
| H20 | Aspire.Hosting in class library | ✅ Fixed (round 3) — removed |
| H21 | Redis docker volume uses tmpfs | ✅ Fixed (round 4) — disk-backed volume |
| H22 | curl health checks in .NET containers | ✅ Fixed (round 4) — switched to wget |

### Medium Findings

| Finding | Status |
|---------|--------|
| async methods with no await (GeneralAgent, ContextExtractor) | ✅ Fixed (round 4) |
| CancellationToken.None in MusicPlaybackSkill | ✅ Fixed (round 4) — 13 methods propagated |
| DiagnosticChatClientWrapper LogWarning for diagnostics | ✅ Fixed (round 4) — demoted to LogDebug |
| Aspire.Hosting.Testing version mismatch | ✅ Fixed (round 4) — $(AspireVersion) |
| TimeProvider.Testing wrong version variable | ✅ Fixed (round 4) |
| Http.Resilience version conflict | ✅ Fixed (round 4) — pinned to 10.1.0 |
| Non-sealed classes (7 classes) | ✅ Fixed (round 4) — sealed |
| CORS hardcoded to localhost:5173 | ✅ Fixed (round 4) — configuration-driven |
| UseHttpsRedirection only in Development | ✅ Fixed (round 4) — all environments |
| docker-compose.yml at repo root vs infra/docker/ | ✅ Fixed (round 5) — relocated |
| LuciaEngine.ClearHistory() throws NotImplementedException | ✅ Fixed (round 3) — removed |
| LuciaEngine duplicate _httpClientFactory assignment | ✅ Fixed (round 3) — removed |
| Remaining medium items (dead code, model inconsistencies, etc.) | ⚠️ Not yet fixed |

### Not Fixed (Low Priority / Deferred)

| Category | Items | Notes |
|----------|-------|-------|
| Auth/authz on endpoints | C1, C5 | Planned future stage |
| Low-severity cleanup | ~27 items | Naming, dead usings, magic strings |
| Test project issues | ~10 items | Metric test gaps, Task.Delay races, dead code |
| Model design issues | ~5 items | String dates, unused models, mutable vs record |
| Dead code removal | ~5 items | PromptCachingChatClient, ModelExtensions obsolete code |

---

## Executive Summary

| Severity | Count | 
|----------|-------|
| 🔴 Critical / Must Fix | 14 |
| 🟠 High / Should Fix | 22 |
| 🟡 Medium | 35 |
| 🟢 Low / Cleanup | 27 |
| **Total** | **98** |

**Top systemic issues:**
1. **No authentication/authorization on any HTTP endpoint** — the entire management API is open
2. **Swallowed exceptions** — 8+ empty `catch` blocks across the codebase
3. **Thread safety** — mutable singleton state without synchronization (LightControlSkill, MusicPlaybackSkill, ContextExtractor)
4. **Security** — SSRF risk, hardcoded secrets in tests, plaintext sensitive config
5. **One-class-per-file violations** — 3 files contain multiple classes

---

## 🔴 CRITICAL Findings (Must Fix)

### Security

| # | Finding | Location |
|---|---------|----------|
| C1 | **No auth on any endpoint** — config reset, task cancel, agent registration, trace deletion, dataset export all unauthenticated | All AgentHost API files |
| C2 | **HTTP body logged in OTel spans** — intentional for debugging platform behavior, but uses sync-over-async `GetAwaiter().GetResult()` pattern that causes deadlock risk | `ServiceDefaults\Extensions.cs:109,127,202` |
| C3 | **SSRF via unvalidated `agentUrl`** — user-supplied URL rewritten then used for outbound HTTP request from the server | `AgentHost\Extensions\AgentProxyApi.cs:26,47-52` |
| C4 | **Hardcoded HA access tokens in test source** — real JWT committed to repo (see Token Inventory below) | `Tests\HomeAssistantApiTests.cs:20`, `HomeAssistantErrorHandlingTests.cs:130,158,186` |
| C5 | **`showSecrets=true` querystring exposes all config values** — no auth required | `AgentHost\Extensions\ConfigurationApi.cs:79` |
| C6 | **Connection string in exception message** — ends up in logs | `Agents\Extensions\ServiceCollectionExtensions.cs:177-179` |

### Architecture

| # | Finding | Location |
|---|---------|----------|
| C7 | **One-class-per-file violations** | `Agents\Skills\Models\MusicLibraryResponse.cs` (3 classes), `AgentHost\Extensions\ConfigurationApi.cs` (5 classes), `AppHost\ModelExtensions.cs` (2 classes) |

### Thread Safety

| # | Finding | Location |
|---|---------|----------|
| C8 | **`LightControlSkill` unsynchronized mutable state** — `_cachedLights` (List) and `_areaEmbeddings` (Dictionary) mutated during `RefreshLightCacheAsync`, read during concurrent `FindLight*` calls. No locking, no `Interlocked` swap. Race conditions under concurrent requests. | `Agents\Skills\LightControlSkill.cs:26-29,618,678-679,735,748` |
| C9 | **`MusicPlaybackSkill._cachedPlayers`** — `List<T>` read via `ResolvePlayerAsync` (no lock) while `RefreshPlayerCacheAsync` calls `.Clear()` and `.Add()`. The `_cacheLock` semaphore only guards `EnsureCacheIsCurrentAsync`, but `RefreshPlayerCacheAsync` can also be called from `InitializeAsync` and `ResolvePlayerAsync:344` without the lock. | `MusicAgent\MusicPlaybackSkill.cs:37,342-347,461,487,493,510` |
| C10 | **`ContextExtractor._domainAgentMap` check-then-act race** — `GetDomainAgentMapAsync()` checks `Count > 0` then populates. Multiple concurrent callers issue redundant registry reads and mutate the dictionary concurrently. | `Agents\Services\ContextExtractor.cs:32-49` |

### Data Integrity

| # | Finding | Location |
|---|---------|----------|
| C11 | **`HomeAssistantClient` constructor mutates shared `HttpClient`** — sets `BaseAddress` (throws `InvalidOperationException` on double-set via factory), adds `Authorization` header as default (leaks bearer token on redirects to external hosts) | `HomeAssistant\Services\HomeAssistantClient.cs:18-27` |
| C12 | **`ServiceCallRequest` inherits `Dictionary<string,object>`** — `[JsonPropertyName]` attributes on `EntityId` are dead code since STJ serializes this type as a dictionary, not as an object. Misleading API. | `HomeAssistant\Models\ServiceCallRequest.cs:5-15` |
| C13 | **`?? default!` returns null for reference types** disguised as non-nullable — callers get `null` with no compiler warning, causing downstream NRE | `HomeAssistant\Services\HomeAssistantClient.cs:245` |
| C14 | **Sync-over-async `GetAwaiter().GetResult()`** in OTel enrichment callback — deadlock risk on some synchronization contexts | `ServiceDefaults\Extensions.cs:202` |

---

## 🟠 HIGH Findings (Should Fix)

### Error Handling (Swallowed Exceptions)

| # | Finding | Location |
|---|---------|----------|
| H1 | Empty `catch {}` in MongoConfigurationProvider polling — silently swallows all exceptions including auth failures | `Agents\Configuration\MongoConfigurationProvider.cs:72-75` |
| H2 | Empty `catch` in PluginLoader.LoadAssembly — DLL load failures (wrong arch, missing deps) are invisible | `A2AHost\Extensions\PluginLoader.cs:51-55` |
| H3 | Empty `catch(Exception) {}` in FindMusicAssistantInstanceAsync — dead method body with swallowed exception | `MusicAgent\MusicPlaybackSkill.cs:446-448` |
| H4 | `catch(Exception) { return []; }` in GetRandomTrackUrisAsync — user asks to shuffle and gets silence, no diagnostic trace | `MusicAgent\MusicPlaybackSkill.cs:558-560` |
| H5 | Bare `catch` in ContextExtractor.CreateJsonElementFromObject — catches `OutOfMemoryException`, `StackOverflowException` etc. Should narrow to `JsonException` | `Agents\Services\ContextExtractor.cs:348-353` |
| H6 | Fire-and-forget `_ = PersistTraceAsync(trace)` — exceptions in this unawaited task are silently lost | `Agents\Training\AgentTracingChatClient.cs:140` |
| H7 | AgentRegistryClient never checks HTTP status codes — `PostAsync`/`DeleteAsync` response is assigned but never checked, plus copy-paste log message says "register" in the unregister method | `A2AHost\AgentRegistry\AgentRegistryClient.cs:16,28,30` |

### Missing Capabilities

| # | Finding | Location |
|---|---------|----------|
| H8 | `IHomeAssistantClient` exposes only 6 of ~18 public methods — consumers needing `GetConfigAsync`, `GetEventsAsync`, `FireEventAsync`, `GetHistoryAsync`, `GetLogbookAsync`, `RenderTemplateAsync`, etc. must depend on the concrete type, defeating DI and mocking | `HomeAssistant\Services\IHomeAssistantClient.cs` |
| H9 | Zero logging in HomeAssistantClient — no `ILogger` injected, no diagnostics when HTTP calls fail. `EnsureSuccessStatusCode()` throws generic `HttpRequestException` with no operation context. | `HomeAssistant\Services\HomeAssistantClient.cs` (entire file) |
| H10 | No `IValidateOptions<HomeAssistantOptions>` — `BaseUrl` and `AccessToken` default to `string.Empty`, causing `UriFormatException` at runtime with no helpful message | `HomeAssistant\Configuration\HomeAssistantOptions.cs` |
| H11 | `RedisTaskStore` ignores `CancellationToken` in all 6 Redis operations — `StringGetAsync`, `StringSetAsync` etc. run without cancellation support | `Agents\Services\RedisTaskStore.cs:62,91,142,166,182,193` |

### Performance

| # | Finding | Location |
|---|---------|----------|
| H12 | O(N) Redis scan + N+1 individual GETs in prompt cache semantic search — `SetMembersAsync` + N individual `StringGetAsync` calls | `Agents\Services\RedisPromptCacheService.cs:101-137` |
| H13 | `server.Keys("lucia:task:*")` full keyspace scan — 4 production locations scan entire Redis keyspace, degrades as data grows | `Agents\Services\TaskArchivalService.cs:68-69`, `RedisTaskStore.cs:179-182`, `AgentHost\TaskManagementApi.cs:45,131`, `TimerAgent\TimerRecoveryService.cs:47` |
| H14 | New `MongoClient` created on every config poll — `MongoClient` should be created once and reused (manages its own connection pool) | `Agents\Configuration\MongoConfigurationProvider.cs:29,58` |

### Null Safety

| # | Finding | Location |
|---|---------|----------|
| H15 | `response.Text[..Math.Min(100, response.Text.Length)]` — NPE if `response.Text` is null | `Agents\Orchestration\LocalAgentInvoker.cs:72` |
| H16 | `response.Text?[..Math.Min(120, response.Text?.Length ?? 0)]` — outer `?` doesn't protect the substring if `Text` is null | `Agents\Agents\DiagnosticChatClientWrapper.cs:48` |

### Architecture

| # | Finding | Location |
|---|---------|----------|
| H17 | `LuciaEngine` — 16 constructor parameters (god class signal, SRP violation). Handles session loading, task persistence, agent resolution, workflow building, and history assembly. | `Agents\Orchestration\LuciaEngine.cs:43-78` |
| H18 | `AgentDispatchExecutor.SetUserMessage()` implicit coupling — mutable state on a workflow executor, caller must remember to call before execution or dispatch silently fails | `Agents\Orchestration\AgentDispatchExecutor.cs:24,50-53` |
| H19 | `AgentHostService.StartedAsync` — one agent `InitializeAsync()` or `RegisterAgentAsync()` failure aborts the entire `foreach`, leaving subsequent agents uninitialized | `A2AHost\Services\AgentHostService.cs:31-39` |

### Config / Build

| # | Finding | Location |
|---|---------|----------|
| H20 | `Aspire.Hosting.Azure.CognitiveServices` referenced in `lucia.Agents` — `Aspire.Hosting.*` packages are for the AppHost project only, not class libraries | `Agents\lucia.Agents.csproj:15` |
| H21 | Redis docker volume uses `tmpfs` — all data RAM-only, lost on restart, contradicts `--appendonly yes` persistence setting | `docker-compose.yml:469-473` |
| H22 | Health checks use `curl` in `read_only` .NET containers — standard .NET runtime images don't include `curl` | `docker-compose.yml:241,346,421` |

---

## 🟡 MEDIUM Findings (Recommended)

### Async Patterns
- `GeneralAgent.InitializeAsync` — `async` method with no `await`, generates unnecessary state machine (`Agents\Agents\GeneralAgent.cs:101`)
- `ContextExtractor.ExtractConversationTopicAsync` — `static async Task<string?>` with no `await`, should return `Task.FromResult` (`Agents\Services\ContextExtractor.cs:244`)
- `LocalAgentRegistry.GetEnumerableAgentsAsync` — `await Task.CompletedTask.ConfigureAwait(false)` is a no-op allocation (`Agents\Registry\LocalAgentRegistry.cs:53-54`)
- Missing `CancellationToken` on `ListSectionsAsync` and `ResetConfigAsync` (`AgentHost\Extensions\ConfigurationApi.cs:31,182`)
- `CancellationToken.None` passed instead of caller's token throughout MusicPlaybackSkill (`MusicAgent\MusicPlaybackSkill.cs:91,293,318,334-335,428,554`)

### Code Quality
- `PromptCachingChatClient` — pure pass-through wrapper, dead code per inline comment (`Agents\Services\PromptCachingChatClient.cs:33-34`)
- `LuciaEngine.ClearHistory()` throws `NotImplementedException` — dead code that crashes at runtime (`Agents\Orchestration\LuciaEngine.cs:424-427`)
- `_httpClientFactory` assigned twice in LuciaEngine constructor (`Agents\Orchestration\LuciaEngine.cs:29,78`)
- `DiagnosticChatClientWrapper` uses `LogWarning` for normal diagnostic output — pollutes warning-level monitoring (`Agents\Agents\DiagnosticChatClientWrapper.cs:31,38,46,59,62,65`)
- Duplicate Redis KEYS scanning code in TaskManagementApi (`AgentHost\TaskManagementApi.cs:44-48 vs 130-134`)
- Copy-paste log message "Error trying to register..." in unregister method (`A2AHost\AgentRegistryClient.cs:30`)
- `ModelExtensions.cs` — 268 lines of `[Obsolete]` dead code never called from `AppHost.cs` (`AppHost\ModelExtensions.cs`)
- Dead `_jsonlOptions` field declared but never referenced (`AgentHost\DatasetExportApi.cs:14`)
- Dead `JsonOptions` field declared but never referenced (`AgentHost\TaskManagementApi.cs:16-20`)
- `_searchTermEmbeddingCache` grows unbounded — `ConcurrentDictionary` never evicts entries (`MusicAgent\MusicPlaybackSkill.cs:38`)
- `AreaEntityMap` missing namespace declaration — class is in global namespace (`Agents\Skills\Models\AreaEntityMap.cs:1-10`)
- `DateTime.UtcNow` used instead of injectable `TimeProvider` in multiple files (`TraceManagementApi.cs:94`, `ConfigurationApi.cs:138,168`, `DatasetExportApi.cs:68`, `MusicPlaybackSkill.cs:42,415,483,518`)
- Many non-sealed classes that should be sealed per project convention (`LightAgent.cs:19`, `GeneralAgent.cs:12`, `AgentConfiguration.cs:7`, `OrchestratorStatus.cs:9`, `InMemorySessionFactory.cs:11`, `LightEntity.cs:9`, `MusicPlayerEntity.cs:8`)
- `AgentInitializationService` is in namespace `lucia.Agents.Extensions` but lives under `Services\` directory (`Agents\Services\AgentInitializationService.cs:7`)

### Configuration / Docker
- `Aspire.Hosting.Testing` hardcoded at `13.0.0` while `$(AspireVersion)` is `13.1.1` (`Directory.Packages.props:38`)
- `Microsoft.Agents.AI.Hosting.OpenAI` uses `alpha` tag vs `preview` for all other Agent packages — same build number `260212.1` (`Directory.Packages.props:57`)
- `Microsoft.Extensions.TimeProvider.Testing` uses `$(MicrosoftExtensionsAIVersion)` variable (semantically wrong) (`Directory.Packages.props:84`)
- MongoDB v2 vs v3 driver split — `A2AHost` uses `Aspire.MongoDB.Driver` (v2), `AgentHost` uses `Aspire.MongoDB.Driver.v3` (v3). Could cause serialization issues if accessing same database. (`A2AHost.csproj:7` vs `AgentHost.csproj:14`)
- OTEL endpoint `http://localhost:4317` inside container — refers to localhost inside the container, not the host or collector (`docker-compose.yml:232`)
- `UseHttpsRedirection` only in Development (backwards — more important in production) (`AgentHost\Program.cs:110`, `A2AHost\Program.cs:73`)
- CORS hardcoded to `localhost:5173` — should come from configuration (`AgentHost\Program.cs:80-88`)
- SSL bypass with no log warning — silently disabling validation is a security concern in production (`HomeAssistant\Extensions\ServiceCollectionExtensions.cs:26-33`)

### Models
- Inconsistent model conventions — mix of mutable `class`, `sealed class`, `sealed record` across HomeAssistant models (`HomeAssistant\Models\*`)
- `CalendarDateTime.Date`/`DateTime` are `string?` instead of `DateOnly?`/`DateTimeOffset?` (`HomeAssistant\Models\CalendarDateTime.cs:12-15`)
- `LogbookEntry.When` is untyped `string` instead of `DateTimeOffset` (`HomeAssistant\Models\LogbookEntry.cs:23`)
- `ServiceCallResponse` model appears unused — not referenced by any method in HomeAssistantClient (`HomeAssistant\Models\ServiceCallResponse.cs`)
- `LightEntity.NameEmbedding = null!` / `MusicPlayerEntity.NameEmbedding = null!` — suppresses nullable warnings, NPE at runtime during similarity comparisons if embedding generation fails (`Agents\Models\LightEntity.cs:13`, `MusicAgent` models)

### AgentHost API Quality
- `UpdateLabelAsync` and `DeleteTraceAsync` return `NoContent` (204) even when trace ID doesn't exist — misleading success response (`AgentHost\TraceManagementApi.cs:82-107`)
- `IsSensitiveKey` uses `ToLowerInvariant()` string allocation — should use `StringComparison.OrdinalIgnoreCase` (`AgentHost\ConfigurationApi.cs:311-316`)
- Inconsistent route grouping — `AgentRegistryApi` accepts `WebApplication` instead of `IEndpointRouteBuilder` and maps directly on `app` instead of using `MapGroup()` (`AgentHost\AgentRegistryApi.cs:10-19`)
- Unvalidated `agentId` passed to `new Uri(agentId)` — throws `UriFormatException` (500) on malformed input (`AgentHost\AgentRegistryApi.cs:45,82`)
- Export files written to `Path.GetTempPath()` with no cleanup or size limit — unbounded disk writes possible (`AgentHost\DatasetExportApi.cs:47-51`)
- `FileStream` opened without try/catch for TOCTOU race (`AgentHost\DatasetExportApi.cs:115`)
- Anonymous types used as API responses — fragile, undocumented by OpenAPI (`AgentHost\PromptCacheApi.cs:51,59`)

---

## 🟢 LOW Findings (Cleanup)

- Magic strings throughout (~30+ instances): `"orchestrator"`, `"unknown"`, `"lucia:task:"` duplicated, `"lucia.needsInput"`, `"admin-ui"`, `"live-config"`, `"Dashboard"`, `"AgentProxy"`, `"music_assistant"`, `"media_player"`, `"assist_satellite"` etc.
- Dead `using` statements in multiple files (`ILuciaAgent.cs:1-5`, `IAgentPlugin.cs:4-5`, `AgentConfiguration.cs:1-3`, `IAgentSkill.cs:1-3`)
- `const string?` — nullable const is meaningless since it has a value (`LightControlSkill.cs:21`, `MusicPlaybackSkill.cs:32`)
- Redundant `VersionOverride` values matching CPM exactly (`lucia.Agents.csproj:36-37`, `AppHost.csproj:16,19`, `Tests.csproj:36,39`)
- Dead methods: `AgentDiscoveryExtension` overloads (lines 52-84), `CreateLibraryResponseJson` in tests (`Tests\MusicPlaybackSkillTests.cs:121-133`)
- Missing `readonly` on fields assigned only in constructors (`MusicAgent\MusicAgent.cs:28-29`)
- `O(n²)` `HasCommonSubstring` algorithm with nested loops (`Agents\Configuration\DomainCapabilitiesExtractor.cs:162-175`)
- `Regex.Matches` without `RegexOptions.Compiled` or `[GeneratedRegex]` — recompiles on every call (`Agents\Configuration\DomainCapabilitiesExtractor.cs:63`)
- Export files written to temp with no cleanup (`AgentHost\DatasetExportApi.cs:47-51`)
- `Activator.CreateInstance(type)!` — null-forgiving with no helpful error context (`A2AHost\PluginLoader.cs:37`)
- `MusicPlaybackSkill.Meter` and `ActivitySource` static fields never disposed (`MusicAgent\MusicPlaybackSkill.cs:25-26`)
- Unused `TaskManager` local variable immediately shadowed (`MusicAgent\MusicAgentPlugin.cs:39`)
- `MusicAgentPlugin` uses `GetRequiredService<ILuciaAgent>()` — returns last registration, not necessarily MusicAgent (`MusicAgent\MusicAgentPlugin.cs:38`)
- `DateTimeOffset.Parse` without `CultureInfo` in `TimerRecoveryService` (`TimerAgent\TimerRecoveryService.cs:86`)
- `ActiveTimer.Cts` holds `CancellationTokenSource` as `required init` — lifecycle smell if GC'd without going through `RunTimerAsync` (`TimerAgent\ActiveTimer.cs:14`)
- `AppHost.cs` uses string interpolation for path construction instead of `Path.Combine()` (`AppHost\AppHost.cs:57-58`)
- `Azure.Identity` on beta version `1.18.0-beta.2` — track for GA (`Directory.Packages.props:90`)
- `System.Linq.Async` on `7.0.0-preview.9` — long-running preview (`Directory.Packages.props:107`)
- MongoDB runs without authentication in docker-compose — fine for local dev only (`docker-compose.yml:103-153`)

---

## Test Project Issues

| Severity | Finding | Location |
|----------|---------|----------|
| 🔴 | **Hardcoded HA JWT** (see C4) — same real token in 4 locations | `HomeAssistantApiTests.cs:20`, `HomeAssistantErrorHandlingTests.cs:130,158,186` |
| 🔴 | **Tests hit live HA with no skip guards** — all 13 `[Fact]` tests fail in CI. Compare with `DurableTaskPersistenceTests.cs:62` which correctly uses `[SkippableFact]` + `Skip.IfNot(...)` | `HomeAssistantApiTests.cs:37-236`, `HomeAssistantErrorHandlingTests.cs:50-209` |
| 🟠 | **Metric tests don't verify metrics** — test names promise metric tracking verification but only check data persistence | `TaskPersistenceMetricsTests.cs:54-73,76-106,109-116,118-155,157-199` |
| 🟠 | **No unit tests for orchestration pipeline** — only eval tests (requiring Azure OpenAI) and one aggregator test via `InputRequiredPipelineTests.cs`. No deterministic unit tests for routing, dispatch, error handling, timeout. | — |
| 🟠 | **`Task.Delay(200)` race conditions** — fragile on slow CI machines, should use `TaskCompletionSource` or polling | `TraceCaptureObserverTests.cs:64,101,136,161,186`, `TimerSkillTests.cs:92,145` |
| 🟡 | **Duplicated ServiceProvider setup** — 3+ tests each build identical DI container with ~20 lines of boilerplate | `HomeAssistantErrorHandlingTests.cs:123-209` |
| 🟡 | **`SessionData` test re-implements production trimming logic** — test asserts on its own reimplementation, not the actual service | `SessionCacheOptionsTests.cs:18-44` |
| 🟡 | **ServiceProvider/HttpClient handler leaks** — `BuildServiceProvider()` returns `IDisposable`, never disposed across test runs | `HomeAssistantApiTests.cs:32`, `HomeAssistantErrorHandlingTests.cs:28,47,140,168,197` |
| 🟡 | **`StubEmbeddingGenerator` returns identical vectors** — all cosine similarities are 1.0, gives false positives in any test relying on differentiation | `StubEmbeddingGenerator.cs:12-20` |
| 🟡 | **`AgentResponseBuilder` confusion** — `WithError(string)` and `WithErrorMessage(string)` do nearly the same thing, error-prone API | `AgentResponseBuilder.cs:35-46` |
| 🟡 | **Dead code** — `CreateLibraryResponseJson()` is never called | `MusicPlaybackSkillTests.cs:121-133` |
| 🟢 | Missing `CancellationToken` forwarding in `StubHttpMessageHandler` | `HomeAssistantTemplateClientTests.cs:104-107` |
| 🟢 | `TestBase` class rarely used (3 of 20+ test classes) — trivial helpers could be direct calls | `TestBase.cs` |

### Test Strengths ✅
- **Good test double design**: `DeterministicEmbeddingGenerator` (bag-of-words hashing), `FakeHomeAssistantClient` (snapshot-based), builder pattern doubles
- **Eval framework**: `AgentEvalTestBase` + `EvalTestFixture` + custom evaluators (`A2AToolCallEvaluator`, `LatencyEvaluator`)
- **Integration tests**: `DurableTaskPersistenceTests` properly uses Testcontainers with `IAsyncLifetime` and `SkippableFact`
- **Edge case coverage**: `ExtractDeviceIdTests` (null, empty, malformed JSON, missing properties)
- **Serialization tests**: `JsonlExportTests` and `ConversationTraceTests` with solid round-trip coverage
- **Naming convention**: Consistently follows `Method_Scenario_ExpectedBehavior`

---

## Token Inventory (for revocation)

### Real Token Found — **REVOKE THIS**

```
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiIyZmQyNzkyNTE4M2Y0ZjQyYjE5N2E2NTVjNzM0ZTkzOCIsImlhdCI6MTc1MjA5NTI5MywiZXhwIjoyMDY3NDU1MjkzfQ.A-ZsmZx0dZJosOno_C4ct3fdh0YYo9kou4H7pN9DIKc
```

| Field | Value |
|-------|-------|
| **Algorithm** | HS256 |
| **Issuer (HA User ID)** | `2fd27925183f4f42b197a655c734e938` |
| **Issued At** | 2025-07-09 (epoch 1752095293) |
| **Expires** | 2035-06-30 (epoch 2067455293) |
| **Found In** | `HomeAssistantApiTests.cs:20`, `HomeAssistantErrorHandlingTests.cs:130,158,186` |

### Not Real Tokens (documentation/examples)
- `.env.example:50` — placeholder with `PLACEHOLDER_REPLACE_WITH_ACTUAL_TOKEN`
- `infra/systemd/README.md:250` — truncated example `eyJ0eXAiOiJKV1Qi...`
- `TraceCaptureOptions.cs:20` — regex pattern for token redaction
- `infra/systemd/lucia.env.example:21` — comment referencing token format
- `specs/002-infrastructure-deployment/contracts/*.md` — documentation examples (truncated)
- `scripts/Export-HomeAssistantSnapshot.ps1:28` — help text example (truncated)
- `infra/docs/*.md` — configuration reference examples (truncated)

---

## Build & Configuration Issues

### Package Management
- CPM properly enabled with transitive pinning ✅
- Package source mapping in NuGet.config with `<clear />` ✅
- `TreatWarningsAsErrors` enabled globally ✅
- Version variables for package families ✅
- `Aspire.Hosting.Testing` should use `$(AspireVersion)` not hardcoded `13.0.0`
- Redundant `VersionOverride` values in 3 projects that match CPM exactly
- `Microsoft.Extensions.TimeProvider.Testing` uses wrong version variable (`$(MicrosoftExtensionsAIVersion)`)

### Docker
- Good security hardening: `read_only`, `no-new-privileges`, `cap_drop`, localhost-only ports, resource limits ✅
- Redis `tmpfs` volume contradicts `--appendonly yes`
- Health checks use `curl` not available in .NET images
- OTEL endpoint points to localhost inside container

---

## Remediation Plan

### Immediate (Security)
1. Revoke hardcoded HA token, move to UserSecrets
2. Fix sync-over-async deadlock in OTel body capture
3. Add SSRF validation to AgentProxyApi
4. Plan API key authentication (future staged work)

### Short-term (Reliability)
5. Fix thread safety with immutable snapshot swap pattern (3 classes)
6. Add logging to all empty catch blocks
7. Split multi-class files

### Medium-term (Quality)
8. Replace `server.Keys()` with Redis SET index
9. Expand `IHomeAssistantClient` interface (6 → 18 methods)
10. Add logging to HomeAssistantClient
11. Extract god-class `LuciaEngine` into smaller services

### Ongoing
12. Seal non-inherited classes
13. Replace magic strings with constants
14. Remove dead code
15. Standardize model types
16. Add unit tests for orchestration pipeline
