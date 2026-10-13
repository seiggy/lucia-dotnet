# lucia.Agents improvement plan

Companion to [code-health-review.md](code-health-review.md). This pass reads deeper into one project: 298 files, 25.5k lines.

## Corrections to the first review

The first review used regex counts across four projects. For `lucia.Agents` two of them were wrong.

| Metric in first review | Real value in lucia.Agents |
|---|---|
| 101 service-locator calls | About 8 outside DI registration (`WorkflowFactory`, `ModelProviderResolver`, `AIAgentExtensions` x2, `TraceRetentionService`, two plugin interfaces). The other 22 in `ServiceCollectionExtensions` are factory lambdas, which is normal. |
| 37 blocking calls | 1 `Task.Run` (`EntityLocationService`, a background job, fine). The rest were `.Result` on tool-result properties. **No sync-over-async problem here.** |

Drop both from the target table. The 124 `catch (Exception)` figure holds and is analysed below.

## Status

Merged in #288 (2026-10-13):
- Phase 0: multi-class splits, cancellation filters on the broad catches, catch triage sheet, CA1031 as a suggestion.
- Mongo removal across code, packages, AppHost, Compose, Helm, raw manifests, CI and docs. `lucia.A2AHost` is deleted. `DataProvider:Store=MongoDB` and any `mongodb://` connection string fail at startup.
- Redis stores and caches moved to `lucia.Data/Redis`. `lucia.Agents` references no storage driver, and `AgentsDependencyTests` enforces it.
- `EntityLocationService` split (see the design section below). 1,438 lines became about 650.
- Provider contract tests on SQLite and PostgreSQL, and the bugs they found (SQLite task stats, revoked API key regeneration, duplicate response template exception).
- Two code reviews and the PR review findings, all fixed.

Open issues from that work:
- #289: entity searches other than the hierarchy search ignore per-agent visibility.
- #290: `RedisLocationSnapshotStore` writes with `FireAndForget`, so the shared version can be bumped before the snapshot lands.

Done on branch `refactor/agents-skills-base`: the skills base. All seven skills in `lucia.Agents` and `MusicPlaybackSkill` use it.
- Characterization tests pin each skill's tool output (30 Climate, 32 Fan, 29 Sensor, 14 Scene, 17 List, 15 Light, plus failure and cancellation cases for Security and Music).
- `ToolRunner` replaces the per-tool try/catch. `AttributeParser` replaces the two copies of the string-list parser. `SkillEntityIndex<T>` owns the embedding generator, cached items, refresh lock and staleness check.
- Line counts: Climate 918 to 651, Fan 797 to 607, Sensor 652 to 567, Scene 226 to 207, List 155 to 130, Light 383 to 361, Security 640 to 590. Constructors are unchanged (7 to 8 parameters).
- The per-skill loader keeps the domain part: read the shared device cache, build one device from a Home Assistant state, write the cache back.
- Telemetry: `ToolRunner` sets the span status and `elapsed_ms`, and `ToolMetrics` gives each tool request, failure and duration metrics with a `tool` tag. Tools that had none (Climate lookups, Sensor lookups, Scene, List, Music control) now report through it. `ServiceDefaults` exports `Lucia.Skills.*`.
- Decision: a failed tool returns a fixed message and logs the exception. `ex.Message` can hold connection strings and addresses, so it never reaches the model. `ToolRunner` has no flag for it.
- Not moved: the Timer, Alarm and Scheduler skills (their catch blocks are best-effort persistence warnings, not tool failures) and the `searxng` and `brave-search` plugins (out of scope for now, they still return `ex.Message`).
- The 500-line target is out of reach with these helpers. The state formatters, device builders and command patterns stay in each skill.
## EntityLocationService split design

Status: done. The facade went from 1,438 to about 600 lines, backed by `LocationSearchService`, `LocationSnapshotLoader`, `EntityEmbeddingJob` and the two store interfaces. `InMemoryEntityLocationService` is gone. In-memory mode registers `InProcessLocationSnapshotStore` and persists visibility through `ConfigStoreVisibilityConfigStore`, so visibility now survives restarts without Redis.

Agreed approach. Facade first: `IEntityLocationService` stays, and the 34 production and 23 test consumers keep compiling. Narrow interfaces come later.

Facts behind it:
- `EntityLocationService` (Redis, 1,438 lines) and `InMemoryEntityLocationService` (1,021 lines) share 39 of about 47 method names. They differ in where the snapshot is persisted, how staleness is detected (Redis version counter) and where visibility config lives.
- In-memory mode loses per-agent visibility settings on every restart and regenerates all embeddings on each boot.

