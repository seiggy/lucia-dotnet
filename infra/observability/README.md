# Remote observability

This Docker Compose deployment runs on an Ubuntu 24.04 management host. It accepts authenticated OTLP over TLS and stores telemetry away from the Jetson.

## Network and storage

Set `MANAGEMENT_BIND_ADDRESS` to a private address assigned to the lab management host or VPN. Bootstrap rejects wildcard, public, and unassigned addresses. Docker publishes no Tempo, Prometheus, Loki, Collector, or Grafana ports directly. Caddy terminates TLS for OTLP and Grafana on that address.

The default retention and storage limits are:

| Signal | Backend | Retention | Limit |
| --- | --- | --- | --- |
| Metrics | Prometheus | 30 days | 40 GB by default, set with `PROMETHEUS_RETENTION_SIZE` |
| Traces | Tempo | 14 days | Host filesystem capacity |
| Logs | Loki | 7 days | Host filesystem capacity |
| Collector queues | Collector | 5,000 requests per signal | 512 MB container memory limit and bounded persistent queues |

Tempo and Loki local storage do not enforce a byte quota. Put `/var/lib/docker` on a filesystem sized for the stated retention and alert at 70% and 85% usage. Reserve at least 80 GB beyond `PROMETHEUS_RETENTION_SIZE` for a small lab deployment, then adjust from measured ingestion volume.

## Deploy

Install Docker Engine, the Compose plugin, OpenSSL, and curl. From this directory:

```bash
cp .env.example .env
chmod 600 .env
docker run --rm caddy:2.10.2-alpine caddy hash-password --plaintext 'replace-me'
```

Put the resulting hash in `OTLP_PASSWORD_HASH`. Keep the single quotes around the hash. Add the same plaintext value as `OTLP_PASSWORD`; only the smoke test reads it. Quote passwords that contain shell metacharacters. Set the management address, hostname, and Grafana password, then deploy:

```bash
./scripts/bootstrap.sh
```

The bootstrap script creates a one-year self-signed certificate. Replace `secrets/tls.crt` and `secrets/tls.key` with a certificate from the lab CA when available. Install that CA or certificate on each telemetry client.

Clients send OTLP/gRPC to `https://<OTLP_HOSTNAME>:4317` or OTLP/HTTP to `https://<OTLP_HOSTNAME>:4318` with an HTTP Basic `Authorization` header. Open Grafana at `https://<OTLP_HOSTNAME>:3000` through the management network or VPN. If the host routes other networks to the management interface, add a host firewall rule that permits these three TCP ports only from the management or VPN CIDR.

## Verify and operate

Run `./scripts/smoke-test.sh`. It sends a fresh trace, a .NET runtime GC metric, and a log carrying that trace ID through the authenticated endpoint. The script queries each unique value through Grafana, including an exact Loki structured-metadata filter for correlation.

`docker compose ps` reports service health. The gateway `/health` endpoint reflects Collector pipeline health. The `health-monitor` service probes every live readiness endpoint and becomes unhealthy when the Docker data filesystem reaches 90%. Inspect the reason with `docker compose logs otel-collector tempo prometheus loki`; queue overflow and exporter failures remain in Collector logs and metrics instead of disappearing behind a static health response.

Back up all state during a brief maintenance window:

```bash
./scripts/backup.sh /mnt/backup/lucia-observability
```

The archive contains configs and one tarball per Compose volume. Back up `.env` and `secrets/` separately to the lab's encrypted credential store. To restore, recover those secrets first, stop the deployment, recreate it with `docker compose create`, and extract each tarball into its matching `lucia-observability_<name>` volume with the pinned `alpine:3.22.1` image. Start the deployment and rerun the smoke test.