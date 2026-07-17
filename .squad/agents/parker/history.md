# Parker's Work History — Backend / Platform Engineer

## Current Role
- **Architecture & host platform:** lucia.AgentHost, lucia.A2AHost, lucia.Data, lucia.Wyoming
- **API surfaces & orchestration:** 40+ endpoints, command routing, agent routing, workflow execution
- **Infrastructure:** Docker, Kubernetes, Helm, systemd deployment patterns
- **Latest focus:** Jetson native voice boundary analysis and infrastructure reviews

## Key Systems Owned
- lucia.AgentHost/ — Main host with 40+ API endpoint groups
- lucia.A2AHost/ — Satellite agent host for mesh mode
- lucia.Agents/ — 7 built-in agents (Light, Climate, Lists, Scene, General, Dynamic, Orchestrator)
- lucia.Data/ — Multi-backend data layer (Redis/InMemory cache, MongoDB/SQLite store)
- lucia.Wyoming/ — Speech runtime, command routing, Wyoming protocol

## Recent Work (2026-07)

### Jetson Orin Nano Native Inference Boundary (2026-07-17 — Research)
Traced Wyoming host-to-native boundary end-to-end; confirmed in-process P/Invoke (Family C) as preferred over new engine classes.

**Key findings:**
- Wyoming flow complete trace: WyomingServer → WyomingSession state machine → inference engines → transcript storage
- Narrowest replacement seam: `ISttEngine` / `ISttSession`
- Managed code unchanged; only native-lib sourcing and GPU-enablement required
- **Rejected proposals:** new `JetsonSttEngine`/`JetsonSttSession` (HybridSttEngine already parametrizes provider)
- Interface contracts to preserve: `ISttEngine`, `ISttSession`, `IVadEngine`, `IWakeWordDetector`, `ISpeechEnhancer`, `IDiarizationEngine`, Wyoming protocol
- ExcludeSpeech build flag pattern already established

**Status:** Boundary strategy locked to Family C (in-process P/Invoke). Architecture seams identified and preserved. No code changes.

## Archived Work
- See `history-archive.md` for prior entries (orchestration span disposal, InputRequired timeouts, auth handler disposal, PostgreSQL index reviews, infra reviews)

## Next Steps
- Await coordinator approval for PoC hardware
- PoC Stages 1–5 on physical Orin Nano 8GB (~3–4 weeks)
- No managed code changes required if Hardware K-gates pass
