# 03: Capture Jetson CPU profiles remotely

**What to build:** Give an authenticated operator a repeatable way to collect a short CPU profile from the running Jetson, store it remotely, and inspect managed and native speech-processing costs in Visual Studio without leaving diagnostic tooling active afterward.

**Blocked by:** 01: Deploy remote observability stack.

**Status:** ready-for-agent

- [ ] A server-side command starts a bounded 30–60 second capture on the Jetson over an authenticated management connection.
- [ ] The capture uses .NET 10 Linux CPU sampling with managed and native stacks when the Jetson kernel supports it, and reports a clear managed-only fallback otherwise.
- [ ] The resulting artifact is uploaded to remote storage with commit, container image digest, model identifiers, scenario, Jetson power mode, capture mode, and timestamps.
- [ ] Visual Studio 2026 Enterprise can open a captured artifact and display CPU stacks for a known speech workload.
- [ ] Capture duration, local disk usage, and concurrent captures are bounded; cancellation and failure leave no profiler process or temporary artifact behind.
- [ ] A measured overhead check records the CPU, memory, and latency cost of an active capture.
