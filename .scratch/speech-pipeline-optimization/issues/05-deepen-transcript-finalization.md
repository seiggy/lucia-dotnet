# 05: Deepen transcript finalization

**What to build:** Make a completed utterance cross one module interface that owns enhanced re-transcription, speaker verification, Wyoming publication, telemetry snapshots, and transcript persistence while preserving current observable behavior.

**Blocked by:** 04: Establish a reproducible speech baseline.

**Status:** ready-for-agent

- [ ] Wyoming protocol state remains in the session module while completed-utterance decisions move behind one transcript-finalization seam.
- [ ] The deep module owns enhanced-audio selection, fallback behavior, telemetry timing, event publication data, background persistence, and failure isolation.
- [ ] Existing protocol output, transcript records, speaker tags, dashboard events, and enhanced-clip behavior remain unchanged in characterization scenarios.
- [ ] Tests exercise finalization through its module interface without opening TCP connections or resolving an entire dependency container.
- [ ] Tests that only asserted the removed shallow implementation are replaced rather than layered beneath the new test surface.
- [ ] The baseline corpus shows no material latency, CPU, memory, allocation, or quality regression.
