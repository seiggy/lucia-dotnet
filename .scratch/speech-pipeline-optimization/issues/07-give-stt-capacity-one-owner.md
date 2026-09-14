# 07: Give STT capacity one owner

**What to build:** Make one deep module own STT permits, queue timing, inference lifetime, retranscription acquisition, cancellation, and shutdown so speech sessions no longer coordinate raw semaphore state.

**Blocked by:** 04: Establish a reproducible speech baseline.

**Status:** ready-for-agent

- [ ] Primary STT and enhanced re-transcription acquire capacity through the same module interface.
- [ ] Permit release is exactly once across normal completion, cancellation, client disconnect, inference failure, and server shutdown.
- [ ] Queue-wait telemetry belongs to the permit lifetime and remains correlated with the utterance that waited.
- [ ] The production session creation path no longer exposes semaphore implementation details; compatibility is preserved long enough to migrate existing callers and tests safely.
- [ ] Deterministic concurrency tests prove active inference never exceeds the configured limit and non-STT sessions remain responsive while permits are exhausted.
- [ ] Shutdown and abandonment tests prove no permit leak, deadlock, or unbounded wait.
