# Code health review and improvement plan

Scope: all .NET projects, the infrastructure layer (`lucia.Data`, `lucia.AppHost`, `lucia.ApplianceManager`, `lucia.InstallerHost`), and the dashboard. Excluded: `.worktrees`, `obj`, `bin`, migrations.

Method. This review is metrics-driven. I measured file size, class count per file, constructor size, dependency graph, exception handling and service-locator use across every project, then confirmed hotspots by reading. It is not a line-by-line audit. Treat each finding as "go look here first", and verify before refactoring.

## Summary

| Project | C# files | LOC | Health | Main issue |
| --- | --- | --- | --- | --- |
| lucia.Agents | 298 | 25.5k | Amber | Mongo-specific code lives in the domain project; god-classes (`EntityLocationService` 1.4k); 124 broad catches |
| lucia.AgentHost | 150 | 14.2k | Amber | 1.1k-line `ApplianceUpdateService`, 745-line `ApplianceApi`, 600-line `Program.cs`, endpoint files mix routing and logic |
| lucia.Wyoming | 181 | 14.8k | Red | `WyomingSession` 1.7k lines with ~44 fields; `AudioClipService` 942; `ModelManager` has 12 ctor params |
| lucia.Data | 58 | 9.8k | Amber | Three parallel implementations (InMemory, Postgres, Sqlite) of ~15 stores, each 450-1000 lines, with duplicated SQL and mapping |
| lucia.EvalHarness | 112 | 9.5k | Green/Amber | 8 multi-class files; large `Program.cs`, `EvalRunner`; depends on `lucia.Tests` |
| lucia.TimerAgent | 25 | 2.7k | Green | `AlarmSkill` 503 lines; 15 broad catches |
| lucia.ApplianceManager | 6 | 1.5k | Amber | `Program.cs` 872 lines, `ApplianceUpdateCoordinator` 740, no structure beyond that |
| lucia.HomeAssistant | 37 | 1.5k | Green | `HomeAssistantClient` 874 lines behind one interface (ISP) |
| lucia.MusicAgent | 5 | 0.8k | Green | `MusicPlaybackSkill` 557 lines |
| lucia.VoiceBenchmarks | 12 | 1.1k | Green | Small, one 436-line runner |
| lucia.InstallerHost | 7 | 0.4k | Green | Small. Leave alone (see constraints) |
| lucia.ServiceDefaults | 2 | 0.4k | Green | Fine |
| lucia.AppHost | 1 | 0.1k | Green | Fine |
| lucia.A2AHost | 5 | 0.5k | n/a | Deprecated. Delete, don't refactor |
| lucia-dashboard (TS) | n/a | n/a | Amber | `api.ts` 2.3k lines; five pages over 1k lines |

Repo-wide numbers (Agents, AgentHost, Wyoming, Data): 252 `catch (Exception)` blocks, 101 `GetRequiredService`/`GetService<>` calls, 37 `Task.Run`/`.Result`/`GetAwaiter().GetResult()` uses, 67 DI registrations in `AgentHost/Program.cs`.

## Constraints on this plan

- The `Lucia-Setup` captive Wi-Fi fallback, the installer's root/direct-control path, and root-owned privileged helpers are hardware-tested. Nothing below touches them. `InstallerHost` and the privileged parts of `ApplianceManager` are out of scope for structural refactors.
- One class per file stays the rule. Five files currently break it.
- TDD. Each refactor starts with a characterization test.

## Findings by principle

### Single responsibility (largest problem)

God-classes that mix several jobs. Splitting these gives the most return.

| Class | Lines | Likely responsibilities to separate |
| --- | --- | --- |
| `Wyoming/WyomingSession` | 1,679 | Protocol parsing, pipeline orchestration (ASR/intent/TTS), session state, speaker identification, audio bufferingtri |
| `Agents/Services/EntityLocationService` | 1,439 | Embedding/index build, search, cache persistence, location resolution |
| `AgentHost/Services/ApplianceUpdateService` | 1,145 | Version check, download/staging, verification, apply, status reporting |
| `Wyoming/AudioClipService` | 942 | Clip storage, playback, generation, cleanup |
| `HomeAssistant/HomeAssistantClient` | 874 | Entities, services, areas, templates, websocket, config |
| `ApplianceManager/Program.cs` | 872 | Endpoint mapping, auth, update coordination, config |
| `Agents/Skills/ClimateControlSkill` | 919 | Tool definitions, entity resolution, parameter parsing, response formatting |
| `Wyoming/CommandPatternMatcher` | 785 | Pattern compile, matching, slot extraction |
| `AgentHost/Apis/ApplianceApi` | 745 | Routing mixed with logic |
| `Agents/Auth/MongoApiKeyService` | 699 | Hashing, persistence, rotation, caching |

The skill files (`Climate`, `Fan`, `Security`, `Sensor`, `Light`) show the same shape four times. Extract the shared tool-parameter parsing and entity-resolution steps once.

### Open/closed

- `lucia.Data` has one class per (store, provider) pair. Adding a provider or store means editing `ServiceCollectionExtensions` and copying a file. A small shared SQL mapping layer (or Dapper-style helpers) would let providers differ only in dialect.
- Dashboard `api.ts` is one file that every new endpoint edits.

### Liskov and interface segregation

- `IHomeAssistantClient` is a single wide interface. Consumers such as skills only need entity state and service calls. Split into read, command, and registry interfaces so tests stop faking 40 members.
- `lucia.Agents` has 42 public interfaces, so the pattern is used. Audit the ones with a single implementation and one consumer; keep only where tests or providers vary.

### Dependency inversion and layering

