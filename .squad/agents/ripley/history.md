# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant, built on .NET 10/C# 14 with Microsoft Agent Framework
- **Stack:** .NET 10, C# 14, Aspire 13, Ollama (local LLMs), Azure OpenAI (judge), xUnit, AgentEval, MongoDB/SQLite, Redis/InMemory
- **Created:** 2026-03-26

## Key Architecture

- **Agents:** LightAgent, ClimateAgent, ListsAgent, SceneAgent, GeneralAgent, DynamicAgent, OrchestratorAgent, MusicAgent (separate project)
- **Orchestration:** RouterExecutor pattern with multi-agent routing, WorkflowFactory with custom executors
- **Evaluation:** lucia.EvalHarness (TUI + reports), lucia.Tests/Orchestration/ (xUnit eval tests)

## Learnings

### 2026-07-17 (second pass): Jetson voice — verified it's a port, not a rewrite

Primary sources confirm the incumbent (Family C) is the answer. sherpa-onnx's own `build-aarch64-linux-gnu.sh` documents the exact board (Jetson Orin Nano Dev Kit Super, JetPack 6.2/L4T 36.4.3, CUDA 12.6, cuDNN 9) with `SHERPA_ONNX_ENABLE_GPU=ON` + `SHERPA_ONNX_LINUX_ARM64_GPU_ONNXRUNTIME_VERSION=1.18.1`, and `cmake/onnxruntime-linux-aarch64-gpu.cmake` pulls ORT from `csukuangfj/onnxruntime-libs`. The `org.k2fsa.sherpa.onnx` NuGet ships a **CPU-only** linux-arm64 native runtime — GPU means swapping `libsherpa-onnx-c-api.so` + `libonnxruntime.so` for the aarch64 GPU build and passing `Provider="cuda"` (already configured in `Dockerfile.voice`). **No new engine class needed** — `HybridSttEngine.BuildConfig` already parametrizes the provider. Parker's `JetsonSttEngine` was over-build; rejected.

True multi-speaker **diarization** is available through the *same* managed wrapper via `OfflineSpeakerDiarization` (pyannote-segmentation-3.0 ONNX re-hosted by k2-fsa + eres2net embedding) — distinct from Lucia's current voice-print **verification+enrollment** (embedding+cosine). Parakeet-TDT-0.6b-v2 = CC-BY-4.0 (commercial OK); it's a batch/offline transducer, so `HybridSttSession`'s periodic re-decode already covers progressive partials — no streaming decoder.

Team-artifact hygiene: Hicks's memo carried several unsupported/wrong claims to reject — ORT `1.24.4 cp310` (a *Python* wheel, wrong layer + version; correct is 1.18.1 native tar.bz2), Orin Nano 8GB "512 cores" (it's **1024** CUDA / 32 Tensor; 512 is the 4GB), a Python-Whisper CPU fallback (violates no-Python-runtime), `nvidia-smi`/Python probes (Jetson uses `tegrastats`), `latest` tags, invented latency/temp/concurrency numbers, and 8 speculative new infra files. Riva/Triton rejected as unneeded (one runtime suffices), not merely "over-engineered." Deployment is L4T/BSP **rootfs flash**, not an ISO.

### 2026-07-17: Jetson Orin Nano voice pipeline — decision frame (research only)

Framed the "replace .NET Wyoming audio path with a non-Python Jetson stack" ask. Key reframe: **Lucia's voice stack is already non-Python** — it is a C# host (`lucia.Wyoming`, `WyomingServer` IHostedService on TCP `10400`) over native C++ inference via `org.k2fsa.sherpa.onnx` (STT/VAD/KWS/speaker embedding) and `Microsoft.ML.OnnxRuntime` (GTCRN enhancement + Granite). Pipeline order: audio-start → wake/VAD → per-frame GTCRN enhancement → streaming STT (Hybrid/Sherpa/Granite) → utterance buffer → diarization/speaker verification → command routing/skill dispatch → transcript events. Contracts to preserve: `ISttEngine`/`ISttSession`, `IVadEngine`, `IWakeWordDetector`, `ISpeechEnhancer`, `IDiarizationEngine`, `ICommandRouter`, and the Wyoming JSONL+PCM protocol (HA-facing).

The real problem is **Jetson GPU enablement, not host language.** Decisive external constraint: official ORT-GPU (CUDA) native libs are **linux-x64 only**; `Microsoft.ML.OnnxRuntime.Gpu.Linux` ships no aarch64+CUDA `.so`. aarch64+CUDA ORT must come from a Jetson-specific build (jetson-zoo / PINTO / build-from-source) and be overlaid the same way `Dockerfile.voice` already overlays x64 GPU `.so`s. Jetson CUDA is delivered via L4T/JetPack — desktop `nvidia/cuda:*` images (current `Dockerfile.voice` base) will NOT work on Jetson; needs `nvcr.io/nvidia/l4t-*`. Current Jetson build sidesteps all of this with `ExcludeSpeech=true`.

