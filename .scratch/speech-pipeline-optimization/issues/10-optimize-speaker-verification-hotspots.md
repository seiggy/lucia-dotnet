# 10: Optimize speaker-verification hotspots

**What to build:** Reduce the largest measured speaker-verification cost while preserving identity, authorization, provisional-profile, and adaptive-update decisions.

**Blocked by:** 06: Deepen speaker verification; 08: Restore post-STT routing behavior.

**Status:** ready-for-agent

- [ ] A CPU profile and allocation measurement identify whether embedding inference, audio copying, profile retrieval, comparison, or persistence is the dominant cost.
- [ ] The optimization remains inside the speaker-verification module or behind its existing profile seam.
- [ ] The selected hotspot improves by at least 10% in CPU samples, elapsed CPU time, allocated bytes, or remote profile-store calls under the reproducible workload.
- [ ] Known, unknown, threshold-edge, provisional, and adaptive-update decisions remain unchanged across the fixed corpus.
- [ ] End-to-end p95 latency and peak memory do not regress.
- [ ] Before and after traces and profiles are stored with the change report.
