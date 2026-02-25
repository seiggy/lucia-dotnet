# Infrastructure Deployment: Lucia AI Assistant

This directory contains all infrastructure deployment utilities and documentation for Lucia, enabling deployment across multiple platforms: Docker Compose, Kubernetes, and Linux systemd services.

## Deployment Modes

Lucia supports two deployment topologies, controlled by the `Deployment__Mode` environment variable:

| Mode | Default? | Description |
|------|----------|-------------|
| **Standalone** | ✅ Yes | All agents (Music, Timer, Orchestrator) run embedded in a single AgentHost process. Simplest setup — single container plus Redis and MongoDB. |
| **Mesh** | No | Agents run as separate A2A containers that register with the AgentHost over the network. Used for Kubernetes, horizontal scaling, or multi-node distribution. |

**Standalone** is the default for Docker Compose and systemd. **Mesh** is the default for Kubernetes (Helm/manifests). External A2A agents can connect to a standalone AgentHost — the modes are not mutually exclusive.

## Quick Navigation

Choose your deployment method below:

### Local Development Topology (AppHost-first)

For local development, start the solution through `lucia.AppHost` so supporting services and agent hosts start in a consistent composition. The AppHost always runs in **mesh mode** (separate processes) for development.

Current AppHost composition includes:

- `lucia-agenthost` (agent registry/API)
- `music-agent` (`lucia.A2AHost` with music plugin)
- `timer-agent` (`lucia.A2AHost` with timer plugin)
- `lucia-dashboard` (Vite app)
- Redis (persistent container + RedisInsight)
- MongoDB (persistent container + mongo-express)

Recommended startup from repository root:

```bash
dotnet build lucia-dotnet.slnx
dotnet run --project lucia.AppHost
```

Use direct host startup (`dotnet run --project lucia.AgentHost`) only for targeted host-only debugging.

### 🐳 [Docker Compose Deployment](./docker/README.md) — **Recommended for Most Users**

Deploy Lucia on home servers, NAS devices, or single machines using Docker Compose. All agents run in-process (standalone mode). A built-in setup wizard handles all configuration on first launch — no `.env` file required.

- **Best for**: Home automation enthusiasts, quick testing, small deployments
- **Time to deploy**: ~5 minutes
- **Complexity**: ⭐☆☆☆☆ (Very Easy)
- **Files**: [Dockerfile](./docker/Dockerfile), [docker-compose.yml](./docker/docker-compose.yml)

**Quick Start**:

```bash
cd infra/docker
docker compose up -d
# Open http://localhost:7233 — setup wizard guides you through configuration
```

---

### ☸️ [Kubernetes Deployment](./kubernetes/helm/README.md) — **For Production & High Availability**

Deploy Lucia on Kubernetes clusters using Helm charts or raw manifests. Runs in **mesh mode** by default, with Music Agent and Timer Agent as separate pods.

- **Best for**: Production deployments, high availability, auto-scaling
- **Time to deploy**: ~20 minutes
- **Complexity**: ⭐⭐⭐⭐☆ (Advanced)
- **Methods**: [Helm Chart](./kubernetes/helm/) or [Raw Manifests](./kubernetes/manifests/)

**Quick Start (Helm)**:

```bash
helm install lucia ./kubernetes/helm -f my-values.yaml
```

**Quick Start (Raw Manifests)**:

```bash
kubectl apply -k kubernetes/manifests/
```

---

### 🐧 [Linux systemd Deployment](./systemd/README.md) — **Traditional Linux Services**

Deploy Lucia as a native Linux service on Ubuntu, Debian, or RHEL using systemd.

- **Best for**: Linux servers without containerization, traditional deployments
- **Time to deploy**: ~25 minutes
- **Complexity**: ⭐⭐⭐☆☆ (Intermediate)
- **Installation**: Automated script or manual setup

**Quick Start**:

```bash
sudo ./systemd/install.sh
sudo systemctl start lucia
sudo systemctl enable lucia
```

---

### 🚀 [GitHub Actions CI/CD](../.github/workflows/docker-build-push.yml) — **Automated Publishing**

Automated Docker image building and publishing to Docker Hub.

- **Best for**: Project maintainers, automated releases
- **Time**: <10 minutes
- **Features**: Multi-platform builds (amd64, arm64), semantic versioning, automatic publishing

---

## Prerequisites Checklist

✅ **All Deployment Methods Require**:

- Home Assistant instance (version 2024.1+) running and accessible
- Home Assistant long-lived access token
- LLM provider (OpenAI API key, Azure OpenAI, or local Ollama/LM Studio)

**Method-Specific Requirements**:

- **Docker Compose**: Docker Engine 24.0+, Docker Compose v2.20+
- **Kubernetes**: kubectl, Kubernetes 1.28+, Helm 3.12+ (optional for Helm method)
- **systemd**: Linux (Ubuntu 22.04+, Debian 12+, RHEL 9+), .NET 10 runtime
- **CI/CD**: GitHub Actions (included), Docker Hub account (for image publishing)

