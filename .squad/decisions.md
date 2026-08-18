# Squad Decisions

## Active Decisions

### 31. Health Check Output Caching & Process Metrics (Hicks, 2026-07-20)

**Summary:** Aspire orchestration floods health-check requests at internal hardcoded intervals. Solution: (1) **Output Cache:** AddServiceDefaults() calls builder.Services.AddOutputCache() with named 10-second policy; MapDefaultEndpoints() includes app.UseOutputCache() middleware. Both /health and /alive endpoints tagged "health-checks". Cache duration 10s balances cost vs. responsiveness. (2) **Set-Cookie/auth safeguard:** Responses with Set-Cookie or auth tokens excluded from cache. (3) **Process Metrics:** Added .AddProcessInstrumentation() to OpenTelemetry metrics builder; exposes process.runtime.dotnet CPU/memory/GC/thread metrics. (4) **Backward compatibility:** Health endpoint contract unchanged. Test coverage: three HTTP behavioral tests pass. **Impact:** Health check executes once per 10-second window instead of per probe interval.


### 30. Redis Container Lifetime Session & AppHost Stability (Parker, 2026-07-20)

**Summary:** Root-cause diagnosis of perpetually UNHEALTHY redis health check. **Primary finding:** ContainerLifetime.Persistent broke DCP proxy endpoint allocation. Result: ConnectionStringAvailableEvent never fired → connectionString stayed null → threw InvalidOperationException on every probe. **Fix:** Changed ContainerLifetime.Persistent → ContainerLifetime.Session in lucia.AppHost/AppHost.cs. Session lifetime restores IsProxied=true → DCP allocates proxy port → event fires. Data preserved via WithDataVolume() (named volume survives restart). **Secondary:** Cleaned three stale dotnet watch process trees consuming 1.63 GB aggregate. **Transitive pins:** Locked MessagePack, Microsoft.OpenApi, SQLitePCLRaw.lib.e_sqlite3 for GHSA vulnerabilities.

