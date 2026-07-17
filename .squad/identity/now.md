---
updated_at: 2026-07-17T14:02:00.107-04:00
focus_area: Native Jetson Orin Nano voice pipeline research
active_issues: []
---

# What We're Focused On

Researching a non-Python, CUDA-accelerated voice pipeline for NVIDIA Jetson Orin Nano that can replace Lucia's existing .NET Wyoming stack.

Target hardware: Jetson Orin Nano Super Developer Kit, 8GB LPDDR5, 67 INT8 TOPS, 1024 CUDA cores, 32 tensor cores, and a 7W-25W power envelope.

## Current Priority

1. Validate native runtime and model support for STT, voice isolation, diarization, and enrolled voice prints.
2. Define the smallest viable C++, Rust, or C#-interop architecture.
3. Preserve Lucia and Home Assistant integration contracts while replacing the runtime.
4. Define a reproducible single-build Jetson deployment image.
5. Cross-compile all native ARM64/CUDA artifacts off-device; the Jetson installs prebuilt binaries only.
