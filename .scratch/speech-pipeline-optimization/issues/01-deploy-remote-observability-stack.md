# 01: Deploy remote observability stack

**What to build:** Give operators a remote, authenticated place to ingest and investigate Lucia telemetry without spending Jetson resources on storage or analysis. The deployment must accept OTLP data, expose trace, metric, and log queries in Grafana, and remain recoverable when a backend is unavailable.

**Blocked by:** None (can start immediately).

**Status:** completed

- [x] An Ubuntu 24 Docker deployment runs OpenTelemetry Collector, Grafana, Tempo, Prometheus, and Loki with pinned versions and persistent storage.
- [x] OTLP ingestion requires authentication and TLS and is reachable only through the lab's management network or VPN.
- [x] The collector applies memory limits and batching so an overloaded backend cannot exhaust the ingestion host.
- [x] Grafana can query a known test trace, runtime metric, and correlated log sent through the collector.
- [x] Retention defaults to 30 days for metrics, 14 days for traces, and 7 days for logs, with documented storage limits and backup steps.
- [x] Health checks make collector or storage failures visible without silently discarding the reason for data loss.