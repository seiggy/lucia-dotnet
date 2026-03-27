# Decision: DB-Backed Options Pattern for CommandRoutingOptions

**Author:** Parker (Backend/Platform)
**Date:** 2025-07-18
**Status:** Implemented

## Context

Zack reported that `CommandRoutingOptions` was "reading from IConfiguration (appsettings.json)" instead of the database, and that dashboard saves weren't connecting to the runtime. The suspicion was that the options needed a separate DB-backed binding pattern.

## Investigation

After tracing the full write→read flow:

1. **Write path:** Dashboard → `PUT /api/config/sections/Wyoming:CommandRouting` → `IConfigStoreWriter.SetAsync("Wyoming:CommandRouting:Enabled", ...)` → DB row with `key="Wyoming:CommandRouting:Enabled"`, `section="Wyoming"`
2. **Read path:** `MongoConfigurationProvider`/`SqliteConfigurationProvider` polls DB every 5s → loads ALL entries into `Data` dict → calls `OnReload()` → `IConfiguration` change tokens fire → `IOptionsMonitor<CommandRoutingOptions>.CurrentValue` refreshes

The binding `Configure<T>(builder.Configuration.GetSection("Wyoming:CommandRouting"))` **already reads from the DB** because the custom ConfigurationProviders are registered in the pipeline. This is the same pattern every other options class uses.

## Root Cause

Two bugs in `ConfigurationApi`:

1. **Section column mismatch:** `SetAsync` stores `section = "Wyoming"` (first segment only), but `GetSectionAsync` queried with `GetEntriesBySectionAsync("Wyoming:CommandRouting")` — exact match on section column, so it never found nested section entries. The endpoint fell back to `IConfiguration`, which might show stale values before the provider's next poll.

2. **No immediate reload:** `UpdateSectionAsync` wrote to the DB but didn't trigger `IConfigurationRoot.Reload()`, so options monitors had to wait for the 5-second poll interval.

## Decision

- **Keep `Configure<T>(GetSection(...))` pattern** — it IS the DB-backed pattern. The DB is a configuration source in the pipeline. No need for a separate `IPostConfigureOptions` or custom monitor.
- **Fix `GetSectionAsync`** to use `GetEntriesByKeyPrefixAsync(section + ":")` instead of `GetEntriesBySectionAsync(section)`. Key-prefix matching works correctly for all sections, nested or flat.
- **Add `IConfigurationRoot.Reload()`** in `UpdateSectionAsync` after writes, forcing immediate provider reload.

## Impact

- Dashboard saves are now immediately visible to `IOptionsMonitor<CommandRoutingOptions>.CurrentValue`
- Dashboard reads now pull directly from DB entries for nested sections
- No changes needed to `ServiceCollectionExtensions.cs` or the options binding pattern
- Pattern is consistent with how all other options classes work

## Alternatives Considered

- **Custom `IPostConfigureOptions<T>`:** Would read from `IConfigStoreWriter` on each options construction. Rejected because it duplicates what the ConfigurationProvider already does.
- **Custom `IOptionsChangeTokenSource<T>`:** Would add a DB-polling change token. Rejected because the ConfigurationProvider's `OnReload()` already fires the standard change tokens.
- **Fix `SetAsync` section derivation:** Could store full section path instead of first segment. Rejected because it would break existing entries and the section column is primarily for `ListSectionsAsync` grouping.
