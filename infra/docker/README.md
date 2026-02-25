# Docker Deployment for Lucia Agent Host

> Last Updated: 2026-02-25  
> Status: Production Ready  
> Version: 2.0.0

## Overview

This directory contains Docker configurations for deploying Lucia Agent Host in containerized environments. The default configuration runs in **standalone mode** — all agents (Music, Timer, Orchestrator) embedded in a single process with Redis and MongoDB. No `.env` file is required; a built-in setup wizard handles all user configuration on first launch.

## Directory Structure

```
infra/docker/
├── Dockerfile                # Multi-stage Dockerfile for Lucia application
├── docker-compose.yml        # Docker Compose configuration
├── DEPLOYMENT.md             # Complete deployment guide
├── TESTING.md                # Testing guide and procedures
├── TESTING-CHECKLIST.md      # Manual testing checklist
├── verify-mvp.sh             # Automated verification script
└── README.md                 # This file
```

See also:

- `.env.example` — Environment variables reference (in project root). Only needed for Aspire dev or advanced overrides, not for Docker Compose.
- `redis:8.2-alpine` — Official Redis image used (no custom Dockerfile needed)
- `mongo:8.0` — Official MongoDB image for config, traces, and tasks storage

## Quick Start

### 1. Start Services

```bash
# Clone repository
git clone https://github.com/seiggy/lucia-dotnet.git
cd lucia-dotnet/infra/docker

# Start all services
docker compose up -d

# View logs
docker compose logs -f lucia
```

### 2. Complete Setup

Open `http://localhost:7233` in your browser. The setup wizard guides you through connecting your LLM provider and Home Assistant instance. All configuration is persisted to MongoDB.

### 3. Verify

```bash
docker compose ps
curl http://localhost:7233/health
```

## Services

### lucia (Agent Host)

- Image: Built from `Dockerfile` (multi-stage, optimized)
- Port: `0.0.0.0:7233` → `8080` (HTTP API, LAN-accessible)
- Mode: Standalone (all agents in-process) by default
- Health: `/health` endpoint
- Restart: Unless stopped
- Resources: CPU ≤ 2, Memory ≤ 1GB

### redis

- Image: `redis:8.2-alpine`
- Port: `127.0.0.1:6379` (Redis protocol)
- Health: `redis-cli PING`
- Persistence: AOF enabled, 256MB max memory
- Resources: CPU ≤ 1, Memory ≤ 512MB

### mongo

- Image: `mongo:8.0`
- Port: `127.0.0.1:27017` (MongoDB protocol)
- Health: `mongosh ping`
- Databases: `luciatraces`, `luciaconfig`, `luciatasks`
- Resources: CPU ≤ 1, Memory ≤ 512MB

### Networks

`lucia-network` — Bridge network for service-to-service communication

### Volumes

- `lucia-redis-data` — Persistent Redis data store
- `lucia-mongo-data` — Persistent MongoDB data store

## Deployment Modes

| Mode | Value | Description |
|------|-------|-------------|
| **Standalone** (default) | `standalone` | All agents embedded in AgentHost. Single container + Redis + MongoDB. |
| **Mesh** | `mesh` | Agents run as separate A2A containers. Set `Deployment__Mode=mesh` on the lucia service. |

Standalone mode is the default. External A2A agents can still connect to a standalone AgentHost. See [DEPLOYMENT.md](DEPLOYMENT.md) for details.

## Configuration

### Setup Wizard (Default)

All user-facing configuration is handled by the setup wizard on first launch at `http://localhost:7233`. Settings are stored in MongoDB and persist across restarts. No `.env` file needed.

### Environment Variable Overrides

For headless or automated deployments, add config directly to the `lucia` service in `docker-compose.yml`:

```yaml
environment:
  - ConnectionStrings__chat-model=Endpoint=https://api.openai.com/v1;AccessKey=sk-proj-YOUR_KEY;Model=gpt-4o;Provider=openai
  - HomeAssistant__BaseUrl=http://192.168.1.100:8123
  - HomeAssistant__AccessToken=eyJ...
```

### LLM Providers