Target classes, one per file:

| Class | Job |
|---|---|
| `LocationSnapshotLoader` | Load and build floors, areas and entities from Home Assistant |
| `LocationSearchService` | Hybrid and hierarchical search, exact match, find by location. Pure logic over a snapshot |
| `EntityEmbeddingJob` | Batching, retries, regenerate and evict |
| `EntityVisibilityService` | Exposed-only flag and per-agent filters |
| `ILocationSnapshotStore` | Persist and reload the snapshot, plus the version counter. Redis and in-memory implementations |
| `IVisibilityConfigStore` | Persist the visibility config. Redis implementation (same key as today) when Redis is the cache provider, config-store implementation (`IConfigStoreWriter`) otherwise |
| `EntityLocationService` | Thin facade implementing today's interface. One implementation serves both cache providers. `InMemoryEntityLocationService` is retired as a class; in-memory mode stays and is just the in-process `ILocationSnapshotStore` registration |

Order of work, each step its own commit with characterization tests first:
1. Characterization tests for search and visibility behaviour through the interface, run against both current implementations.
2. Extract `LocationSearchService` (pure, lowest risk).
3. Extract `LocationSnapshotLoader` and `EntityEmbeddingJob`.
4. Introduce the two store interfaces with the Redis and in-memory implementations; route the facade through them.
5. Add the config-store visibility implementation, so visibility persists in SQLite and PostgreSQL deployments. Behaviour change: visibility now survives restarts in non-Redis mode.
6. Retire the `InMemoryEntityLocationService` class (the in-memory mode itself stays); `lucia.Agents` drops its Redis package; add the architecture test.

## Health by namespace

| Namespace | Files | LOC | Health | Notes |
|---|---|---|---|---|
| `Services` | 31 | 6,019 | Red | `EntityLocationService` 1,439; `RedisPromptCacheService` 757; `RedisDeviceCacheService` 515; per-domain cache DTOs sit beside services |
| `Skills` | 13 | 3,853 | Red | Five skills of 640-920 lines repeating the same cache, resolve and format code |
| `Orchestration` | 30 | 3,326 | Amber | `WorkflowFactory` takes 16 constructor params; `LuciaEngine` and `LocalAgentInvoker` take 7 |
| `Integration` | 13 | 2,273 | Amber | Grab bag. Holds Mongo and Redis stores, string utilities and chat-client decorators together |
| `Agents` | 9 | 2,039 | Amber | `DynamicAgent` takes 10 params. The seven static agents are 170-300 lines each and look alike |
| `Auth` | 8 | 1,227 | Amber | `MongoApiKeyService` 699 lines mixes hashing, rotation and persistence |
| `PluginFramework` | 16 | 1,477 | Green/Amber | `PluginManagementService` 455 lines, Mongo types leak in |
| `Training` | 23 | 1,453 | Green | `MongoTraceRepository` should live in a store project |
| `Models` | 50 | 1,491 | Green | Small DTOs. Check for BSON attributes leaking in |
| `Configuration` | 22 | 1,267 | Amber | Mongo config provider and options with `[Bson*]` attributes mixed together |
| `Abstractions` | 38 | 1,481 | Green | Good seam. Some interfaces carry Mongo types |
| `Extensions` | 7 | 1,104 | Amber | `ServiceCollectionExtensions` is one 250-line method (`AddLuciaAgents`) that registers 60+ services |
| `Providers` | 7 | 909 | Green | `ModelProviderResolver` 465 lines, uses `IServiceProvider` |
| `CommandTracing`, `Mcp`, `GitHubCopilot`, `Registry`, `Hosting`, `DataStores` | 31 | ~1,700 | Green | Small. `Mcp/McpToolRegistry` has 5 broad catches |

## Triage

Priority = harm if left alone x how often the code changes. P0 blocks other work, P1 is the main refactor, P2 is cleanup.

### P0. Storage driver leaks into the domain project

152 Mongo references and 5 Redis references live in `lucia.Agents`. Concrete stores:

- Mongo: `MongoApiKeyService`, `MongoTraceRepository`, `MongoCommandTraceRepository`, `MongoTaskArchiveStore`, `MongoMemoryStore`, `MongoPresenceSensorRepository`, `MongoAgentDefinitionRepository`, `MongoModelProviderRepository`, `MongoPluginManagementRepository`, `MongoConfigurationProvider`, `ConfigStoreWriter`.
- Redis: `RedisTaskStore`, `RedisDeviceCacheService`, `RedisPromptCacheService`, `RedisSessionCacheService`.
- Model and config types carry `[BsonId]`/`[BsonElement]` (12 hits in `Models`, `Abstractions`, `Configuration`), so even the DTOs are coupled.

