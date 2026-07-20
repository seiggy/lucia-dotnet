# Decision: Runtime Diagnostics — Redis Health Check + AppHost Stability

**Date:** 2026-07-20  
**Author:** Parker  
**Status:** Resolved

---

## Problem

`redis_check` perpetually Unhealthy; AgentHost stuck in `Waiting` state indefinitely. Reported 100% CPU and ~5 GB memory on the AppHost process group.

---

## Root Cause 1 — Orphaned `dotnet watch` Process Trees

**Finding:** Three stale `dotnet watch --non-interactive --project lucia.AppHost` trees from prior developer sessions were still alive. Each orphaned DLL child consumed 435–479 MB WS. Their AuxiliaryBackchannel socket connections injected SocketException (10054) crashes into the active AppHost, causing it to abort on first startup.

**Decision:** Kill orphaned PIDs individually with `Stop-Process -Id`. No code change required. Freed ~1.63 GB WS.

**Prevention:** When stopping Aspire, use `aspire stop` (clean exit) and verify no orphaned `dotnet watch` trees remain with `Get-CimInstance Win32_Process | Where-Object CommandLine -match 'dotnet watch'`.

---

## Root Cause 2 — `ContainerLifetime.Persistent` Causes Proxyless Endpoints, Breaking Health Check

**Finding (confirmed via ILSpy decompilation of `Aspire.Hosting.dll 13.4.2`):**

`ContainerLifetime.Persistent` in the Redis builder chain caused `DcpExecutor.GetEffectiveIsProxied` to return `false`:
```
return endpoint.IsExplicitlyProxied ?? (!resource.HasPersistentLifetime())
// Persistent → HasPersistentLifetime() = true → IsProxied = false
```

Proxyless services get `Service.Spec.AddressAllocationMode = "Proxyless"` and are excluded from DCP's proxy port allocation. `DcpModelUtilities.AreResourceEndpointsAllocated` always returns `false` (no `AllocatedPort` is ever set). `DcpExecutor.PublishConnectionStringAvailableEventAsync` only fires when `AreResourceEndpointsAllocated = true` → `ConnectionStringAvailableEvent` is **never published**.

The `AddRedis()` health check factory captures `connectionString` via closure from the event subscription. Since the event never fires, `connectionString` stays `null` on every health check execution → throws `System.InvalidOperationException: Connection string is unavailable` indefinitely.

**Decision:** Change `ContainerLifetime.Persistent` → `ContainerLifetime.Session` in `lucia.AppHost/AppHost.cs`.

- `Session` lifetime → `HasPersistentLifetime() = false` → `IsProxied = true` → DCP allocates a proxy port → `AllocatedPort` is set → `AreResourceEndpointsAllocated = true` → `ConnectionStringAvailableEvent` fires → health check passes.
- Data is preserved via `WithDataVolume()` (named volume `lucia.apphost-ebb5a3a612-redis-data`) — no data loss.
- The prior TLS fix (`WithoutHttpsCertificate()` + `WithCertificateTrustScope(CertificateTrustScope.None)`, Decision #25) remains correct and needed.

**File changed:** `lucia.AppHost/AppHost.cs` line 24.

---

## Secondary Finding — Existing Persistent Container Not Recreated by DCP

When AppHost restarts with Session lifetime, DCP detects the still-running Persistent container and reuses it. The DCP proxy is allocated a new port (e.g., 52894) but the proxy cannot reach the existing container (which was not created through DCP's proxy mechanism and may not be on the Aspire container network). Proxy accepts connections but immediately closes them.

**Decision:** Manually delete the Docker container (`docker stop redis && docker rm redis`) before or after the AppHost restart. DCP then creates a fresh container from scratch through its normal proxy-aware path on AppHost restart.

---

## CPU/Memory Baseline (Post-Fix)

| Metric | Value |
|--------|-------|
| AgentHost WS | 17 MB |
| AgentHost PM | 53 MB |
| AgentHost CPU (total) | 5.92 s |
| AgentHost threads | 16 |
| dotnet-counters | Not installed; sampled via Get-Process × 3 at 10s intervals |

No 100% CPU reproduced. The reported load was entirely the orphaned watch processes.

---

## Version Note

Aspire.Hosting NuGet `13.4.2` vs Aspire CLI `13.4.6`. Not causal to this bug. Recommend aligning in a future dependency upgrade.