---

## Deployment Method Comparison

| Feature | Docker | Kubernetes | systemd | CI/CD |
| ------- | ------ | ---------- | ------- | ----- |
| **Complexity** | Very Easy | Advanced | Intermediate | N/A |
| **Setup Time** | ~5 min | ~20 min | ~25 min | <10 min |
| **Best For** | Home servers | Production | Linux servers | Automation |
| **Default Mode** | Standalone | Mesh | Standalone | N/A |
| **Config Method** | Setup wizard | Helm values / env vars | env vars | N/A |
| **Scalability** | Single host | Multi-host | Single host | N/A |
| **HA/Failover** | Manual | Automatic | Manual | N/A |
| **Persistence** | Volumes | PVCs | File system | N/A |
| **Monitoring** | Health checks | Probes | journald | Logs |

---

## Configuration

### Docker Compose

Configuration is handled automatically by the **setup wizard** on first launch. Open `http://localhost:7233` and follow the prompts to connect your LLM provider and Home Assistant. Settings are stored in MongoDB.

For headless/automated deployments, pass config as environment variables in `docker-compose.yml`. See the [Docker deployment guide](./docker/DEPLOYMENT.md#advanced-environment-variable-overrides) for details.

### Kubernetes

Configuration is managed via Helm `values.yaml` or ConfigMap environment variables. See the [Kubernetes deployment guide](./kubernetes/helm/README.md).

### Environment Variable Reference

Essential variables for manual/headless configuration:

| Variable | Purpose |
|----------|---------|
| `HomeAssistant__BaseUrl` | Your Home Assistant URL |
| `HomeAssistant__AccessToken` | Long-lived Home Assistant token |
| `ConnectionStrings__chat-model` | Unified LLM connection string (format: `Endpoint=...;AccessKey=...;Model=...;Provider=openai\|azureopenai\|ollama\|azureinference`) |
| `Deployment__Mode` | `standalone` (default) or `mesh` |

**Note on Embeddings**: Embeddings for semantic search are currently supported on **Azure OpenAI** only. Support for other providers is planned.

---

## Architecture

```
lucia.AppHost (dev orchestration — mesh mode)
    ├── lucia-agenthost (registry + APIs)
    ├── music-agent (A2AHost plugin)
    ├── timer-agent (A2AHost plugin)
    ├── lucia-dashboard (UI)
    ├── redis (state persistence)
    └── mongodb (traces/config/tasks stores)

Docker Compose (production — standalone mode)
    └── lucia (AgentHost with all agents in-process)
        ├── redis
        └── mongodb

Home Assistant (custom component) ↔ agent endpoints
LLM provider(s) consumed by agent hosts
```

**Deployment Options**:

1. **Docker Compose** — Standalone AgentHost + Redis + MongoDB in containers (recommended for most users)
2. **Kubernetes** — Mesh mode with separate agent pods, Redis, MongoDB, Ingress
3. **Linux systemd** — Standalone AgentHost + Redis as systemd services
4. **Hybrid** — Application in one method, backing services in another

---

## Quick Health Check

After deploying, verify your deployment is healthy:

```bash
# Local AppHost dev stack
dotnet run --project lucia.AppHost
# Then use the Aspire dashboard to open each resource endpoint and health page

# Docker Compose
docker compose ps                     # Check container status
curl http://localhost:7233/health     # Check application health

# Kubernetes (Helm)
kubectl get pods -n lucia             # Check pod status
kubectl logs -n lucia lucia-0         # Check application logs

# systemd
sudo systemctl status lucia           # Check service status
sudo journalctl -u lucia -f           # Follow service logs
```

---

## Documentation Guide

### Getting Started

- [Docker Deployment Guide](./docker/DEPLOYMENT.md) — Complete Docker Compose walkthrough
- [Docker README](./docker/README.md) — Quick reference for Docker deployment
- [Kubernetes Deployment (Helm)](./kubernetes/helm/README.md) — Helm chart guide

### Operations & Troubleshooting

- [Docker Testing Guide](./docker/TESTING.md) — Testing procedures and debugging
- [Docker Testing Checklist](./docker/TESTING-CHECKLIST.md) — Manual test scenarios

### Utilities

- [Health Check Script](./scripts/health-check.sh) — Validate deployment health
- [Deployment Validation Script](./scripts/validate-deployment.sh) — Pre-deployment checks
- [Configuration Backup Script](./scripts/backup-config.sh) — Backup deployments

---

## Support & Documentation

- **Project Repository**: [github.com/seiggy/lucia-dotnet](https://github.com/seiggy/lucia-dotnet)
- **Issue Tracker**: [GitHub Issues](https://github.com/seiggy/lucia-dotnet/issues)
- **Home Assistant Integration**: See root [README.md](../README.md)

---

## License

All infrastructure deployment utilities are released under the same license as the Lucia project.

---

**Last Updated**: 2026-02-25  
**Status**: ✅ Production Ready