1. `lucia.Data` references `Agents`, `TimerAgent` and `Wyoming`, while `AgentHost` references all of them. Persistence implements domain interfaces, which is the right direction, but the three feature projects each define their own store interfaces in themselves. A neutral `lucia.Abstractions` (or `lucia.Core`) project would let features avoid depending on `Agents` just for shared types.
2. `lucia.Agents` contains 152 MongoDB references and 5 Redis ones. The "domain" project owns a concrete database implementation next to the Postgres and Sqlite ones in `Data`. Move Mongo stores into `lucia.Data` (or `lucia.Data.Mongo`) so Agents holds no storage drivers.
3. `lucia.EvalHarness` references `lucia.Tests`. A production-tooling project depending on a test project is backwards. Extract shared fakes to a `lucia.Testing` library.
4. 101 service-locator calls. Some are legitimate (factories, scoped resolution from singletons). Audit each. Constructor injection or `IServiceScopeFactory` wrappers should replace the rest.

### Clean Code

- **Exception handling.** 252 `catch (Exception)` blocks. Many are likely log-and-continue. That hides faults in agent and voice paths. Catch specific exceptions, let cancellation through, and keep broad catches only at hosted-service and request boundaries.
- **Sync over async.** 37 blocking calls (`Task.Run`, `.Result`, `.GetAwaiter().GetResult()`). Review each. Blocking in a request path risks thread-pool starvation on a Jetson Orin Nano with 8 GB RAM.
- **Constructor bloat.** `ModelManager` takes 12 parameters. Over 5 usually signals two classes in one.
- **Large `Program.cs`.** `AgentHost` (608 lines, 67 registrations) and `ApplianceManager` (872). Group into `Add*` extension methods per feature, which the repo already does elsewhere.
- **Endpoint files.** `*Api.cs` in AgentHost run 430-745 lines and hold business logic. Handlers should be thin and delegate to services.
- **Multi-class files (rule breach).** `EntityVisibilityApi.cs`, `MongoPresenceSensorRepository.cs`, `OrchestrationTelemetry.cs`, `LiveEvent.cs` (3), `BackgroundTaskTracker.cs`, plus 8 in `EvalHarness`. Verify and split.
- **Naming and folders.** `Agents/Services`, `Agents/Models`, `Agents/Extensions` are catch-all folders. Prefer feature folders.
- **Deprecated code.** `lucia.A2AHost` is retired. Remove it and its references.

### Tests and infrastructure

- `lucia.Tests` is 40.9k lines against 25.5k in `Agents`. Good coverage signal, but size makes it slow to navigate. Check for per-class test files that mirror the god-classes.
- No per-project dependency rule is enforced. Add an architecture test (NetArchTest or similar) that fails on forbidden references, such as `Agents` referencing a storage driver.

## Improvement plan

Order is by risk reduction per effort. Each step ships as its own PR with tests green.

### Phase 0. Quick wins (days)

1. Delete `lucia.A2AHost` and its references.
2. Split the multi-class files (13 files). Mechanical.
3. Add an architecture test for project references and one-class-per-file.
4. Turn on analyzers for `CA1031` (general catch) and `CA2007`/`VSTHRD002` (blocking), as warnings first. Record the baseline so the count only goes down.

### Phase 1. Layering (1-2 weeks)

1. Create `lucia.Abstractions` with shared interfaces and DTOs. Point `Agents`, `Wyoming`, `TimerAgent`, `MusicAgent` and `Data` at it.
2. Move Mongo implementations out of `Agents`.
3. Extract shared test fakes so `EvalHarness` stops referencing `lucia.Tests`.
4. Replace service-locator calls, starting with the highest-count files.

### Phase 2. God-class decomposition (2-4 weeks, one class per PR)

Priority order, because each is both the largest and the most changed:

1. `WyomingSession`
2. `EntityLocationService` (and the duplicated `InMemoryEntityLocationService`, 1,022 lines)
3. `ApplianceUpdateService` and `ApplianceUpdateCoordinator` (preserve root-owned helper paths and offline setup)
4. `HomeAssistantClient` plus interface split
5. `AudioClipService`, `CommandPatternMatcher`, `ModelManager`
6. Skill base helpers shared by Climate/Fan/Security/Sensor/Light

For each, write characterization tests first, extract one collaborator at a time, and keep the public interface stable until the end.

### Phase 3. Data layer and API hygiene (2-3 weeks)

1. Introduce a shared mapping and SQL helper so Postgres and Sqlite stores share query text where dialects match. Target removal of 30 to 40 percent of duplicate code.
2. Thin the `*Api.cs` endpoints into handler plus service.
3. Break `Program.cs` files into feature `Add*` and `Map*` extensions.
4. Triage the 252 broad catches. Fix the ones in agent, voice and update paths first.

### Phase 4. Dashboard

1. Split `api.ts` by domain (agents, config, appliance, voice, installer).
2. Break the five pages over 1,000 lines into container plus presentational components and hooks.

## Health targets

| Metric | Now | Target |
| --- | --- | --- |
| Files over 600 lines (excluding generated) | ~25 | under 8 |
| Multi-class files | 18 | 0 |
| `catch (Exception)` outside boundaries | 252 total | under 60 |
| Blocking async calls | 37 | 0 in request paths |
| Constructors over 7 params | 2 | 0 |
| `Agents` storage-driver references | 157 | 0 |
| Forbidden project references | 1 known (`EvalHarness` to `Tests`) | 0, enforced by test |

## Open questions

- Is `lucia.Data` meant to stay one project for all providers, or split per provider to trim the Jetson image?
- Should Mongo remain a supported provider? If not, Phase 1 step 2 becomes deletion.
- Do you want an inventory of the individual 252 catch sites with a keep/fix verdict as a follow-up?
