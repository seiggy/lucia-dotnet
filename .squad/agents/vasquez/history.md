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

## Archived Work
- See `history-archive.md` for prior STT semaphore, HTTPClient lifetime, and PR review entries (7 major review cycles 2026-07-10)

