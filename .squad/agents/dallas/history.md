# Dallas' Work History — Package Dependency Management

## Current Role
- **Package Maintenance:** Central Package Management (CPM), cross-project dependency alignment
- **NuGet Security:** Version pinning, vulnerability audits, transitive dependency management
- **Configuration Authority:** Directory.Packages.props, version constraints, multi-target RID logic
- **Compliance:** .NET version upgrades, security advisories, breaking-change vetting

## Learnings

### 2026-07-18: Jetson Deployment — ORT Version Alignment (Final Authority)

**Responsibility:** Maintain globally consistent `Microsoft.ML.OnnxRuntime.Managed` version across all RID targets.

**Situation:** Multi-cycle review mandated **globally consistent ORT 1.23.2** (all managed + native). Jetson deployment uses from-source ORT 1.23.2 compiled for `sm_87` only (CUDA-only EP, no CPU fallback). Decision 29 gates on exact version alignment: managed 1.23.2 everywhere; native 1.23.2 from-source on device; no cross-version API gaps.

**Key finding:** Managed `InferenceSession` API surface is stable across 1.18.1–1.23.2 (backward-compat append-only design). Code binaries compiled against 1.23.2 run unchanged on 1.18.1 native (C-API v18 covered), but reverse fails (managed 1.23.2 requests API v23 → native v18 returns null → init crash). **Recommendation:** keep global 1.23.2 pin; any future production migration to newer native requires managed pin update + full test cycle.

**Ownership:** Dallas owns the `Directory.Packages.props` entry point; RID-conditional logic (if attempted in future) stays with Parker (package-file author).

**Status:** Decision 29 approved with global 1.23.2 lock. No action needed until next major ORT version available.

## Archived Work
- See `history-archive.md` for prior entries (CPM adoption, transitive pinning, build health improvements)

