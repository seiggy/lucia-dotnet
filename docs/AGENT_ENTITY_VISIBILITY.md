# Agent scoped entity discovery

Agent-aware searches apply entity visibility consistently. Timer and alarm tools
use those searches when resolving a location, including a presence-based alarm's
location lookup at fire time. Administrative inventory remains complete.

## Shared visibility rule

`LocationSearchService` owns one entity predicate used by scoped location search,
scoped entity search and hierarchy search:

| Entity allow-list | Agent-scoped result |
| --- | --- |
| `IncludeForAgent == null` | Visible to every agent |
| Empty collection | Hidden from every agent |
| Non-empty collection | Visible only to a listed caller, compared case-insensitively |

The production agent-facing methods reject null, empty or whitespace caller IDs.
Use a registered runtime identity, not a caller ID supplied by the model or user.
Both timer and alarm skills belong to `timer-agent`. There is no separate alarm
agent identity.

## Agent and admin APIs

`IAgentFilteredEntityLocationService` exposes:

- `FindEntitiesByLocationForAgentAsync` for location expansion, with optional domain and matching options.
- `SearchEntitiesForAgentAsync` for direct entity search, filtering candidates before embedding/fuzzy scoring or substring fallback.
- `SearchHierarchyForAgentAsync` for the existing hierarchy query.

For example:

```csharp
var visible = await filteredLocations.FindEntitiesByLocationForAgentAsync(
    "Kitchen",
    callerAgentId: registeredAgentId,
    domainFilter: ["media_player"],
    ct: cancellationToken);
```

The old `IEntityLocationService` signatures are unchanged. Its inventory,
location/entity searches and area/floor searches are unscoped, for administrative
use. Legacy `SearchHierarchyAsync` retains its existing behavior: it excludes
globally hidden entities but does not require any particular agent's allow-list.
Do not use the unscoped interface as a fallback for an agent search.

The two new filtered-interface methods have default implementations that throw
`NotSupportedException`. Older implementers can still load without implementing
the additions, but must implement them to support the new scoped queries. The
defaults never delegate to unfiltered inventory. The production service implements
both. Timer/alarm location resolution returns no target when the filtered
interface is absent. An implementation that advertises the interface but leaves
the new methods unsupported fails with `NotSupportedException`.

## Scope and cache behavior

The change covers all three legacy location-lookup configurations through one
new scoped method, plus direct entity search. The existing admin methods preserve
their output and matching behavior. No new facade, policy registry, configuration
switch, dependency or database migration is required.

Visibility updates use the existing cache freshness mechanism. Local changes
affect subsequent queries. Other Redis-backed instances check versions on the
existing 30-second interval during normal query traffic; this is not immediate
distributed authorization revocation.

Areas and floors remain unscoped location metadata. Entity visibility does not
invent an area/floor hiding policy or remove names of locations containing only
hidden entities. The agent-scoped result entities still follow the shared rule.

This is discovery filtering, not complete Home Assistant authorization. Existing
direct-ID paths still pass IDs through, and already resolved scheduled targets
are not revalidated by this change. A presence alarm resolves its location when
it fires, but playback does not recheck visibility on every iteration. Effect
authorization, exact-ID checks and revocation require a separate change with an
explicit execution contract.

## Tests

Offline regression checks:

```powershell
dotnet test lucia.Tests --filter "(FullyQualifiedName~EntityLocation|FullyQualifiedName~LocationSearchVisibilityTests|FullyQualifiedName~TimerSkillTests|FullyQualifiedName~AlarmSkillTests|FullyQualifiedName~AlarmScheduledTaskTests|FullyQualifiedName~DirectSkillExecutorTests|FullyQualifiedName~EntityResolverTests|FullyQualifiedName~EntitiesApiTests)&Category!=Integration&Category!=Eval"
```

The same service contract runs against a disposable Redis test container:

```powershell
dotnet test lucia.Tests --filter "FullyQualifiedName~RedisEntityLocationServiceTests"
```

Tests cover unrestricted, hidden, matching and other-agent allow-lists, case
handling, missing identities, domain filters, floor/area expansion, unchanged admin
inventory, local visibility changes, pre-scoring candidates and substring fallback.
Consumer checks use the production service with timer/alarm skills and verify that
hidden satellites are not queried for live state. Redis tests verify persisted
visibility on a second service instance. Caller tests check the fixed agent ID,
token forwarding and absence of an unscoped fallback.

No live model or Home Assistant device is used. Docker is needed only for Redis
integration tests.