`lucia.Data` already holds the Postgres, Sqlite and in-memory versions of these same interfaces. The Mongo versions are the odd ones out.

Decision (owner): drop Mongo support. The Mongo stores are deleted, not moved.

Mongo is wider than `lucia.Agents`. Full removal touches:

| Location | Work |
|---|---|
| `lucia.Agents` | Delete the 11 Mongo files, strip `[Bson*]` attributes and `using MongoDB` from DTOs, drop `MongoDB.Driver` |
| `lucia.AgentHost` | `Program.cs` (33 refs), `AgentDefinitionApi`, `ConfigurationApi`, `OnboardingMiddleware`, `TaskManagementApi`, `ResponseTemplateApi`, `SetupApi`; delete `MongoResponseTemplateRepository`, drop the `[Bson*]` attributes and `MongoDB.*` usings from `ResponseTemplate`, remove the `useMongo` branch in `Program.cs`; drop `Aspire.MongoDB.Driver.v3` |
| `lucia.Wyoming` | Drop `MongoDB.Driver` package reference (no `.cs` use found; verify) |
| `lucia.Data` | Remove `Mongo` from `StoreProviderType`/`DataProviderOptions`; clean the comments in `Sqlite*` and `PostgresDbNames` |
| `lucia.AppHost` | Remove the Mongo resource and `Aspire.Hosting.MongoDB` |
| `lucia.A2AHost` | Deleted anyway |
| `lucia.Tests` | Delete `MongoApiKeyServiceTests`, `MongoMemoryStoreTests`; drop `Testcontainers.MongoDb` |
| Docs and compose | README (compose service, `luciatraces` connection string, feature blurb), `Directory.Packages.props` entries |

Risk: none to carry. No known installs used the legacy Mongo provider, and the defaults, templates and install scripts already target PostgreSQL. No export path. Release notes should tell anyone still on Mongo to re-run setup. Dropping the Mongo container also saves memory on the Jetson.

Redis stays (task store, caches), but the four Redis services move out of `lucia.Agents` into `lucia.Data` behind the existing interfaces. `EntityLocationService` calls Redis directly (1 reference); route it through a new `ILocationSnapshotCache`.

Postgres and Sqlite response-template stores already exist (`PostgresResponseTemplateRepository`, `SqliteResponseTemplateRepository`, both wired in `AgentHost/Program.cs`), so nothing blocks the removal. They have no direct tests yet. Add a Sqlite round-trip test (create, get by id, get by skill and action, update, delete, reset to defaults) before deleting the Mongo version.

Action order:
1. Delete Mongo code in one PR per project, bottom-up (Agents, Data, AgentHost, AppHost, tests, docs).
2. Move the Redis services.
3. Add an architecture test: `lucia.Agents` may not reference `MongoDB.*` or `StackExchange.Redis`.

Done when no `csproj` or `Directory.Packages.props` mentions MongoDB and `lucia.Agents.csproj` has no driver packages.

### P1. `EntityLocationService` (1,439 lines, 40+ members)

One class does six jobs, readable from its member list:

| Job | Members | Candidate class |
|---|---|---|
| Load from Home Assistant | `LoadFromHomeAssistantAsync`, `LoadAreaEntityMapAsync`, `BuildFloors/Areas/Entities` | `LocationSnapshotLoader` |
| Hold and swap the snapshot | `SwapData`, `GetSnapshot`, `GetAreaForEntity`, `GetFloorForArea` | stays as the core |
| Search | `SearchEntities/Areas/Floors`, `SearchHierarchy*`, `ExactMatch*`, `FindEntitiesByLocation` (3 overloads) | `LocationSearchService` |
| Embedding generation | `StartEmbeddingGenerationJob`, `RunEmbeddingGenerationJobAsync`, `TryGenerateBatchWithRetryAsync`, `CloneEmbedding`, regenerate/evict | `EntityEmbeddingJob` |
| Redis persistence | `TryLoadFromRedisAsync`, `SaveToRedisAsync`, `BumpRedisVersionAsync`, `EnsureFreshAsync` | `LocationSnapshotCache` (see P0) |
| Visibility config | `GetVisibilityConfigAsync`, `SetUseExposedOnly*`, `SetEntityAgents*`, `ClearAllAgentFilters*`, `ApplyVisibilityToEntities` | `EntityVisibilityService` |

