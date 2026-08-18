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

### 2026-07-20T10:13:43.764-04:00: Health-Cache Regression Test — HTTP Behaviour Over Registration Inspection

**Situation:** Owned the independent revision of the rejected health-cache tests (Hicks/Lambert locked out). Three files flagged: a permanently-skipped Aspire Redis smoke test and two `HealthCheckCachingTests` that only asserted DI registration (`IOutputCacheStore` present) or that `AddServiceDefaults`/`MapDefaultEndpoints` wired up without throwing — none proved actual cache behaviour.

**Action:** Deleted all three (they were untracked working-tree artifacts, so no git deletion shows). Wrote one HTTP-level test at `lucia.Tests/Integration/HealthCheckCachingTests.cs` that boots a real `WebApplication` over Kestrel on `http://127.0.0.1:0`, calls production `AddServiceDefaults()` + `MapDefaultEndpoints()`, registers a counting "expensive" health check, and maps an undecorated `/uncached` GET. Two rapid `GET /health` → check executes **once** (cached inside 10s window); two `GET /uncached` → handler runs **twice** (Parker's `AddPolicy("health-checks")` is a NAMED policy applied only via `.CacheOutput("health-checks")`, not a base policy, so unrelated endpoints are not cached).

**Key findings:**
- Parker's working-tree impl changed from a global `AddBasePolicy(...Tag("health-checks"))` to a scoped `AddPolicy("health-checks", p => p.Expire(10s))`. The named-policy form is what makes claim (b) provable — a base policy would have cached `/uncached` too. Always read the live tree, not a cached view.
- No TestHost/WebApplicationFactory package is referenced; `FrameworkReference Microsoft.AspNetCore.App` is enough to run real Kestrel and hit it with `HttpClient` via `app.Urls.First()` after `StartAsync()`. `UseUrls` needs `using Microsoft.AspNetCore.Hosting;`.
- Sequential awaited requests are deterministic for cache-hit assertions; truly-concurrent requests can both miss the cache. Count executions with instance fields + `Interlocked` to honour one-class-per-file (no helper class).
- Result: targeted test passes (1/1, ~280ms); slopwatch `--no-baseline --fail-on warning` = 0 issues. Global `Slopwatch.Cmd` 0.4.2 runs as `slopwatch analyze`, not `dotnet slopwatch`.

## Archived Work
- See `history-archive.md` for prior entries (CPM adoption, transitive pinning, build health improvements)

## 2026-07-20 — HealthCheckOutputCachePolicy 503 safeguard fix (reviewer blocker)
- Blocker: policy blindly set `AllowCacheStorage = true` for ANY 503, overriding DefaultPolicy safeguards → sensitive `Set-Cookie`/authenticated 503s could be stored and replayed to other clients.
- Verified against dotnet/aspnetcore `DefaultPolicy.cs`: `ServeResponseAsync` has EXACTLY three storage guards — (1) `Set-Cookie` header present → no store, (2) `HttpContext.User.Identity.IsAuthenticated` → no store, (3) status != 200 → no store. There is NO `Vary: *` guard in the policy (nor in `OutputCacheMiddleware`), so nothing to invent/preserve there — reviewer's `Vary: *` concern is a non-issue for this API. `CacheRequestAsync` also guards GET/HEAD + Authorization header at request time.
- Minimal correct fix: only widen guard (3) to also admit 503, and re-check (1) `StringValues.IsNullOrEmpty(response.Headers.SetCookie)` + (2) not-authenticated in the same condition. DefaultPolicy runs first in the named policy (order = DefaultPolicy then appended), so for a plain healthy 200 we simply don't touch what it set. Needs `using Microsoft.Extensions.Primitives;` for `StringValues`.
- New HTTP coverage: added `Cached503WithSetCookie_IsNotStored` to the existing test class — a `/cookie-503` endpoint bound to the SAME named `health-checks` policy that returns 503 + `Set-Cookie`; both requests must re-execute the handler (count == 2), proving the cookie safeguard survives. Reused the instance-field+Interlocked pattern (one class per file). `Results.Empty` + manual `ctx.Response.StatusCode` keeps the 503 and cookie intact.
- Result: 3/3 targeted tests pass (~302ms). Slopwatch: no repo baseline exists; used global `slopwatch analyze -d <dir> --no-baseline` on both changed dirs = 0 issues (avoids creating a `.slopwatch/baseline.json` debris file). `-d` takes a DIRECTORY, not a file path.


## 2026-07-20T21:45:04-04:00 — lucia-agenthost memory: warm-up vs retained-growth (PID 74064)

**Blocker on live capture:** Target PID 74064 no longer exists; zero live `lucia.AgentHost` processes running (only 4 `dotnet.exe`, all `lucia.AppHost` dotnet-watch launchers), and Aspire AppHost MCP is not connectable ("No Aspire AppHost currently running"). AppHost log last references `lucia-agenthost` resource at 15:17; instance has since exited/stopped. **A fresh >=5-min idle series on the exact PID is impossible this session — not fabricated.**

**Instrumentation already present (provider-free path):** `lucia.ServiceDefaults/Extensions.cs` wires `AddRuntimeInstrumentation()` + `AddProcessInstrumentation()`. These are the source of every metric the requester listed (working set, gen sizes, committed, last-collection heap, allocations, exceptions, GC count, threadpool). No new dependency/harness needed to observe a leak — the counters ship with the SDK meters `System.Runtime` and `OpenTelemetry.Instrumentation.Process`.

**Analysis of supplied observations (3 points over ~11 min):**
- WS 3.74GB -> 1.815GB (startup decommit) -> 1.862GB (+47MB, +2.6% off trough)
- last-collection heap 279 -> 187 -> 222 MB (+35MB, +18.7% off trough)
- GC committed 288 -> 235 -> 275 MB (+40MB, +17% off trough)
- WS/heap/committed rise TOGETHER off the same trough = re-expansion signature (gen0 budget + live-set warm objects), not isolated managed-heap growth.
- Cumulative counters (total allocated, exceptions, GC count, work items) are monotonic by definition — only rates carry signal.
- Thread-pool size + timer count NON-monotonic -> no unbounded thread/timer accumulation (rules out the two commonest leak classes).

**Dimension correctness caveat:** "last-collection heap" is the heap after the *most recent* GC of *whatever generation last ran*. Comparing 187MB (likely post-gen2 trough) with 222MB (possibly post-gen0/1) is apples-to-oranges and CANNOT prove retention. Only a gen2-gated series proves it.

**Leak verdict:** NOT PROVEN — consistent with normal warm-up. Confidence it is benign: MODERATE-HIGH (~70%). Reasons: single up-segment off a GC trough, small absolute rises, correlated across WS/heap/committed, bounded thread/timer counts. Confidence it is a real leak: LOW.

**Metric that could actually prove it:** post-full-GC (gen2) *retained* managed heap sampled at >=3 points during idle. Zero-dependency in-proc form: `GC.GetTotalMemory(forceFullCollection: true)`. Dashboard form: GC heap size filtered to samples immediately following gen2 collections, or repeated idle-plateau floor drift. Monotonic upward post-gen2 floor across successive full GCs = leak; flat = warm-up.

**Ponytail-minimal next action (no threshold invented, no harness added):** re-measure on a live instance — `dotnet-counters monitor --process-id <pid> System.Runtime` for 10 idle min after warm-up, and induce 2-3 gen2 GCs; confirm retained heap returns to a stable floor. Only if that floor drifts up across GCs do we then add a soak assertion. Current evidence does NOT justify adding a memory/soak test (none exists in lucia.Tests today).

## 2026-07-20T22:15-04:00 — AgentHost idle-leak measurement ABORTED: target process absent (2nd consecutive session)
- Requested read-only >=5-min idle series on `lucia-agenthost-wwsvbfvc` (reported Running/Healthy). Reality at capture time: NO measurable process exists.
- Resolution sweep (all read-only): 0 `lucia.AgentHost.exe`; 0 `dotnet.exe` hosting `lucia.AgentHost`/`lucia.AppHost` dll (only 5 orphaned MSBuild worker nodes, parent PID 76948 already dead); 3 `aspire.exe` CLI launchers (49352, 72984, 53104) each with ONLY a conhost child — no AppHost spawned; Aspire AppHost MCP not connectable ("No Aspire AppHost currently running"); no dotnet/lucia TCP listeners.
- Conclusion: the dashboard "Running/Healthy" state is STALE vs. the OS process table. Cannot resolve a child PID → cannot sample WS/private/heap/GC/exception/threadpool. No baseline, no fabricated numbers.
- Per charter + task rule: did NOT start/stop/restart/rebuild anything; reported and stopped. Todo `agenthost-leak-metrics` left in_progress.
- Standing next step (unchanged from prior session, zero new tooling): the moment a live AgentHost PID exists, run `dotnet-counters monitor --process-id <pid> System.Runtime` for ~10 idle min post-warmup, induce 2-3 gen2 GCs, and confirm post-full-GC retained heap returns to a stable floor. Only an upward-drifting post-gen2 floor proves a managed leak; correlated WS/heap/committed re-expansion off one GC trough = benign warm-up.


## 2026-08-18 — Eval Unavailable-Result Invariant: Revision Assignment & Checklist

**Status:** REVISION OWNER — Assigned via Decision #32 (Ripley's retrospective)

**Situation:** Eval aggregation boundary failed to enforce unavailable-result (RunCount == 0) invariant uniformly. Score dimension was correctly nullable via Average<double?>, but latency and pass-rate dimensions missed the filter. Result: unavailable backends can appear fastest (0 ms) and all-unavailable profiles render as 0% instead of N/A — dangerous false signals.

**Your charter assignment (non-transferable):** Own the revision of aggregation logic in ProfileAggregation.cs and renderer classes (ProfileComparisonRenderer, BackendComparisonRenderer). Not locked out (Vasquez locked out as rejecting reviewer).

**Revision checklist:**
1. ProfileAggregation.AvgLatencyMs → double?; compute only over Performance.RunCount > 0 results; null when none.
2. ProfileAggregation.PassRate → double?; null when TotalTests == 0 (all-unavailable); distinct from real 0%.
3. ProfileComparisonRenderer.GroupByModelAndProfile (~line 247) → filter Performance.RunCount > 0 before .Average(latency).
4. BackendComparisonRenderer.GroupByBaseModelAndBackend (267–282) → exclude zero-run perf from Mean/Median/P95/Min/Max; if all unavailable, mark backend N/A, never fastest.
5. Ensure downstream Format*/delta/winner-selection treat null as N/A and never pick unavailable as best/fastest.

**Re-review gate (blocking your merge):**
- Build + all EvalHarness tests green.
- New/updated test: unavailable result must NOT lower a real latency average nor appear as fastest; all-unavailable must render N/A, not 0.
- Vasquez will re-review; you own the revision, Vasquez locks in approval.

**Context:** Root cause was that the contract was defined at the value level (nullable scores) but never established as a shared invariant at the aggregation boundary. Each renderer re-implements its own logic independently, so correctness depends on every consumer remembering to filter RunCount == 0 before aggregation.
