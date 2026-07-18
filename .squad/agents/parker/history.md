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

**Revision (2026-07-17 — Slopwatch compliance):**
- `VersionOverride` approach rejected as SW006 CPM bypass under Slopwatch policy.
- Replaced with proper CPM: `OrtManagedVersion` MSBuild property in `Directory.Packages.props` `Version Variables` PropertyGroup; `PackageVersion` uses `$(OrtManagedVersion)`. `lucia.Wyoming.csproj` keeps a single plain versionless `PackageReference`. No per-project bypass.
- Slopwatch `--hook --no-baseline`: zero SW006 in dirty files. One SW005 (`NoWarn NU1510;EXTEXP0001` at csproj:4) confirmed pre-existing by `git diff` (not in my changeset).
- SHA256 cross-check: published `Microsoft.ML.OnnxRuntime.dll` is byte-for-byte `/microsoft.ml.onnxruntime.managed/1.18.1/` NuGet cache.
- `dotnet list package` (no RID) shows `1.23.2` — expected; property evaluates at publish time when `RuntimeIdentifier=linux-arm64` is a CLI property.


### Wyoming/ORT Package Build Seam Fix (2026-07-17 — Implementation)
Fixed linux-arm64 MSB3030 failure and aligned managed ORT with Track A production version.