Fix the three `FindEntitiesByLocationAsync` overloads and the two `SearchHierarchy*` variants at the same time. Optional parameters would remove at least two.

`lucia.Data/InMemory/InMemoryEntityLocationService.cs` (1,022 lines) is a second copy of much of this. Decision (owner): it stays. It serves mono-container deployments where cross-process caching is not needed, so it is a supported production mode, not a test double. The goal changes from deleting it to removing the duplication. After the split, the search, embedding and visibility classes are shared. The Redis and in-memory variants differ only in `ILocationSnapshotCache` (Redis-backed vs in-process), and `InMemoryEntityLocationService` shrinks to wiring plus the in-memory cache, or disappears into a registration if nothing else is left. Keep both registrations selectable in `lucia.Data` `ServiceCollectionExtensions`, and run the shared test suite against both cache implementations.

### P1. Skills: five copies of one pattern

`ClimateControlSkill` (919), `FanControlSkill` (798), `SensorControlSkill` (652), `SecurityControlSkill` (641), `LightControlSkill`, `SceneControlSkill` each carry:

- A constructor with 7-8 dependencies (HA client, embedding resolver, device cache, location service, entity matcher, options, logger, configuration).
- `RefreshCacheAsync` / `EnsureCacheIsCurrentAsync`, 4-8 references per file, differing only by DTO type. This is why there are `ClimateCacheDto`, `FanCacheDto`, `LightCacheDto`, `PlayerCacheDto`, `SensorCacheDto` in `Services`.
- `InitializeAsync`, `UpdateEmbeddingProviderAsync`, `GetCurrentMatchOptions`.
- A `FindXDeviceAsync` that resolves a search term to an entity.
- A `FormatXState` method and string-list attribute parser (`ParseStringListAttribute`).
- A try/catch per tool that logs, bumps a counter and returns `$"Error ...: {ex.Message}"` (11 such returns).

Action:
1. Add an abstract `EntitySkillBase<TCacheEntry>` or, better, a composed `SkillEntityIndex<T>` that owns cache refresh, embedding provider swap and match options. Skills take it as one dependency, cutting constructors to 4-5.
2. Move the 7-8 line try/catch into one helper (`ToolRunner.RunAsync(name, counter, func)`) so tool methods hold only their domain logic.
3. Move `ParseStringListAttribute` and shared formatting to one static helper.
4. Move the tool methods (`GetTools`, command patterns) from the skill into a thin class if the skill still exceeds 400 lines.

Characterization tests first: `lucia.Tests` already covers the skills; add a snapshot of each tool's output before extracting.

Concern about leaking internals: `ex.Message` is returned to the LLM and possibly to users. Return a generic message and log the detail.

### P1. Orchestration construction

- `WorkflowFactory`: 16 constructor params, takes `IServiceProvider` and calls `GetService<AgentSessionStore>()` at line 169. Both are signs that it builds too much. Split into `WorkflowBuilder` (graph wiring) and `AgentExecutorFactory`, and inject `AgentSessionStore` as optional through a normal constructor argument.
- `AgentInitializationService`: 14 params, holds `IServiceProvider`. Looks like a coordinator that starts every agent. Replace with `IEnumerable<ILuciaAgent>` initialisation through an `IAgentInitializer` per agent, or fold into agents' own `InitializeAsync`.
- `DynamicAgent` (10) and `DynamicAgentLoader` (10): group related dependencies into small option or context objects (`DynamicAgentContext`).
- `LuciaEngine`, `LocalAgentInvoker` at 7: borderline. Revisit after the above.

### P1. Broad exception handling (124 catches)

Samples show two patterns:
- Skills: log, count, return error text. Acceptable at a tool boundary once centralised (see Skills).
- Services and caches (`RedisPromptCacheService` 13, `AgentInitializationService` 4, `DynamicAgentLoader` 3, `McpToolRegistry` 5, `WorkflowFactory` 4): log and continue. These hide partial failure and can mask cancellation.

Only 13 files in the project mention `OperationCanceledException`, against 124 broad catches.

Action:
1. Add `catch (OperationCanceledException) { throw; }` or an `when (ex is not OperationCanceledException)` filter wherever a broad catch wraps awaited work.
2. For each remaining catch, classify as boundary (keep), recoverable (narrow the type) or swallowed (remove or rethrow). A triage sheet of the 124 is the first deliverable.
3. Enable `CA1031` as a warning for this project and baseline the count.

