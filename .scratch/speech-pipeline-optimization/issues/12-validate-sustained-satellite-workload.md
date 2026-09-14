# 12: Validate sustained satellite workload

**What to build:** Prove that the optimized speech pipeline remains stable under sustained multi-satellite use and publish a final comparison that explains the resource savings and remaining costs.

**Blocked by:** 09: Optimize transcript finalization hotspots; 10: Optimize speaker-verification hotspots; 11: Tune Jetson STT capacity.

**Status:** ready-for-agent

- [ ] A sustained workload exercises the supported number of wake streams, concurrent STT bursts, known and unknown speakers, routing, disconnects, backend outages, and graceful shutdown.
- [ ] The run shows no unbounded memory growth, permit leak, deadlock, dropped command outside configured overload behavior, or thermal collapse.
- [ ] Final measurements compare CPU time, peak RSS, allocations, real-time factor, queue wait, and p50 and p95 latency with the ticket 04 baseline.
- [ ] Transcript accuracy, wake decisions, speaker decisions, and routing outcomes remain within their accepted quality thresholds.
- [ ] Telemetry-off and normal metrics modes are both measured so production overhead is explicit.
- [ ] A final report records achieved improvements, unresolved hotspots, recommended Jetson configuration, and links to representative traces and profiles.