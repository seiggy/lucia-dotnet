# Squad Decisions

## Active Decisions

### 25. Aspire 13.4 Redis — Disable Client Certificate Trust Scope (Parker, 2026-07-01)

**Summary:** Aspire.Hosting 13.4.2 split certificate handling into server-HTTPS and client-trust APIs. Redis was reported UNHEALTHY in dashboard even though the container ran fine because `.WithoutHttpsCertificate()` disabled only server TLS, but Aspire still injected a CA cert file (`--tls-ca-cert-file`), causing the health probe to attempt TLS against the plaintext server and fail (EOF). Fix: added `.WithCertificateTrustScope(CertificateTrustScope.None)` to the Redis chain in `lucia.AppHost/AppHost.cs`. Branch: fix/package-updates-build.


### 24. Transitive Package Vulnerability Pins (Parker, 2026-07-01)

**Summary:** Pinned three vulnerable transitive dependencies in Directory.Packages.props using CentralPackageTransitivePinningEnabled: MessagePack 2.5.198→2.5.302 (GHSA via StreamJsonRpc), Microsoft.OpenApi 2.0.0→2.7.5 (GHSA-v5pm-xwqc-g5wc), SQLitePCLRaw.lib.e_sqlite3 2.1.11→3.50.3 (GHSA-2m69-gcr7-jv3q). Build verified: 0 warnings, 0 errors. Branch: fix/package-updates-build.


### 14. Validate HA access token before opening WebSocket (Bishop, 2026-05-30)

**Summary:** Added null/whitespace guard in `HomeAssistantClient.SendWebSocketCommandAsync` to validate token presence before opening WS connection. Missing token now throws `InvalidOperationException` with actionable message instead of opaque `auth_invalid` server error. PR #188. See full document below.

### 15. Constant-time comparison for internal service token (Parker, 2026-05-30)

**Summary:** Replaced `string.Equals(..., StringComparison.Ordinal)` with `CryptographicOperations.FixedTimeEquals` in `InternalTokenAuthenticationHandler` token validation to prevent timing side-channels. SHA-256 hash comparison ensures constant-time regardless of token content. PR #185. See full document below.

### 16. Global React Error Boundary (Kane, 2026-05-30)

**Summary:** Added global `ErrorBoundary` class component in lucia-dashboard wired as outermost wrapper in main.tsx (outside all providers). Catches render errors and displays fallback UI with "Try again" and "Reload page" recovery actions. PR #184. See full document below.

### 17. Snapshot pipeline-stage timings before background transcript save (Brett, 2026-05-30)

**Summary:** Fixed race condition where `WyomingSession.ResetUtteranceAudio()` zeroed timing fields while background `Task.Run` was reading them. Now snapshots four timing fields into locals before `Task.Run` and passes as explicit parameters. Telemetry correctness improved. PR #187. See full document below.

### 18. Pin GitHub Actions to full commit SHAs (Hicks, 2026-05-30)

**Summary:** Hardened supply-chain security by pinning 13 unique GitHub Actions across 8 workflows to immutable full-length commit SHAs while retaining human-readable version comments. Prevents tag reassignment attacks. PR #186. See full document below.

### 19. Validate agentId URI at API boundary (Parker, 2026-05-30)

**Summary:** Added `Uri.TryCreate` validation in `AgentRegistryApi.RegisterAgentAsync` and `UpdateAgentAsync` to return HTTP 400 with actionable message instead of 500 on malformed agentId. Incidental: bumped `Nerdbank.MessagePack` 1.1.62→1.2.4 to clear NU1902 CVE advisory. PR #191. See full document below.

### 20. Docker base image digest pinning (Hicks, 2026-05-30)

**Summary:** Pinned all Docker base images across 10 Dockerfiles to immutable sha256 digests (26 FROM lines total) while retaining human-readable tags. Eliminates floating-tag supply-chain risk and enables deterministic builds. Aligns with charter "pin exact versions." PR #193. See full document below.

### 21. Align mDNS instance name with Wyoming InfoEvent name (Brett, 2026-05-30)

**Summary:** Made `WyomingOptions.ServiceName` the single source of truth (default `lucia-{hostname}`), eliminating mDNS collision between `ZeroconfAdvertiser` and `WyomingServiceInfo`. Added `DescribeEvent_AsrAndWakeName_MatchServiceName` regression test. PR #192. See full document below.

### 22. Add send_message service block to services.yaml (Bishop, 2026-05-30)

**Summary:** Added `send_message` entry to `custom_components/lucia/services.yaml` with field documentation. Service was registered in Python code but had no YAML, making it undiscoverable in HA Developer Tools. PR #189. See full document below.

### 23. Surface error UI for template and optimizer fetches (Kane, 2026-05-30)

**Summary:** Added retryable inline error UI to `ResponseTemplatesPage` and `SkillOptimizerPage`. ResponseTemplatesPage shows full-page error panel and inline banner; SkillOptimizerPage shows loading skeleton and retryable error panel. Consistent with project error handling patterns. PR #190. See full document below.

### 24. Hire Vasquez as PR Review Gatekeeper + mandatory pre-push review gate (Squad, 2026-07-10)

**Summary:** Owner (Zack Way) hired a dedicated review agent, **Vasquez** (Alien universe, diegetic-expansion overflow), model-locked to **`gpt-5.6-sol`** (no fallback) via `.squad/config.json`. Established a **mandatory pre-push review gate**: no `squad/*` branch may be pushed to the remote, turned into a PR, or merged to `master` until Vasquez has reviewed the branch diff and every blocking problem is resolved. Enforced two ways — (1) **governance**: coordinator routes every `squad/*` branch to Vasquez before push/PR (see `routing.md` Pre-Push Review Gate + Rule 8, and the `Pre-Push Review Gate` ceremony); (2) **mechanical**: the version-controlled `.githooks/pre-push` hook (active via `core.hooksPath=.githooks`, installed by `scripts/install-git-hooks.sh`; docs + `Approve-Branch.ps1` in `.squad/gate/`) that blocks any push whose destination is `refs/heads/squad/*` and whose pushed SHA lacks a Vasquez approval marker in `<git-common-dir>/squad-approvals/<sha>`. The hook runs the gate and then the stock Git LFS step, so LFS is preserved. Approvals are per-commit, so any new commit invalidates approval and forces re-review. Because `core.hooksPath` is a relative path, each worktree runs its own checked-out `.githooks/pre-push`; the hook is not a single shared copy, so it covers any worktree whose checkout contains it (all future `squad/*` worktrees branch from `master`, which carries it) — it is not retroactively injected into a pre-existing worktree on a stale branch. The *approval markers* do live in the shared common git dir. Owner escape hatch: `SQUAD_GATE_BYPASS=1`. `master` and non-`squad/*` branches are not gated.


