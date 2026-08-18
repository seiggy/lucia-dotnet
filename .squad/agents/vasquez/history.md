# Vasquez — History

## Seed (2026-07-10)

- **Project:** lucia-dotnet — privacy-first multi-agent AI assistant for Home Assistant.
- **Stack:** .NET 10 / C# 14, Aspire 13, Ollama/Azure OpenAI, xUnit + FakeItEasy, TypeScript/React dashboard, Python HA custom component, Docker/K8s.
- **Owner:** Zack Way.
- **Why I exist:** Owner hired me as the team's PR Review Gatekeeper. I review every `squad/*` worktree branch before it is pushed or turned into a PR, and I hold the merge gate — nothing reaches `master` until I've reviewed the diff and all blocking problems are resolved.
- **My model is fixed to `gpt-5.6-sol`** by owner mandate. No fallback.
- **The hard gate:** the version-controlled `.githooks/pre-push` hook (active via `core.hooksPath=.githooks`, installed by `scripts/install-git-hooks.sh`) blocks any push whose destination is `refs/heads/squad/*` unless the pushed SHA has an approval marker in `<git-common-dir>/squad-approvals/<sha>`. The hook runs the gate and then the stock Git LFS step. I write that marker only after a clean review, via `.squad/gate/Approve-Branch.ps1`.

## Learnings

- Repo constitution non-negotiables to enforce at the gate: ONE class per `.cs` file; TDD/tests for public behavior; nullable reference types; file-scoped namespaces; `[LoggerMessage]` logging; OpenTelemetry retained; Conventional Commits + `Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>` trailer with `Fixes #N` footer.
- Known non-blocking baseline: ~141 eval tests fail without LLM/Ollama backends — that is pre-existing, not a branch regression.
- Merge reality in this repo: Copilot's automated review only ever leaves COMMENTED reviews (never APPROVED), and branch protection requires all conversations resolved even with `--admin`. My APPROVE is the team's human-equivalent gate before that machinery.
- **2026-08-18T10:38:51.023-04:00 — REQUEST-CHANGES, issue #137 @ `b1841ac6e788e52141df2532e27fe130e126a72f`:** Nullable judge scores and deadline/timeout tracking were merged correctly and all 138 EvalHarness tests passed, but profile and backend comparison aggregation still averages `RunCount == 0` performance as 0 ms; profile comparison also renders an all-unavailable pass rate as 0%. Unavailable provider runs can therefore appear measured, failed, or fastest. No approval recorded.

## 2026-07-18 — Jetson Deployment Review Cycles (Final Authority)

**Responsibility:** Final design gatekeeper for Jetson voice deployment artifacts and test structure. Presided over 2 full review cycles.

**Cycle 1 Findings:**
- Rejected test-decoupling failure: `test-deploy-jetson-validation.sh` copy-pasted production regex instead of invoking production script
- Locked Bishop out; recommended Parker as revision owner
- Approved immediate deployment to physical device (zackw@192.168.1.239) once revised test landed

**Cycle 2 Findings:**
- **Key mandate:** Globally consistent ORT 1.23.2 (all managed + native); no Track-A/Track-B split
- Unified Dockerfile: single-stage consolidation (removed separate builder image)
- Single-owner discipline: Ripley (design authority), Brett (native compile), Hicks (Dockerfile/Compose), Parker (validation tests)
- All package-file gates applied; RID-conditional `Gpu.Linux` confirmed
- Approved Parker's revised test (production-coupled, no regex duplication)
- Confirmed build artifacts pass syntactic validation; Compose config valid; model checksums verified (all 6 voice models)
- Approved POC deployment hand-off to on-device K1–K5 validation

**Status:** All review gates cleared. Image deployed and running on Jetson. K1 (CUDA-EP registration) confirmed in logs. Ready for on-device stress testing (K2–K5).

### 2026-07-18 — Bootstrap Validation Hardening Review

**Responsibility:** Independent review of bootstrap script and test hardening cycle (Ash).

