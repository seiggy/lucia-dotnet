# 09: Optimize transcript finalization hotspots

**What to build:** Reduce the largest measured CPU or allocation cost inside transcript finalization while preserving protocol output, persistence, routing behavior, and speech quality.

**Blocked by:** 05: Deepen transcript finalization; 08: Restore post-STT routing behavior.

**Status:** ready-for-agent

- [ ] A baseline CPU profile and allocation measurement identify the selected finalization hotspot before implementation begins.
- [ ] The optimization remains behind the transcript-finalization interface and does not expose new implementation knowledge to callers.
- [ ] The selected hotspot improves by at least 10% in CPU samples, elapsed CPU time, or allocated bytes under the reproducible workload.
- [ ] End-to-end p95 latency does not regress, and transcript, speaker, routing, and persistence outcomes remain equivalent.
- [ ] Before and after traces and profiles are stored with the change report so the result can be independently reviewed.
- [ ] Any tempting but unmeasured optimization is recorded as follow-up evidence rather than included in this ticket.
