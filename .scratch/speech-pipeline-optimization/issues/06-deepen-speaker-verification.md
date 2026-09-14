# 06: Deepen speaker verification

**What to build:** Make speaker verification one coherent domain decision so callers do not coordinate embedding extraction, profile lookup, unknown-speaker policy, provisional tracking, adaptive updates, and event publication themselves.

**Blocked by:** 05: Deepen transcript finalization.

**Status:** ready-for-agent

- [ ] One deep speaker-verification module owns the known-or-unknown decision and exposes one interface to transcript finalization.
- [ ] Mongo-backed and in-memory profile adapters remain behind the existing real seam and produce equivalent decisions for the same profile data.
- [ ] Raw versus enhanced verification audio is selected explicitly and consistently with enrollment and configuration.
- [ ] Known speakers, unknown speakers, provisional profile creation, enrollment suggestions, adaptive updates, disabled features, and failures are covered through the module interface.
- [ ] Terminology and telemetry use “speaker verification” rather than implying multi-speaker diarization.
- [ ] The baseline corpus shows unchanged speaker decisions and no material resource regression.