**Findings:**
- SIGPIPE detection bug eliminated (pipe/grep replaced with direct Bash string matching)
- 105-case production-coupled test suite validates real `deploy-jetson.sh` via `--dry-run`; no regex duplication
- All 105 test cases pass; bash -n syntax clean
- Commit touches only `infra/docker/deploy-jetson.sh` and `test-deploy-jetson-validation.sh`

**Verdict:** APPROVE. Bootstrap validation is hardened and ready for deployment.

**Deployment Follow-up (Brett):**
- Dry-run: passed
- Real bootstrap: passed (exit 0)
- All 3 services healthy/running (AgentHost, Redis, PostgreSQL)
- Setup wizard accessible; Wyoming reachable

**Status:** Bootstrap gates B1–B3 complete. User must complete HA setup wizard. K1 gate deferred to next cycle.

## Archived Work
- See `history-archive.md` for prior STT semaphore, HTTPClient lifetime, and PR review entries (7 major review cycles 2026-07-10)

## 2026-07-20 — Runtime Diagnostics Working-Tree Review

- **Verdict:** REQUEST-CHANGES.
- `CacheOutput("health-checks")` selects a named policy, but `AddBasePolicy(...)` does not register that name and instead applies the default cache policy globally. The intended endpoint-only scope is therefore not enforced. The active tests only build DI/mapping and miss request behavior; the regression must issue repeated HTTP requests and prove one health-check execution inside the TTL while an unrelated endpoint is not cached.
- Keep one behavioral cache test. Delete the duplicate configuration-only suite and the permanently skipped Redis “test”; Parker's recorded live Redis Healthy/AgentHost Running proof is acceptable for the Aspire lifetime change.
- `AddProcessInstrumentation()` is available and complements runtime instrumentation for process CPU/memory. Runtime instrumentation, not process instrumentation, provides GC/allocation/thread-pool/exception metrics; team history must describe that split accurately.
- `.slopwatch/` is generated initialization debris (including a 52 KB baseline of unrelated pre-existing/worktree findings), not part of the runtime fix.
- Existing persistent Redis containers require a one-time removal before the Session-lifetime proxy path works; this migration step needs durable user-facing placement, not only a squad inbox note.


## 2026-08-18 — Eval Unavailable-Result Invariant: Re-Review Gate & Reviewer Lockout

**Status:** RE-REVIEWER (Locked out from authorship per Decision #32)

**Situation:** Previous REQUEST-CHANGES (commit b1841ac) identified nullable judge scores and deadline tracking correctly merged, but revealed aggregation boundary failure: unavailable results (RunCount == 0) incorrectly averaged in latency and pass-rate dimensions. Profile aggregation renders all-unavailable as 0% (indistinguishable from real 0%), and BackendComparisonRenderer picks unavailable backends as fastest (0 ms).

**Ripley's retrospective:** Root cause identified as value-level contract (nullable scores) without aggregation-boundary invariant enforcement. Single invariant needed: exclude RunCount==0 from ALL aggregates uniformly; aggregates become nullable; null surfaces N/A.

**Your charter assignment:** RE-REVIEW gate after Dallas completes revision.

**Re-review checklist (blocking Dallas's merge):**
- Build + all EvalHarness tests green.
- New/updated test proves: (1) unavailable result does NOT poison real latency average (does not lower it), (2) unavailable never appears as fastest, (3) all-unavailable renders N/A, not 0%.
- Commit message references Decision #32 and backlinks to b1841ac (the change you rejected).

**Reviewer lockout clarification:** You are explicitly NOT PERMITTED to author the revision code; your role is approval-gate-keeper only. Dallas owns the .NET changes and test additions. You approve or request further changes, but do not commit changes yourself.

**Context:** This was a process gap — the invariant needed to be enforced at a single choke point (aggregation boundary), not re-implemented separately by each renderer. Your REQUEST-CHANGES correctly identified the breakage; the retrospective ensures the fix is systemic and prevents future similar gaps.
