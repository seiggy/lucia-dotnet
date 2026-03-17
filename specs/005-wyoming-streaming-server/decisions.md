# Design Decisions: Wyoming Streaming Voice Server

**Spec**: 005-wyoming-streaming-server
**Date**: 2026-03-13
**Status**: Resolved

## Blocker Resolutions

### D1: Wyoming→Lucia Orchestrator Contract
**Decision**: Build the Wyoming voice pipeline as a new pipeline type alongside the existing text pipeline. Retool `LuciaEngine` to support direct invocation from the Wyoming pipeline with metadata-rich requests (speaker ID, confidence, partial matches, audio context). This is expected to require modifications to the orchestrator interface during Phase 2 implementation — the exact contract will be designed when we get there.

**Rationale**: The current `ProcessRequestAsync(text, taskId, sessionId)` is too narrow for voice. Rather than force voice into the text API, we extend the orchestrator to support voice-native invocation. This keeps the text pipeline untouched while enabling richer voice interactions.

**Impact**: Phase 1 can proceed using the existing text API for basic STT→orchestrator flow. Phase 2 will require orchestrator changes for fast-path dispatch and metadata enrichment.

---

### D2: Session/Context Continuity
**Decision**: Design a session mapping layer in the Wyoming pipeline that tracks Lucia `contextId`/`sessionId` per satellite connection. The orchestrator contract changes (D1) will include returning session IDs to the caller. Exact implementation deferred to Phase 2/4 when `continue_conversation` is implemented.

**Rationale**: Session continuity is essential for multi-turn voice conversations but requires orchestrator changes that align with D1. Phase 1 can work with stateless request/response.

---

### D3: Raw TCP Security — Device Pairing
**Decision**: Implement a device pairing system. Wyoming satellites must be explicitly paired before the server accepts their audio streams. This includes:
- A device pairing page in the dashboard for Satellite1 devices
- Home Assistant integration for HA-managed satellites
- Unpaired connections are rejected at the TCP level after the initial `describe`/`info` exchange
- Pairing tokens stored in MongoDB alongside device metadata

**Rationale**: An open TCP port that can trigger home automations is a security risk. Device pairing is the standard approach used by Bluetooth, HomeKit, and other IoT protocols.

---

### D4: Browser Onboarding Requires Full Lucia Setup First
**Decision**: The browser-based voice onboarding page is NOT available pre-setup. Users must complete full Lucia setup (API keys, initial configuration) before the Wyoming server comes online and voice onboarding becomes accessible. Standard AgentHost authentication protects all onboarding endpoints.

**Rationale**: Allowing anonymous voice profile enrollment on the LAN is a security hole. Requiring setup first ensures proper auth is in place before biometric data collection begins.

**Impact**: Remove any "works before setup" language from specs. Onboarding endpoints use standard API key auth like all other AgentHost APIs.

---

### D5: TTS Audio Format
**Decision**: TTS streams audio at the native output quality of the TTS model (typically 24kHz for Qwen3-TTS and Chatterbox). If the satellite cannot handle the native rate, fall back to 16kHz resampling. Audio format is negotiated via the Wyoming `audio-start` event.

**Rationale**: Downsampling loses quality. Stream at native quality when possible, convert only when necessary.

**Impact**: Update contracts to show 24kHz as default TTS output. Resampling is a fallback, not the default path.

---

### D6: No Python Sidecar — Direct ONNX Only
**Decision**: All inference (STT, TTS, wake word, diarization) runs via direct ONNX Runtime in C#. No Python sidecar processes. This applies to both Chatterbox Turbo (direct ONNX) and Qwen3-TTS (direct ONNX, not the ElBruno.QwenTTS file-based NuGet wrapper).

**Rationale**: Single-process simplicity is a core architectural goal. Python sidecars add deployment complexity, IPC latency, and operational overhead. Direct ONNX inference in C# is the cleanest path.

**Impact**: 
- Remove all Python sidecar references from specs
- Qwen3-TTS integration changes from ElBruno.QwenTTS NuGet to direct ONNX Runtime inference (same approach as Chatterbox)
- Both TTS engines need custom C# tokenizers ported from their Python implementations
- This enables the <500ms TTFA target by eliminating file I/O overhead

---

## Notable Item Resolutions

### N1: Mesh Mode
**Decision**: Mesh mode is being retired. The Wyoming pipeline assumes standalone/in-process mode only. A legacy demo branch will preserve mesh mode if needed, but it's not a design constraint for Wyoming.

**Impact**: Remove any mesh mode hedging from specs. Direct DI dispatch is the only path.

---

### N2: Partial Transcript Events
**Decision**: Support partial (streaming) transcript events. Add a `partial-transcript` event type to the Wyoming contract with `is_final: false` semantics. Final results use the existing `transcript` event.

**Impact**: Update contracts and Phase 1 to include partial transcript support.

---

### N3: ONNX Runtime Version Pinning
**Decision**: Pin `Microsoft.ML.OnnxRuntime` (and GPU provider packages) in `Directory.Packages.props` following the repo's central package management convention. Verify compatibility between sherpa-onnx's bundled native libs and the separately-pinned ONNX Runtime.

---

### N4: Biometric Data Retention
**Decision**: User's responsibility. Lucia is self-hosted — users own and control their data. No special consent/retention/export framework needed. Standard MongoDB backup/restore covers voice profiles.

---

### N5: Qwen3-TTS — Direct ONNX (Not NuGet Wrapper)
**Decision**: Use direct ONNX Runtime inference for Qwen3-TTS instead of the ElBruno.QwenTTS NuGet package. The NuGet wrapper is file-based (writes WAV, reads back) which prevents streaming and adds latency. Direct ONNX enables true streaming synthesis with <500ms TTFA.

**Impact**: Replace ElBruno.QwenTTS dependency with direct ONNX Runtime. Port the Qwen3-TTS tokenizer and inference pipeline to C#. More implementation work but better performance and streaming support.

---

### N6: Wake Word Hot Reload Strategy
**Decision**: On wake word configuration changes, broadcast a graceful disconnect to connected satellites and rely on client-side reconnection. The keyword spotter is recreated with new keywords, and satellites automatically reconnect within seconds. This is the simplest reliable approach.

**Future**: Investigate adding a server-status protocol extension for Satellite1 firmware to enable graceful reload notifications without full disconnection.

---

### N7: Health Check Alignment with Configuration
**Decision**: Health checks are configuration-aware. If Wyoming/STT/TTS are disabled in config, their health checks report healthy (not applicable). Only enabled-but-broken services report unhealthy. This prevents false alarms when optional features are intentionally disabled.

---

### N8: Merged Onboarding API
**Decision**: Consolidate the duplicate onboarding APIs (`/api/speakers/onboarding/*` and `/api/onboarding/*`) into a single unified flow at `/api/onboarding/*`. This single flow handles voice profile enrollment, wake word selection, and calibration in one guided session.

---

### N9: Diarization Terminology
**Decision**: Clarify that "diarization" in this spec means **single-utterance speaker verification** (who is speaking this command?), not multi-speaker meeting-style segmentation. The speaker embedding comparison against enrolled profiles is the core capability. True multi-speaker diarization is out of scope.
