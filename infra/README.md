# Infrastructure Deployment: Lucia AI Assistant

This directory contains all infrastructure deployment utilities and documentation for Lucia, enabling deployment across multiple platforms: Docker Compose, Kubernetes, and Linux systemd services.

## Quick Navigation

Choose your deployment method below:

### Local Development Topology (AppHost-first)

For local development, start the solution through `lucia.AppHost` so supporting services and agent hosts start in a consistent composition.

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

### 🐳 [Docker Compose Deployment](./docker/README.md) - **Recommended for Most Users**

Deploy Lucia on home servers, NAS devices, or single machines using Docker Compose with Redis.

- **Best for**: Home automation enthusiasts, quick testing, small deployments
- **Time to deploy**: ~15 minutes
- **Complexity**: ⭐⭐☆☆☆ (Easy)
- **Files**: [Dockerfile](./docker/Dockerfile), [docker-compose.yml](./docker/docker-compose.yml), [.env.example](./docker/.env.example)

**Quick Start**:
```bash
cd infra/docker
cp .env.example .env
# Edit .env with your configuration
docker compose up -d
```

---

### ☸️ [Kubernetes Deployment](./kubernetes/helm/README.md) - **For Production & High Availability**

Deploy Lucia on Kubernetes clusters using Helm charts or raw manifests.

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

### 🐧 [Linux systemd Deployment](./systemd/README.md) - **Traditional Linux Services**

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

### 🚀 [GitHub Actions CI/CD](../.github/workflows/docker-build-push.yml) - **Automated Publishing**

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
|---------|--------|-----------|---------|-------|
| **Complexity** | Easy | Advanced | Intermediate | N/A |
| **Setup Time** | ~15 min | ~20 min | ~25 min | <10 min |
| **Best For** | Home servers | Production | Linux servers | Automation |
| **Scalability** | Single host | Multi-host | Single host | N/A |
| **HA/Failover** | Manual | Automatic | Manual | N/A |
| **Persistence** | Volumes | PVCs | File system | N/A |
| **Monitoring** | Health checks | Probes | journald | Logs |

---

## Configuration Reference

All deployment methods require configuration via environment variables. See [docs/configuration-reference.md](./docs/configuration-reference.md) for complete documentation.

**Essential Variables**:
- `HomeAssistant__BaseUrl` - Your Home Assistant URL
- `HomeAssistant__AccessToken` - Long-lived Home Assistant token
- `ConnectionStrings__chat-model` - Unified chat model connection string (format: `Endpoint=...;AccessKey=...;Model=...;Provider=openai|azureopenai|ollama|azureinference`)
- `Redis__ConnectionString` - Redis connection (default: localhost:6379)

**Note on Embeddings**: Embeddings for semantic search are currently supported on **Azure OpenAI** only. Support for other providers is planned for future releases.

---

## Documentation Guide

### Getting Started
- [Quickstart Guide](../specs/002-infrastructure-deployment/quickstart.md) - Step-by-step for all methods
- [Configuration Reference](./docs/configuration-reference.md) - All environment variables
- [LLM Providers Guide](./docs/llm-providers.md) - Configure OpenAI, Azure, Ollama, etc.

### Deployment Methods
- [Docker Deployment Guide](./docker/README.md) - Docker Compose documentation
- [Kubernetes Deployment (Helm)](./kubernetes/helm/README.md) - Helm chart guide
- [Kubernetes Deployment (Raw Manifests)](./kubernetes/README.md) - kubectl guide
- [Linux systemd Deployment](./systemd/README.md) - Service installation guide

### Operations & Troubleshooting
- [Deployment Comparison](./docs/deployment-comparison.md) - Compare methods
- [Troubleshooting Guide](./docs/troubleshooting.md) - Common issues and solutions
- [Security Hardening](./docs/security-hardening.md) - Security best practices

### Utilities
- [Health Check Script](./scripts/health-check.sh) - Validate deployment health
- [Deployment Validation Script](./scripts/validate-deployment.sh) - Pre-deployment checks
- [Configuration Backup Script](./scripts/backup-config.sh) - Backup deployments

---

## Architecture

```
lucia.AppHost (orchestration)
    ├── lucia-agenthost (registry + APIs)
    ├── music-agent (A2AHost plugin)
    ├── timer-agent (A2AHost plugin)
    ├── lucia-dashboard (UI)
    ├── redis (state persistence)
    └── mongodb (traces/config/tasks stores)

Home Assistant (custom component) ↔ agent endpoints
LLM provider(s) consumed by agent hosts
```

**Deployment Options**:
1. **All In Docker** - Application + Redis in containers
2. **All on Kubernetes** - Application, Redis, Ingress in K8s
3. **All on Linux** - Application + Redis as systemd services
4. **Hybrid** - Application in one method, Redis in another

---

## Quick Health Check

After deploying, verify your deployment is healthy:

```bash
# Local AppHost dev stack
dotnet run --project lucia.AppHost
# Then use the Aspire dashboard to open each resource endpoint and health page

# Docker Compose
docker compose ps                    # Check container status
curl http://localhost:7235/health   # Check application health

# Kubernetes (Helm)
kubectl get pods -n lucia            # Check pod status
kubectl logs -n lucia lucia-0        # Check application logs

# systemd
sudo systemctl status lucia          # Check service status
sudo journalctl -u lucia -f          # Follow service logs
```

---

## Support & Documentation

- **Project Repository**: [github.com/seiggy/lucia-dotnet](https://github.com/seiggy/lucia-dotnet)
- **Issue Tracker**: [GitHub Issues](https://github.com/seiggy/lucia-dotnet/issues)
- **Home Assistant Integration**: See root [README.md](../README.md)

---

## License

All infrastructure deployment utilities are released under the same license as the Lucia project.

---

**Last Updated**: 2025-10-24  
**Status**: ✅ Production Ready