**Changes made:**
- Removed `Microsoft.ML.OnnxRuntime.Gpu.Linux` `PackageReference` from `lucia.Wyoming.csproj` (was x64-only, no arm64 content; `.targets` file tried to copy win-arm64 DLL that doesn't exist)
- Removed dead `PackageVersion` entry for `Gpu.Linux` from `Directory.Packages.props` (surgical XML — CLI can't remove unreferenced DPP entry)
- Added `VersionOverride="1.18.1"` for `linux-arm64` on `Managed` ref in `lucia.Wyoming.csproj` (conditional, surgical XML — CLI can't express `VersionOverride` with `Condition`)
- `Directory.Packages.props` `Managed` remains at `1.23.2` globally (required for `DirectML 1.23.0 → Managed >= 1.23.0` compatibility)

**Key learnings:**
- `Gpu.Linux` 1.23.2 ships ONLY `runtimes/linux-x64/native/`. Its `.targets` is gated by `IsOSPlatform('Linux')` (build-host), not target RID — so on a Linux cross-compile host targeting arm64, MSBuild includes the package and copies wrong-RID files → MSB3030.
- Global `Managed 1.18.1` breaks full solution: `DirectML 1.23.0 → Managed >= 1.23.0` (NU1109) in all projects that transitively pull `DirectML` through `lucia.Wyoming`.
- `VersionOverride` on `PackageReference` is the minimal CPM escape hatch. Works per-project, doesn't affect other projects' transitive resolution.
- `Managed 1.18.1` is safe against any native ORT >= 1.18.1 (ORT C API is additive). For arm64 the sherpa-onnx 1.12.34 bundled native is ORT 1.23.2 — upward compat confirmed.
- For non-arm64 builds (empty `RuntimeIdentifier`), `'$(RuntimeIdentifier)' != 'linux-arm64'` evaluates true → DPP 1.23.2 used → DirectML stays happy.

**Validation results:**
- `dotnet restore` ✓ (no errors, no NU warnings)
- Windows full-solution build (`lucia.Tests` + all deps): **0 errors, 0 warnings** ✓
- linux-arm64 Docker SDK publish (speech-enabled, no `CpuOnly=true`): **exit 0**, `libonnxruntime.so` 29.94 MB, `Microsoft.ML.OnnxRuntime.dll` 204 KB (1.18.1), no GPU Linux `.so` ✓
- Wyoming test suite: **301 passed, 0 failed** ✓


- See `history-archive.md` for prior entries (orchestration span disposal, InputRequired timeouts, auth handler disposal, PostgreSQL index reviews, infra reviews)

### Docker/Deploy Reviewer Rejection Revision (2026-07-17 — Implementation)
Fixed 8 reviewer blockers across 4 Docker/deploy files (`Dockerfile.jetson-voice-assets`, `Dockerfile.agenthost-jetson-voice`, `docker-compose.jetson-voice.yml`, `deploy-jetson.sh`).

**Root cause discoveries (two new critical learnings):**

1. **NuGet restore base-framework-pass RID bug (task 2 regression):**
   - NuGet generates `project.assets.json` with TWO targets: `net10.0` (base) and `net10.0/linux-arm64` (RID-specific).
   - The BASE pass evaluates properties WITHOUT `$(RuntimeIdentifier)` set, even when `--runtime linux-arm64` is specified.
   - `_SpeechRuntimeTarget` falls back to `$(NETCoreSdkPortableRuntimeIdentifier)`. On a Linux AMD64 Docker SDK container (cross-compiling to arm64), this = `linux-x64`.
   - Result: `Gpu.Linux` condition evaluates TRUE in base pass → included in restore graph → MSB3030 on publish.
   - `OrtManagedVersion` property also evaluates to `1.23.2` in base pass (not 1.18.1) → managed > native = runtime init failure.
   - **Fix in Dockerfile**: pass `/p:_SpeechRuntimeTarget=linux-arm64 /p:OrtManagedVersion=1.18.1` to BOTH `dotnet restore` and `dotnet publish --no-restore`. These are explicit MSBuild property overrides, not suppressions. They override the derived property to match what it should be for the intended target platform.
   - **Proper long-term fix (package files)**: change `lucia.Wyoming.csproj` condition to use `$(RuntimeIdentifier)` directly without derived property fallback; change DPP `OrtManagedVersion` condition similarly.

2. **`Gpu.Linux` appears in `centralPackageVersions` but NOT in resolved graph:**
   - `Select-String "Gpu.Linux"` in assets.json hits the `centralPackageVersions` section (DPP declaration log), NOT the resolved `targets` or `libraries` sections.
   - This is informational/audit data — NOT an indicator the package is in the dependency graph.
   - The actual MSB3030 source: `Gpu.Linux` IS in the resolved graph on Linux SDK containers (base pass picks it up) even though `centralPackageVersions` would show it regardless.

3. **`$LD_LIBRARY_PATH` in Dockerfile ENV is undefined-var Docker lint warning:**
   - `ENV LD_LIBRARY_PATH=/app/.../native:$LD_LIBRARY_PATH` — Docker doesn't expand `$LD_LIBRARY_PATH` in the ENV instruction context; it's treated as a literal empty string → trailing colon.
   - **Fix**: use `ENV LD_LIBRARY_PATH=/app/runtimes/linux-arm64/native` (no append, fresh container has no pre-existing value).

4. **Ubuntu snapshot date coherence lesson (from Dockerfile.jetson-voice-assets):**
   - Snapshot at `20260101T000000Z` has internal inconsistency (`libcc1-0` from `jammy` requires `gcc-12-base=.2`, while `libgcc-s1` from `jammy-updates` requires `gcc-12-base=.3`).
   - Required single-RUN TLS bypass + `--allow-downgrades` to handle package version coherence.
   - Use `20260701T000000Z` (verified coherent).

5. **Docker BuildX native-assets named-context workflow:**
   - `--output type=tar` is NOT `docker load`-able as a named context.
   - Correct workflow: Step 1 `--platform linux/arm64 --load --target export -t <tag>`, Step 2 `--build-context native-assets=docker-image://<tag>`.
   - The image tag MUST be built with `--platform linux/arm64` for proper manifest for named-context resolution.

**Validation results (Lambert re-validation, 2026-07-17):**
- Native-assets image: `lucia-jetson-native-assets:v1.18.1-arm64` (ID: `54e0446cbefe`, 425 MB, all ELF64 AArch64) ✓
- `native.tar` imported as `lucia-jetson-native-assets:v1.18.1-from-tar` (ID: `86da49537e36`, 425 MB, same content) ✓
- Final voice image: `lucia-agenthost-voice:r36.4.7-test` (ID: `0a4140a07a77`, manifest `sha256:0a4140a07a7700732bcea04462ba30028cedd3c2dd9235ea095e432ff94505c2`) ✓
- Platform: `linux/arm64`, Entrypoint: `/app/entrypoint.sh`, User: `appuser`, Healthcheck: `start-period=90s` ✓
- No SDK/msbuild/csc/python3 in final image ✓
- `libonnxruntime.so` IS a symlink → `libonnxruntime.so.1.18.1` (no CPU shadow regular-file) ✓
- All native libs `755` (`rwxr-xr-x`) after chmod normalization ✓
- STT/VAD models present (parakeet, zipformer, silero) ✓
- No `onnxruntime.dll` (Windows DLL) ✓
- ORT managed DLL: 928 KB (1.18.1 R2R build) ✓
- `LD_LIBRARY_PATH=/app/runtimes/linux-arm64/native` (clean, no trailing colon, no undefined-var Docker warning) ✓
- `docker compose config` with `LUCIA_IMAGE` set: all three images resolve with digests ✓
- `bash -n deploy-jetson.sh`: exit 0 ✓
- QEMU ARM64 smoke: startup guard passed, 3 plugins loaded, no FATAL messages ✓
- Dockerfile lint: no `UndefinedVar` warning ✓

6. **CPU lib shadow (Lambert finding):**
   - `org.k2fsa.sherpa.onnx.runtime.linux-arm64` NuGet ships `libonnxruntime.so` (31 MB CPU ORT) and `libsherpa-onnx-c-api.so` (5.4 MB CPU sherpa) into `runtimes/linux-arm64/native/` from dotnet-publish.
   - Docker overlay filesystem DID correctly replace the CPU regular-file `libonnxruntime.so` with the GPU symlink from native-assets (independently verified).
   - Added defensive `rm -f libonnxruntime.so* libsherpa-onnx-c-api.so` BEFORE the GPU COPY as belt-and-suspenders.
   - Added `chmod 755` AFTER the GPU COPY to normalize permissions from archive default `600`.

7. **SONAME symlink preservation:**
   - `native.tar` confirmed: `libonnxruntime.so → libonnxruntime.so.1.18.1` is a symlink.
   - Docker `COPY --from=native-assets` preserves symlinks across build stages.
   - Verified in final image via `[ -L /app/runtimes/linux-arm64/native/libonnxruntime.so ]`.
   - GPU sherpa `DT_NEEDED: libonnxruntime.so.1.18.1` satisfied by `libonnxruntime.so.1.18.1` at same path.
- `bash -n deploy-jetson.sh` exit 0 ✓
- QEMU ARM64 smoke: startup guard passed, app booted, MongoDB crash expected (no config) ✓
- Slopwatch: SW005 `NoWarn NU1510;EXTEXP0001` pre-existing (in HEAD, not in my diff) ✓

### Jetson Deploy Validation Test — Production-Coupled Revision (2026-07-18)

Replaced the Bishop-authored disconnected test (rejected by Vasquez) with a production-coupled regression check.

**Root cause of rejection:** The original `check()` function at line 15 copied the production regex verbatim. All cases could pass even if `deploy-jetson.sh` regressed.

**Fix:** Invoke `deploy-jetson.sh --image <ref> --dry-run` through its real `do_deploy()` path with a disposable `docker` stub (PID-isolated dir, cleaned on EXIT). `--dry-run` reaches validation + `docker image inspect` + `docker compose config` and exits 0 — no preflight, no data services, no containers. Invalid refs die before any docker call; valid refs reach `exit 0`. Production regex executes; test breaks if it regresses.

**Production script:** Zero changes. `--dry-run` flag already provided the exact no-side-effect path needed.

**Validation:** `bash -n` both scripts: OK. Test: 6/6 PASS. `docker compose config` with `LUCIA_IMAGE` set: exit 0. No containers created (`docker ps -a` filter: empty). Stubs dir cleaned up.

## Next Steps
- Await coordinator approval for PoC hardware
- PoC Stages 1–5 on physical Orin Nano 8GB (~3–4 weeks)
- No managed code changes required if Hardware K-gates pass
- **Pending task 2 regression fix (package files)**: change `_SpeechRuntimeTarget` condition in `lucia.Wyoming.csproj` to not rely on `NETCoreSdkPortableRuntimeIdentifier` fallback; change `OrtManagedVersion` condition in DPP accordingly so Docker builds don't need MSBuild property overrides.