#### OpenAI (Recommended)

```
ConnectionStrings__chat-model=Endpoint=https://api.openai.com/v1;AccessKey=sk-proj-YOUR_KEY;Model=gpt-4o;Provider=openai
```

#### Ollama (Local LLM)

```
ConnectionStrings__chat-model=Endpoint=http://host.docker.internal:11434;AccessKey=ollama;Model=llama3.2;Provider=ollama
```

#### Azure OpenAI (Enterprise)

```
ConnectionStrings__chat-model=Endpoint=https://YOUR_RESOURCE.openai.azure.com/;AccessKey=YOUR_KEY;Model=gpt-4-deployment;Provider=azureopenai
```

## Common Operations

### Startup

```bash
docker compose up -d          # Start all in background
docker compose up             # Start and show logs
docker compose up -d lucia    # Start specific service
```

### View Logs

```bash
docker compose logs           # All services
docker compose logs lucia     # Lucia only
docker compose logs -f lucia  # Follow real-time
docker compose logs --tail=100 lucia
```

### Health Checks

```bash
docker compose ps
curl http://localhost:7233/health
docker compose exec lucia-redis redis-cli PING
docker compose exec lucia-mongo mongosh --eval "db.runCommand('ping').ok"
```

### Restart Services

```bash
docker compose restart lucia  # Lucia only
docker compose restart        # All
docker compose down && docker compose up -d  # Full cycle
```

### Shutdown

```bash
docker compose stop           # Stop (keep volumes)
docker compose down           # Stop and remove containers
docker compose down -v        # Stop and remove volumes (destructive!)
```

### Backup/Restore

```bash
# Backup MongoDB
docker compose exec lucia-mongo mongodump --out /tmp/backup
docker cp lucia-mongo:/tmp/backup ./backup/mongo-$(date +%Y%m%d)

# Backup Redis
docker run --rm -v lucia-redis-data:/data -v ./backup:/backup \
  redis:8.2-alpine tar czf /backup/redis-$(date +%Y%m%d).tar.gz -C / data
```

## Troubleshooting

### Services won't start

```bash
docker compose logs lucia
# Common: port 7233 in use, Docker not running, insufficient memory
```

### Setup wizard not appearing

```bash
docker compose ps               # Container running?
curl -v http://localhost:7233    # Port reachable?
docker compose exec lucia-mongo mongosh --eval "db.runCommand('ping').ok"  # MongoDB healthy?
```

### Home Assistant connection fails

```bash
curl http://192.168.1.100:8123
curl -H "Authorization: Bearer YOUR_TOKEN" http://192.168.1.100:8123/api/
# Re-run setup wizard to update credentials
```

### LLM API errors

```bash
# OpenAI
curl -H "Authorization: Bearer sk-proj-YOUR_KEY" https://api.openai.com/v1/models

# Ollama
curl http://localhost:11434/api/tags
```

See [DEPLOYMENT.md](DEPLOYMENT.md) for comprehensive troubleshooting.

## Security

The `docker-compose.yml` includes security hardening by default:

- ✅ LAN-accessible AgentHost port (`0.0.0.0:7233`), localhost-only backing services
- ✅ Read-only filesystem with tmpfs mounts
- ✅ `no-new-privileges` security option
- ✅ Dropped capabilities (`NET_RAW`, `SYS_PTRACE`, `SYS_ADMIN`)
- ✅ No secrets in environment (setup wizard stores to MongoDB)

**For remote access**, use a reverse proxy (nginx, Caddy) with authentication, a VPN, or Cloudflare Tunnel. Do not bind to `0.0.0.0` without authentication.

## Next Steps

1. **Deploy** — Follow [DEPLOYMENT.md](DEPLOYMENT.md) for the full walkthrough
2. **Test** — Use [TESTING.md](TESTING.md) or [TESTING-CHECKLIST.md](TESTING-CHECKLIST.md)
3. **Verify** — Run `./verify-mvp.sh`
4. **Monitor** — Setup OpenTelemetry integration
5. **Scale** — Consider [Kubernetes](../kubernetes/README.md) for HA and mesh mode