**Recommendation:** keep the incumbent (C# host over native C ABI, Family C), and design the spike to *falsify* it — prove CUDA-accelerated ORT + sherpa-onnx can be P/Invoked from the existing .NET host on real Orin Nano within latency/thermal/memory budget. Only escalate to Rust (Family B) or C++ (Family C-native) if the spike falsifies C. Ponytail: a language rewrite with no evidence the incumbent fails on hardware is speculative and rejected. Orin Nano Super facts (cite datasheet): Ampere 1024 CUDA / 32 Tensor cores, 8GB LPDDR5 102 GB/s, 7/15/25W + MAXN, JetPack 6.2 / CUDA 12 / TensorRT. No hardware perf invented — the spike must produce it.

### 2026-05-30: GitHub Issue Inbox Re-triage (50 issues, all incorrectly bulk-labeled `squad:lambert`)

All 50 open `squad`-labeled issues were found carrying `squad:lambert` after a bad bulk-triage. Correct labels applied by domain routing:

| Member    | Count | Issues |
|-----------|-------|--------|
| parker    | 17    | #176 #175 #174 #173 #172 #171 #170 #169 #168 #167 #166 #165 #158 #154 #153 #145 #140 |
| hicks     | 11    | #181 #164 #162 #161 #159 #155 #151 #147 #142 #138 #135 |
| brett     | 6     | #183 #182 #180 #179 #178 #177 |
| lambert   | 4     | #144 #148 #152 #156 (correctly kept) |
| dallas    | 4     | #134 #137 #141 #150 |
| kane      | 3     | #136 #139 #143 |
| bishop    | 3     | #149 #157 #160 |
| ripley    | 1     | #146 |
| ash       | 1     | #163 |

**Root cause:** Bulk triage without reading domain map; lambert's narrow scope (writing test scenarios, assertions, skill unit tests, provider-free coverage) was applied to all issues indiscriminately. 46 of 50 issues were wrong.

---

## Durable Learnings (Condensed)

### 2026-05-29: Whole-Solution Health Review — Systemic Intent-Enforcement Gaps

The solution's biggest risk is **intent-vs-enforcement drift**, not localized bugs. Aspirational guarantees (observability, reproducibility, CI gating, UTC discipline) silently fail at the seams:

- **CI is non-functional on repo default branch (master)** — squad workflows trigger on main/dev/preview but not the real default; build/test steps are cho stubs. Treat green CI as meaningless until infra issues #1-#3 fixed.
- **OTel source/meter names are ordinal case-sensitive** — Lucia.* in code never matches registered lucia.*; drops orchestration spans + most skill/service/task meters. Use shared name constants.
- **DateTime.Now vs UtcNow is cross-cutting bug** — appears in 4 domains (agent refresh gates, MusicAgent, SQLite text timestamps, test naming). UTC-behind timezones cause per-request rebuilds; non-UTC offsets corrupt range queries.
- **Routing brain has near-zero deterministic tests** — RouterExecutor/ResultAggregatorExecutor/LuciaEngine coverage is provider-gated eval suites that skip in CI.
- **Voice pipeline is the observability model citizen** — cite it when arguing OTel patterns are achievable.

**Decision:** Three-wave remediation sequence. Wave 1 blocks all later work: fix CI gate, restore telemetry naming consistency, apply UTC fixes, close unauthenticated API surfaces.

### 2026-03-27 to 2026-05-25: Architecture & Config Decisions (Archived)

**Synthesized insights:**
- **Entity matching cascading elimination** — replaces heuristic scoring; deterministic pipeline (location→domain→entity) for simple commands, LLM fallback for complex.
- **MAF Workflows v2 feasibility** — consolidate WorkflowFactory into DynamicOrchestrationWorkflow (10-15% complexity reduction, no risk).
- **Config poll intervals** — increase SQLite/Mongo from 5s to 30s (API writes already provide instant reload).
- **ONNX thread pool idle spin** — set ORT_THREADPOOL_SPIN_CONTROL=0 (highest-impact, zero code change); reduce NumThreads 4→2 (Phase 2).
- **Agent timeout handling** — map OperationCanceledException to user-readable failures; use CancellationToken.None for aggregation to survive upstream cancellation.

### Previous Releases & Decisions
- Prompt cache architecture: two-tier system (routing + chat) with SHA256 + semantic fallback
- Embedding provider changes: force rebuild on provider switch to avoid vector-space drift
- Jetson Nano ARM64: ExcludeSpeech=true preserves command routing while dropping voice runtime
- Personality response pipeline: production-ready with IPersonalityResponseRenderer

- Participated in 2026-05-29 health review
## 2026-05-31 — PR #195 Copilot Comment Resolution

Triaged full review-comment batch, established pre-push review gate (decision #24), and fixed EvalHarness Reports issues (E1-E7). Consolidated with Hicks/Parker into commit 9809a36.

## 2026-07-17 — Jetson Orin Nano Research Consolidation

Scribe task: consolidated all research inputs (Brett/Parker/Hicks) into Decision 27 (Jetson Orin Nano Native Voice Inference). Treated the second-pass corrected synthesis as authoritative; rejected over-engineered proposals (Riva, Triton, Python fallback, speculative infra). Hardware target locked to Jetson Orin Nano Super 8GB only. Architecture confirmed: Family C (C# Wyoming host over CUDA-accelerated sherpa-onnx + ORT 1.18.1 GPU). Merged inbox into decisions.md; created orchestration logs for all four team members; deleted inbox files.

