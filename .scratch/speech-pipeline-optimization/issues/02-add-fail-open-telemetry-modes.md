# 02: Add fail-open telemetry modes

**What to build:** Let operators choose the cost of observability on the Jetson while guaranteeing that telemetry export can never delay the speech pipeline. The selected mode takes effect predictably at process startup and controls collection as well as export.

**Blocked by:** 01: Deploy remote observability stack.

**Status:** ready-for-agent

- [ ] Operators can select `Off`, `Metrics`, `Trace`, or `Profile` mode through deployment configuration.
- [ ] `Off` creates no telemetry providers or exporters; `Metrics` emits low-frequency runtime, process, and speech metrics without spans; `Trace` adds sampled spans; `Profile` adds the correlation metadata required by profiling captures.
- [ ] The existing telemetry enable setting remains compatible or produces a clear migration error instead of being silently ignored.
- [ ] Wyoming trace sources and speech-pipeline meters are registered and appear under the expected application resource in the remote backend.
- [ ] OTLP export uses bounded batching, short timeouts, and drop-on-overflow behavior; an unavailable collector does not increase speech latency or retain an unbounded queue.
- [ ] Automated tests verify each mode and the fail-open behavior, including an unreachable collector.
