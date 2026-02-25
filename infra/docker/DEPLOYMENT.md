# Docker Deployment Guide

> Last Updated: 2026-02-25  
> Status: Production Ready for Single-Node Deployments  
> Version: 2.0.0

## Overview

This guide covers deploying Lucia Agent Host using Docker Compose for single-node deployments. The default Docker Compose configuration runs in **standalone mode** — all agents (Music, Timer, Orchestrator) are embedded in a single process. No `.env` file is required; a built-in setup wizard handles all user configuration on first launch.

**Ideal for:**

- **Home labs** — Small-scale smart home automation
- **Development** — Test locally before production
- **Testing** — CI/CD pipelines and feature validation
- **Small production** — Single Home Assistant instance with low-traffic automation

**For high-availability production, see [Kubernetes Deployment Guide](../kubernetes/README.md)**

## Prerequisites

### Required

- Docker Engine 24.0+ ([Install](https://docs.docker.com/get-docker/))
- Docker Compose v2.20+ ([Install](https://docs.docker.com/compose/install/))
- 2GB available RAM
- 10GB free disk space (for logs, Redis, and MongoDB persistence)

### Recommended

- curl or Postman for API testing
- Home Assistant 2024.10+ for best compatibility

### Optional

- Ollama for local LLM ([Install](https://ollama.ai/download)) if using local models
- Docker Desktop for GUI management
- ngrok or Cloudflare Tunnel for external access

## Quick Start (5 minutes)

### 1. Clone and Start

```bash
# Clone the repository
git clone https://github.com/seiggy/lucia-dotnet.git
cd lucia-dotnet/infra/docker

# Start all services
docker compose up -d

# View logs
docker compose logs -f lucia
```

### 2. Complete Setup Wizard

Open `http://localhost:7233` in your browser. The setup wizard will guide you through:

1. **LLM Provider** — Connect your OpenAI, Azure OpenAI, or Ollama instance
2. **Home Assistant** — Enter your HA URL and long-lived access token
3. **API Key** — Set an optional API key for securing the Lucia API

All configuration is stored in MongoDB and persists across restarts.

### 3. Verify Deployment

```bash
# List running containers
docker compose ps

# Check service health
curl http://localhost:7233/health

# Redis connectivity
docker compose exec redis redis-cli PING

# MongoDB connectivity
docker compose exec mongo mongosh --eval "db.runCommand('ping').ok"
```

### 4. Test Agent API

```bash
# List available agents
curl http://localhost:7233/api/agents

# Send test message to Lucia
curl -X POST http://localhost:7233/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "Turn on the living room lights", "sessionId": "test-123"}'
```

## Deployment Modes

Lucia supports two deployment topologies controlled by the `Deployment__Mode` environment variable:

| Mode | Value | Description |
|------|-------|-------------|
| **Standalone** (default) | `standalone` | All agents run embedded in the main AgentHost process. Simplest setup — single container plus Redis and MongoDB. |
| **Mesh** | `mesh` | Agents run as separate A2A containers that register with the AgentHost over the network. Used for Kubernetes, horizontal scaling, or multi-node distribution. |

**When to use each mode:**

- **Standalone** — Home lab, single-server, Docker Compose, or any deployment where simplicity matters. External A2A agents can still connect to a standalone AgentHost.
- **Mesh** — Kubernetes clusters, multi-node setups, or when you want to scale individual agents independently. The Helm chart and K8s manifests default to mesh mode.

To switch to mesh mode, add the environment variable to the `lucia` service in `docker-compose.yml`:

```yaml
environment:
  - Deployment__Mode=mesh
```

## Configuration

### Setup Wizard (Recommended)

All user-facing configuration — LLM provider, Home Assistant connection, API keys — is handled by the setup wizard on first launch. Settings are stored in MongoDB (`luciaconfig` database) and loaded automatically on startup.

To re-run the setup wizard, clear the config collection:

```bash
docker compose exec mongo mongosh luciaconfig --eval "db.settings.drop()"
docker compose restart lucia
```

### Environment Variables (Infrastructure Only)

The `docker-compose.yml` only contains infrastructure-level environment variables that are deterministic within the compose network:

| Variable | Purpose | Default |
|----------|---------|---------|
| `ASPNETCORE_ENVIRONMENT` | Runtime environment | `Production` |
| `ASPNETCORE_URLS` | Listen address | `http://+:8080` |
| `ConnectionStrings__luciatraces` | MongoDB traces DB | `mongodb://lucia-mongo:27017/luciatraces` |
| `ConnectionStrings__luciaconfig` | MongoDB config DB | `mongodb://lucia-mongo:27017/luciaconfig` |
| `ConnectionStrings__luciatasks` | MongoDB tasks DB | `mongodb://lucia-mongo:27017/luciatasks` |
| `ConnectionStrings__redis` | Redis connection | `lucia-redis:6379` |
| `Deployment__Mode` | Deployment topology | `standalone` (omitted = standalone) |

### Advanced: Environment Variable Overrides

For automation or headless deployments where the setup wizard is not practical, you can override any configuration via environment variables added to the `lucia` service:

```yaml
environment:
  # LLM Provider
  - ConnectionStrings__chat-model=Endpoint=https://api.openai.com/v1;AccessKey=sk-proj-YOUR_KEY;Model=gpt-4o;Provider=openai
  # Home Assistant
  - HomeAssistant__BaseUrl=http://192.168.1.100:8123
  - HomeAssistant__AccessToken=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

See `.env.example` in the repository root for all available variables.

### LLM Provider Examples

#### OpenAI (Recommended)

```
ConnectionStrings__chat-model=Endpoint=https://api.openai.com/v1;AccessKey=sk-proj-YOUR_KEY;Model=gpt-4o;Provider=openai
```

#### Ollama (Local — No Cost)

```
ConnectionStrings__chat-model=Endpoint=http://host.docker.internal:11434;AccessKey=ollama;Model=llama3.2;Provider=ollama
```

#### Azure OpenAI (Enterprise)

```
ConnectionStrings__chat-model=Endpoint=https://YOUR_RESOURCE.openai.azure.com/;AccessKey=YOUR_KEY;Model=gpt-4-deployment;Provider=azureopenai
```

### Advanced Configuration

#### Enable HTTPS (Production)

For production, use a reverse proxy (nginx, Caddy, Traefik) in front of the Lucia container rather than terminating TLS in the application itself.

#### Increase Resource Limits

Edit `docker-compose.yml` to increase resources:

```yaml
services:
  lucia:
    deploy:
      resources:
        limits:
          cpus: '4'
          memory: 2G
        reservations:
          cpus: '2'
          memory: 1G
```

## Deployment Scenarios

### Scenario 1: Development (Laptop/Desktop)

```bash
git clone https://github.com/seiggy/lucia-dotnet.git
cd lucia-dotnet/infra/docker
docker compose up          # foreground, see logs directly
# Open http://localhost:7233 and complete setup wizard
# Cleanup (removes all data)
docker compose down -v
```

**Resources:** 1GB RAM minimum · 5GB disk · 5 minutes to running

### Scenario 2: Home Lab (Always-On Server)

```bash
cd lucia-dotnet/infra/docker
docker compose up -d       # background, persistent volumes by default
docker compose logs -f lucia
docker stats               # monitor resource usage
```

**Resources:** 2GB RAM · 10GB disk · Data persists across restarts

### Scenario 3: Headless / Automated Deployment

For CI/CD or scripted deployments where a browser-based setup wizard isn't practical, pass configuration via environment variables:

```bash
cd lucia-dotnet/infra/docker
CHAT_MODEL="Endpoint=https://api.openai.com/v1;AccessKey=sk-proj-XXX;Model=gpt-4o;Provider=openai"
HA_URL="http://homeassistant.local:8123"
HA_TOKEN="eyJ..."

docker compose up -d \
  -e "ConnectionStrings__chat-model=$CHAT_MODEL" \
  -e "HomeAssistant__BaseUrl=$HA_URL" \
  -e "HomeAssistant__AccessToken=$HA_TOKEN"
```

## Operational Tasks

### View Logs

```bash
# All services
docker compose logs

# Lucia only
docker compose logs lucia

# Follow real-time
docker compose logs -f lucia

# Last 100 lines
docker compose logs --tail=100 lucia
```

### Health Checks

```bash
# Check service status
docker compose ps

# Lucia health endpoint
curl http://localhost:7233/health

# Redis connectivity
docker compose exec redis redis-cli PING

# MongoDB connectivity
docker compose exec mongo mongosh --eval "db.runCommand('ping').ok"
```

### Restart Services

```bash
# Restart Lucia (keeps Redis/MongoDB running)
docker compose restart lucia

# Restart everything
docker compose restart

# Full cycle (down/up)
docker compose down
docker compose up -d
```

### Backup and Restore

#### Backup MongoDB Data

```bash
# Dump all Lucia databases
docker compose exec mongo mongodump --out /tmp/backup
docker cp lucia-mongo:/tmp/backup ./backups/mongo-$(date +%Y%m%d)
```

#### Backup Redis Data

```bash
docker run --rm -v lucia-redis-data:/data -v ./backups:/backups \
  redis:8.2-alpine tar czf /backups/redis-$(date +%Y%m%d).tar.gz -C / data
```

#### Restore MongoDB Data

```bash
docker cp ./backups/mongo-20260225 lucia-mongo:/tmp/restore
docker compose exec mongo mongorestore /tmp/restore
```

#### Restore Redis Data

```bash
docker compose down
docker volume rm lucia-redis-data
docker run --rm -v lucia-redis-data:/data -v ./backups:/backups \
  redis:8.2-alpine tar xzf /backups/redis-20260225.tar.gz -C /
docker compose up -d
```

### Update Application

```bash
# Pull latest code
git pull origin main

# Rebuild image
docker compose build --no-cache lucia

# Restart with new image
docker compose down
docker compose up -d
```

## Troubleshooting

### Services Won't Start

```bash
# Check logs
docker compose logs lucia

# Common issues:
# - Port 7233 already in use: change the host port in docker-compose.yml
# - Docker not running: systemctl start docker
# - Insufficient memory: increase Docker memory limits
```

### Setup Wizard Not Appearing

If the setup wizard doesn't appear at `http://localhost:7233`:

```bash
# Check if the container is running
docker compose ps

# Check if the port is reachable
curl -v http://localhost:7233/health

# Check MongoDB connectivity (setup state is stored there)
docker compose exec mongo mongosh --eval "db.runCommand('ping').ok"
```

### High Memory Usage

```bash
docker stats
# Adjust resource limits in docker-compose.yml
# Restart: docker compose restart lucia
```

### Redis Persistence Issues

```bash
docker compose logs redis
docker compose exec redis redis-cli DBSIZE
# Clear Redis (destructive): docker compose exec redis redis-cli FLUSHALL
docker compose restart redis
```

### Home Assistant Connection Fails

If configured via the setup wizard, re-check your HA URL and token:

```bash
# Test HA directly
curl http://192.168.1.100:8123

# Test token
curl -H "Authorization: Bearer YOUR_TOKEN" \
  http://192.168.1.100:8123/api/
```

Re-run the setup wizard to update credentials (see [Configuration](#setup-wizard-recommended) above).

### LLM API Errors

```bash
# Test OpenAI
curl -H "Authorization: Bearer sk-proj-YOUR_KEY" \
  https://api.openai.com/v1/models

# For Ollama, verify it's running
curl http://localhost:11434/api/tags
```

## Security Considerations

### Restrict Port Access

```bash
# Default: 0.0.0.0:7233 (LAN-accessible — required for Home Assistant integration)
# Redis/MongoDB remain localhost-only (127.0.0.1)

# To restrict AgentHost to localhost only, change in docker-compose.yml:
# ports:
#   - "127.0.0.1:7233:8080"

# For internet-facing deployments, use:
# - nginx/Caddy reverse proxy with authentication
# - VPN to Docker host
# - Cloudflare Tunnel
```

### Rotate Credentials Regularly

Re-run the setup wizard or update environment variables:

```bash
# 1. Generate new Home Assistant token in HA → Profile → Long-Lived Access Tokens
# 2. Generate new LLM API key from your provider
# 3. Update via setup wizard or restart with new env vars
```

### Container Security

The docker-compose.yml includes security hardening by default:
- `read_only: true` filesystem (except tmpfs mounts)
- `no-new-privileges` security option
- Dropped `NET_RAW`, `SYS_PTRACE`, `SYS_ADMIN` capabilities
- Localhost-only port binding

## Next Steps

1. **Monitor in Production** — Set up log aggregation and alerts
2. **Add External Agents** — Connect additional A2A agents to your standalone AgentHost
3. **Upgrade to Kubernetes** — For HA, auto-scaling, and mesh mode
4. **Add Monitoring** — Integrate OpenTelemetry for tracing and metrics

See [Kubernetes Deployment Guide](../kubernetes/README.md) for HA setup.
