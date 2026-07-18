---
updated_at: 2026-07-18T15:31:35.572-04:00
focus_area: Jetson bootstrap deployment + K1 CUDA-EP verification gate
active_issues:
  - K1 (CUDA provider registration and kernel execution on physical device)
  - HA setup wizard configuration (Base URL, token)
---

# What We're Focused On

Bootstrap deployment of three-service unified Docker Compose (AgentHost + Redis + PostgreSQL) to physical Jetson Orin Nano Super 8GB succeeded. All services healthy, setup wizard accessible. Next phase: user completes HA configuration; strict K1 (CUDA-EP) gate validation.

Target hardware: Jetson Orin Nano Super Developer Kit, 8GB LPDDR5, 67 INT8 TOPS, 1024 CUDA cores, 32 tensor cores, 7W-25W power envelope.

## Current Priority

1. **Bootstrap gates (B1–B3):** ✓ Complete (hardware confirmed, services running, setup wizard live)
2. **HA setup wizard:** PENDING (user: Base URL, token, entity mappings)
3. **K1 CUDA-EP verification:** Deferred (strict validation after wizard; requires kernel execution confirmation)
4. **K2–K5 stress testing:** Open (RTF, thermal, memory, WER, sustained streaming)
5. **Remaining architecture validation:** Data pipeline integration, Wyoming speech round-trip

