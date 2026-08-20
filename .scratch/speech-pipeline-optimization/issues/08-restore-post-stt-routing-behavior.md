# 08: Restore post-STT routing behavior

**What to build:** Make the completed speech pipeline honor its specified routing decisions: known commands can use the fast path, ambiguous requests can fall back to orchestration, and configured unknown-speaker filtering is authoritative.

**Blocked by:** 05: Deepen transcript finalization; 06: Deepen speaker verification.

**Status:** ready-for-agent

- [ ] A known, authorized speaker with a confident command match executes the fast path and returns the resulting response.
- [ ] An ambiguous or unmatched command reaches the orchestration fallback when fallback is enabled.
- [ ] An unknown speaker produces no command execution, routing, or response when unknown-speaker filtering is enabled, while provisional tracking still follows the configured policy.
- [ ] Disabled routing or optional dependencies degrade to the documented transcript-only behavior without throwing.
- [ ] Transcript telemetry records the route decision, confidence, matched skill, filtering decision, response, and stage timings.
- [ ] End-to-end tests cover these outcomes through the Wyoming protocol and replace contradictory tests or names.
