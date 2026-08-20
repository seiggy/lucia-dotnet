# 04: Establish a reproducible speech baseline

**What to build:** Give maintainers a repeatable speech workload and baseline report that can prove whether later architecture and optimization changes improve Jetson resource use without harming voice quality.

**Blocked by:** 02: Add fail-open telemetry modes; 03: Capture Jetson CPU profiles remotely.

**Status:** ready-for-agent

- [ ] A versioned audio corpus exercises wake detection, streaming STT, enhanced re-transcription, known and unknown speakers, routing, and concurrent satellites.
- [ ] A single command runs the corpus against the Jetson and records CPU time, peak RSS, managed allocations, queue wait, real-time factor, and end-to-end p50 and p95 latency.
- [ ] The report records transcript accuracy, wake decisions, and speaker decisions so resource improvements cannot hide quality regressions.
- [ ] Every run records commit, image digest, model identifiers, model execution provider, Jetson power mode, temperature, and telemetry mode.
- [ ] Baselines quantify the overhead of `Off`, `Metrics`, `Trace`, and `Profile` modes under the same workload.
- [ ] Repeated runs establish an acceptable variance threshold and fail when environmental drift makes a comparison unreliable.
