# Agent scoped entity discovery

Timer and alarm tools resolve a spoken location such as "office" through the
same hierarchy search the other agents use, scoped to `timer-agent`. Before
this change they called `FindEntitiesByLocationAsync`, which ignored the
per-agent entity allow-list, so a satellite or speaker hidden from the timer
agent could still receive its announcements.

## How the timer agent resolves a location

`TimerSkill`, `AlarmSkill` and the presence branch of `AlarmScheduledTask` call
`IAgentFilteredEntityLocationService.SearchHierarchyForAgentAsync` with the
`timer-agent` ID and a domain filter (`assist_satellite` for timers,
`media_player` for alarms). They read `ResolvedEntities` from the result.

Hierarchy search scores floors, areas and entities together. "Office",
"upstairs" and "office speaker" all resolve, where the old location lookup only
expanded floors and areas. The search applies the agent's allow-list before
scoring, so an entity hidden from the timer agent can't win the match and crowd
out a visible one.

A presence alarm searches by the occupied area's name, because hierarchy
matching scores names, not area IDs. When the match also covers a similarly
named room, it prefers a media player in the occupied area itself.

The shared visibility rule is unchanged:

| Entity allow-list | Visible to the timer agent |
| --- | --- |
| `IncludeForAgent == null` | Yes |
| Empty collection | No |
| Non-empty collection | Only when it lists `timer-agent` (case-insensitive) |

`SearchHierarchyForAgentAsync` now rejects a null, empty or whitespace caller
ID. A null caller would otherwise fall through to the unscoped hierarchy.
Timer and alarm resolution returns no target when the location service doesn't
implement `IAgentFilteredEntityLocationService`, and never falls back to the
unscoped searches.

## What stays the same

No new search API. `FindEntitiesByLocationAsync` and `SearchEntitiesAsync` keep
serving the dashboard and admin APIs, unscoped. Areas and floors stay unscoped
location metadata.

This is discovery filtering, not Home Assistant authorization. A full entity ID
such as `assist_satellite.kitchen` still passes through unchecked, and a
scheduled alarm doesn't recheck visibility on every playback loop. Execution-time
authorization needs its own change.

## Tests

Offline checks:

```powershell
dotnet test lucia.Tests --filter "(FullyQualifiedName~EntityLocation|FullyQualifiedName~TimerSkillTests|FullyQualifiedName~AlarmSkillTests|FullyQualifiedName~AlarmScheduledTaskTests|FullyQualifiedName~DirectSkillExecutorTests)&Category!=Integration&Category!=Eval"
```

The service contract also runs against a disposable Redis container (Docker
required):

```powershell
dotnet test lucia.Tests --filter "FullyQualifiedName~RedisEntityLocationServiceTests"
```

The contract tests cover the allow-list matrix, case handling and domain
filters for the scoped hierarchy, plus missing caller IDs. They also run the
real timer and alarm skills against the production location service and check
that a hidden satellite is never queried for live state. Skill tests check the
fixed agent ID, token forwarding, the occupied-room preference and that no
unscoped search runs. No live model or Home Assistant instance is needed.
