## Context

Sensor, light, climate, and other skills rely on cached embeddings for hybrid entity matching. When the configured embedding provider changes, previously persisted vectors may be stale or dimensionally incompatible with the new provider.

## Decision

On embedding-provider changes, skills that cache embeddings should force a one-time rebuild from source data instead of trusting cached embeddings. For the sensor agent fix, `SensorControlSkill` now bypasses Redis cache reads during invalidation, regenerates embeddings from Home Assistant state, and then repopulates cache entries with the new provider output.

## Rationale

Persisted embeddings are provider-specific artifacts, not stable business data. Rebuilding on provider change avoids mismatched vector spaces and keeps Redis useful for steady-state reads after the refresh completes.
