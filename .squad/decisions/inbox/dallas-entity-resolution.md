# Decision: Dynamic Entity Registration for Eval Scenarios

**Author:** Dallas (Eval Engineer)
**Date:** 2025-07-15
**Status:** Implemented

## Problem

Climate agent eval scenarios define entities in YAML `initial_state` (e.g., `climate.living_room_thermostat`), but these entities were only registered in the `FakeHomeAssistantClient`. The `SnapshotEntityLocationService` — which powers Find-style tools — was built exclusively from the static HA snapshot file, which contains zero climate entities. This caused all Find tool calls to return "no devices found", breaking the two-step find-then-act pattern.

Additionally, the `IEmbeddingProviderResolver` was faked to return null, preventing `ClimateControlSkill.RefreshCacheAsync` from ever populating its device cache.

## Decision

Three-part fix:

1. **Dynamic entity registration** — Added `RegisterEntity(entityId, friendlyName, areaId)` to `SnapshotEntityLocationService`. Called from `SetupInitialStateAsync` so scenario entities become discoverable by area/entity search.

2. **Fake embedding generator** — Created `FakeEmbeddingGenerator` that returns constant-value vectors. Wired into `RealAgentFactory.CreateClimateAgentAsync` so `ClimateControlSkill` can populate its device cache from `FakeHomeAssistantClient.GetAllEntityStatesAsync()`.

3. **Zero cache TTL for eval** — Set `CacheRefreshMinutes = 0` on climate/fan skill options in the eval factory so the device cache refreshes on every search, picking up entities injected after agent initialization.

## Why Not Alternatives

- **Merging into snapshot file** — Too invasive; would mix static snapshot data with per-scenario test entities.
- **Making FakeHomeAssistantClient implement IEntityLocationService** — Too much refactoring; the two services have very different interfaces.

## Files Changed

- `lucia.Tests/TestDoubles/SnapshotEntityLocationService.cs` — Added `RegisterEntity()`
- `lucia.Tests/TestDoubles/FakeEmbeddingGenerator.cs` — New file
- `lucia.Tests/TestDoubles/FakeHomeAssistantClient.cs` — Added climate service handlers
- `lucia.EvalHarness/Providers/RealAgentFactory.cs` — Exposed `EntityLocationService`, wired fake embeddings, set cache TTL
- `lucia.EvalHarness/Evaluation/ScenarioValidator.cs` — Extended `SetupInitialStateAsync` with optional location service
- `lucia.EvalHarness/Evaluation/EvalRunner.cs` — Pass location service through
- `lucia.EvalHarness/Evaluation/ParameterSweepRunner.cs` — Pass location service through
- `lucia.EvalHarness/Tui/EvalProgressDisplay.cs` — Pass location service through