### P2. Interface hygiene

Within the project, 30 interfaces have exactly one implementation. That count is misleading, since `lucia.Data` provides alternate implementations of most stores. Genuine candidates to inline are those with one consumer and no alternate provider:

- `IAutoAssignEntityService`, `IAgentFilteredEntityLocationService`, `IEmbeddingSimilarityService`, `IPresenceDetectionService`, `ICascadingEntityResolver`, `IDynamicAgentProvider`.

Keep an interface only if a test fake or second provider uses it. Five interfaces have no implementation in the project (`IAgentPlugin`, `IAgentProvider`, `ILuciaPlugin`, `IWebSearchSkill`, `ISpanCollector`). Decision (owner): they are public extension points for third-party plugins and drop-in updates. Nothing in-repo implements them. Treat them as a public API: do not rename, remove or change signatures without a version bump. Mark them in docs as the supported extension surface, and add one sample plugin or test implementer so a breaking change fails the build. `ILuciaPlugin` takes `IServiceProvider` by design, a deliberate service-locator seam. Leave it.

### P2. Folder structure and file hygiene

- `Services` mixes caching, entity resolution, hosted services and DTOs. Group into `Entities/`, `Caching/`, `HostedServices/`.
- `Integration` mixes stores, string utils (`StringSimilarity` 505 lines, `SearchTermNormalizer`, `SearchTermCache`) and chat-client decorators. Move to `Text/`, `ChatClients/`, and stores to the data project.
- Per-domain cache DTOs move next to their skill.
- Multi-class files: `MongoPresenceSensorRepository.cs` (2), `OrchestrationTelemetry.cs` (2), `LiveEvent.cs` (3). Split.
- 4 `#pragma warning disable` / `[Obsolete]` markers to review.
- 11 static mutable collections. Confirm each is initialised once or thread-safe.

### P2. `ServiceCollectionExtensions.AddLuciaAgents`

One method, 22 inline `GetRequiredService` factory lambdas and 60+ registrations. Break into `AddLuciaCaching`, `AddLuciaOrchestration`, `AddLuciaSkills`, `AddLuciaPlugins`, `AddLuciaProviders`. Each skill registration is three lines (skill, `IOptimizableSkill` alias, agent plus `ILuciaAgent` alias). A small `AddSkillAgent<TSkill,TAgent>()` helper removes about 30 lines of repetition.

## Phased plan

| Phase | Work | Size | Gate |
|---|---|---|---|
| 0 | Split 3 multi-class files. Fix catch/cancellation gaps. Add `CA1031`. Produce catch triage sheet. | 2-3 days | Build green, baseline recorded |
| 1 | P0: delete Mongo across the repo; move Redis stores to `lucia.Data`; architecture test for drivers | 1 week | No MongoDB mention in any csproj; `lucia.Agents.csproj` has no driver packages |
| 2 | P1: split `EntityLocationService` into 5 classes; dedupe `InMemoryEntityLocationService` (kept for mono-container mode) | 1-2 weeks | Public interface unchanged, tests green |
| 3 | P1: skill base (`SkillEntityIndex<T>`, `ToolRunner`); migrate Climate, Fan, Sensor, Security, Light, Scene one per PR | 2 weeks | Each skill under 500 lines, ctor under 6 params |
| 4 | P1: `WorkflowFactory`, `AgentInitializationService`, `DynamicAgent` constructor reductions | 1 week | No ctor over 7 params |
| 5 | P2: interface inlining, folder moves, DI extension split | 1 week | |

Each step is its own PR, TDD-first, with characterization tests before extraction.

## Targets

| Metric | Now | Target |
|---|---|---|
| Storage-driver references | 157 | 0 |
| Files over 600 lines | 8 | 0 |
| Constructors over 7 params | 4 (`WorkflowFactory`, `AgentInitializationService`, `DynamicAgent`, `DynamicAgentLoader`) | 0 |
| `catch (Exception)` | 124 | under 40, none around cancellable awaits without a filter |
| Multi-class files | 3 | 0 |
| Service-locator calls (non-DI) | ~8 | 2 (plugin seam, one hosted-service scope) |
| Largest class | 1,439 lines | under 500 |

## Out of scope

Installer, captive Wi-Fi fallback, and privileged helper paths. None live in `lucia.Agents`.

## Open questions

Resolved: Mongo is dropped with no export path, `InMemoryEntityLocationService` stays for mono-container mode, and the plugin interfaces stay as a public extension API.

No open questions remain.
