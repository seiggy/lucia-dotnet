---
updated_at: 2026-07-20T10:13:43.764-04:00
focus_area: Runtime health & resource diagnostics (primary); Jetson K1 CUDA-EP verification (secondary)
active_issues:
  - ✓ Redis lifetime + health endpoint caching (completed 2026-07-20)
  - Jetson K1 (CUDA provider registration and kernel execution on physical device)
  - HA setup wizard configuration (Base URL, token)
---

# What We're Focused On

## Primary: Runtime Health & Resource Diagnostics ✓ (Stable)

Multi-agent review and fix for health endpoint caching and Redis container lifecycle issues. Completed 2026-07-20:
- **Redis ContainerLifetime Session:** Restores proxy endpoint allocation; eliminates Aspire 13.4 certificate handling mismatch
- **Health Output Cache:** Named 10-second policy for healthy 200 and safe unhealthy 503 responses (Set-Cookie/auth safeguard)
- **ProcessInstrumentation:** CPU/memory telemetry; RuntimeInstrumentation remains source for GC/allocation/thread-pool
- **Measurements:** Orphaned AppHost ~1.63GB cleaned; clean AgentHost baseline: 17MB WS, 53MB private memory
- **Test Coverage:** Three HTTP behavioral tests pass; no regressions

## Secondary: Jetson Bootstrap & K1 CUDA-EP Verification

Target hardware: Jetson Orin Nano Super Developer Kit, 8GB LPDDR5, 67 INT8 TOPS, 1024 CUDA cores, 32 tensor cores, 7W-25W power envelope.

1. **Bootstrap gates (B1–B3):** ✓ Complete (hardware confirmed, services running, setup wizard live)
2. **HA setup wizard:** PENDING (user: Base URL, token, entity mappings)
3. **K1 CUDA-EP verification:** Deferred (strict validation after wizard; requires kernel execution confirmation)
4. **K2–K5 stress testing:** Open (RTF, thermal, memory, WER, sustained streaming)
5. **Remaining architecture validation:** Data pipeline integration, Wyoming speech round-trip

