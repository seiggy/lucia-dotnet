# 11: Tune Jetson STT capacity

**What to build:** Select evidence-based STT concurrency and enhanced re-transcription settings for the Jetson Orin Nano 8GB so it sustains useful throughput without thermal throttling, memory pressure, or runaway queue latency.

**Blocked by:** 07: Give STT capacity one owner.

**Status:** ready-for-agent

- [ ] A controlled matrix measures concurrency levels and enhanced re-transcription modes against CPU, GPU, RSS, temperature, real-time factor, queue wait, and latency percentiles.
- [ ] Tests include steady traffic, bursts, permit exhaustion, client abandonment, and shutdown while work is queued.
- [ ] Recommended defaults are selected for the supported model and execution-provider combinations rather than assumed from x86 behavior.
- [ ] Configuration validation rejects values that exceed measured safe limits or explains when an operator overrides the recommendation.
- [ ] Transcript quality and speaker decisions remain within the baseline acceptance thresholds.
- [ ] The tuning report records the selected defaults, hardware state, model identifiers, and the rejected alternatives.
