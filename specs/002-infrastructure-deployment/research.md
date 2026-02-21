# Research: Infrastructure Deployment

**Feature**: Infrastructure Deployment Utilities and Documentation  
**Date**: 2025-10-24  
**Status**: Complete

This document contains research findings for all infrastructure deployment methods, based on official documentation and best practices.

---

## 1. Docker Multi-Stage Builds for .NET 10

### Decision

Use multi-stage Dockerfile with SDK image for build and runtime-deps image for final container, optimized for .NET 10 with layer caching and security hardening.

### Rationale

- **Multi-stage builds** drastically reduce final image size (SDK ~1GB vs runtime-deps ~200MB)
- **Layer caching** speeds up iterative builds by caching NuGet restore and build artifacts
- **Security**: Running as non-root user and using minimal runtime images reduces attack surface
- **Multi-platform support**: Using `docker buildx` enables ARM64 builds for Raspberry Pi deployments

### Implementation Pattern

```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0-rc AS build
WORKDIR /src
COPY *.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-rc AS final
WORKDIR /app
COPY --from=build /app/publish .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "lucia.AgentHost.dll"]
```

### Alternatives Considered

- **Single-stage build**: Simpler but results in 5x larger images
- **Runtime image instead of runtime-deps**: Slightly larger but includes ASP.NET Core runtime (needed)
- **Alpine-based images**: Smaller size but potential compatibility issues with .NET globalization

### References

- [Microsoft .NET Docker Documentation](https://learn.microsoft.com/en-us/dotnet/core/docker/build-container)
- [Docker Multi-Stage Build Best Practices](https://docs.docker.com/build/building/multi-stage/)
- [.NET Container Security](https://learn.microsoft.com/en-us/dotnet/core/docker/security)

---

## 2. Docker Compose Best Practices

### Decision

Use Docker Compose v3.8+ with separate service definitions for Lucia application and Redis, environment variables via .env file, health checks, and volume mounts for persistent configuration.

### Rationale

- **Service isolation**: Separate containers for app and Redis enables independent scaling and maintenance
- **.env file pattern**: Standard approach for managing configuration without hardcoding secrets
- **Health checks**: Enable Docker to monitor service health and restart failed containers
- **Named volumes**: Provide persistent storage that survives container recreation
- **Development overrides**: `docker-compose.override.yml` enables dev-specific settings without modifying base config

### Implementation Pattern

```yaml
version: '3.8'

services:
  lucia:
    image: lucia-agent:latest
    build:
      context: .
      dockerfile: infra/docker/Dockerfile
    ports:
      - "7235:8080"
    environment:
      - HomeAssistant__BaseUrl=${HA_BASE_URL}
      - HomeAssistant__AccessToken=${HA_ACCESS_TOKEN}
      - OpenAI__ApiKey=${OPENAI_API_KEY}
      - Redis__ConnectionString=redis:6379
    depends_on:
      redis:
        condition: service_healthy
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
    volumes:
      - ./config:/app/config:ro
    restart: unless-stopped

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    volumes:
      - redis-data:/data
    command: redis-server --appendonly yes --maxmemory 256mb --maxmemory-policy allkeys-lru
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 3s
      retries: 3
    restart: unless-stopped

volumes:
  redis-data:
```

### Alternatives Considered

- **Hardcoded environment variables**: Rejected due to security concerns and lack of flexibility
- **External Redis**: Rejected for quickstart; advanced users can override
- **Bridge network**: Default is sufficient; custom networks add unnecessary complexity

### References

- [Docker Compose Documentation](https://docs.docker.com/compose/)
- [Docker Compose Environment Variables](https://docs.docker.com/compose/environment-variables/)
- [Docker Compose Health Checks](https://docs.docker.com/compose/compose-file/compose-file-v3/#healthcheck)

---

## 3. Kubernetes Deployment Patterns

### Decision

Provide both Helm chart (recommended) and raw Kubernetes manifests (alternative), using ConfigMaps for non-sensitive configuration, Secrets for credentials, Ingress for external access, and StatefulSet for Redis with PVC.

### Rationale

- **Helm charts**: Industry standard for Kubernetes package management; enables templating and values-based configuration
- **Raw manifests**: Alternative for users without Helm or who prefer direct kubectl
- **ConfigMap vs Secret**: Clear separation of sensitive (API keys) vs non-sensitive (URLs, feature flags) data
- **Ingress**: Standard K8s pattern for HTTP routing; supports multiple ingress controllers
- **StatefulSet for Redis**: Ensures stable pod identity and persistent storage across restarts
- **Resource limits**: Prevents resource exhaustion on small home lab clusters

### Implementation Pattern

**Helm Chart Structure:**
```
helm/
├── Chart.yaml           # Chart metadata
├── values.yaml          # Default configuration
├── templates/
│   ├── deployment.yaml  # Lucia deployment
│   ├── service.yaml     # ClusterIP service
│   ├── ingress.yaml     # HTTP ingress
│   ├── configmap.yaml   # Non-sensitive config
│   ├── secret.yaml      # API keys and tokens
│   └── redis-statefulset.yaml
```

**Key Kubernetes Patterns:**
- **Liveness probe**: Restart pod if health check fails
- **Readiness probe**: Don't route traffic to unhealthy pods
- **Resource requests/limits**: 500m CPU, 1Gi RAM (suitable for home labs)
- **Rolling updates**: Zero-downtime deployments with RollingUpdate strategy
- **PodDisruptionBudget**: Ensure availability during cluster maintenance

### Alternatives Considered

- **Deployment instead of StatefulSet for Redis**: Rejected because Redis needs stable identity for persistence
- **LoadBalancer service**: Rejected in favor of Ingress (more flexible, works with home clusters)
- **Kustomize overlays**: Considered but Helm provides better templating for multi-environment support

### References

- [Kubernetes Best Practices](https://kubernetes.io/docs/concepts/configuration/overview/)
- [Helm Chart Best Practices](https://helm.sh/docs/chart_best_practices/)
- [Kubernetes Ingress](https://kubernetes.io/docs/concepts/services-networking/ingress/)
- [StatefulSets](https://kubernetes.io/docs/concepts/workloads/controllers/statefulset/)

---

## 4. GitHub Actions Docker Workflows

### Decision

Use GitHub Actions with official `docker/build-push-action@v5` for building multi-platform images, semantic versioning from git tags, Docker Hub push on main branch and version tags, with build caching for performance.

### Rationale

- **Official Docker actions**: Maintained by Docker team, well-documented, reliable
- **Multi-platform builds**: Supports amd64 and arm64 architectures using buildx
- **Semantic versioning**: Git tags (e.g., `v1.2.3`) automatically create Docker tags
- **Conditional push**: Build on all PRs but only push to Docker Hub from main/tags
- **Layer caching**: GitHub Actions cache dramatically speeds up rebuilds

### Implementation Pattern

```yaml
name: Build and Push Docker Image

on:
  push:
    branches: [ main ]
    tags: [ 'v*.*.*' ]
  pull_request:
    branches: [ main ]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Docker meta
        id: meta
        uses: docker/metadata-action@v5
        with:
          images: username/lucia
          tags: |
            type=ref,event=branch
            type=semver,pattern={{version}}
            type=semver,pattern={{major}}.{{minor}}
            type=sha

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Login to Docker Hub
        if: github.event_name != 'pull_request'
        uses: docker/login-action@v3
        with:
          username: ${{ secrets.DOCKERHUB_USERNAME }}
          password: ${{ secrets.DOCKERHUB_TOKEN }}

      - name: Build and push
        uses: docker/build-push-action@v5
        with:
          context: .
          file: ./infra/docker/Dockerfile
          platforms: linux/amd64,linux/arm64
          push: ${{ github.event_name != 'pull_request' }}
          tags: ${{ steps.meta.outputs.tags }}
          labels: ${{ steps.meta.outputs.labels }}
          cache-from: type=gha
          cache-to: type=gha,mode=max
```

### Alternatives Considered

- **Manual docker build commands**: Rejected in favor of official actions (better caching, multi-platform support)
- **GitHub Container Registry**: Considered but Docker Hub is more widely known for home users
- **Building on every push**: Rejected; only build on main and tags to conserve CI minutes

### References

- [Docker Build Push Action](https://github.com/docker/build-push-action)
- [Docker Metadata Action](https://github.com/docker/metadata-action)
- [GitHub Actions Docker Documentation](https://docs.github.com/en/actions/publishing-packages/publishing-docker-images)

---

## 5. Redis Configuration & Deployment

### Decision

Use official Redis 7-alpine image with AOF persistence enabled, 256MB memory limit with allkeys-lru eviction policy, and basic authentication for security.

### Rationale

- **Alpine variant**: Smaller image size (~30MB vs ~110MB) with security updates
- **AOF persistence**: Better durability than RDB for home automation state data
- **Memory limit + LRU eviction**: Prevents Redis from consuming unlimited memory on small systems
- **Authentication**: Protected mode prevents unauthorized access
- **Appendonly mode**: Ensures Lucia's task persistence survives Redis restarts

### Implementation Pattern

**Docker Compose:**
```yaml
redis:
  image: redis:7-alpine
  command: >
    redis-server
    --appendonly yes
    --maxmemory 256mb
    --maxmemory-policy allkeys-lru
    --requirepass ${REDIS_PASSWORD}
  volumes:
    - redis-data:/data
```

**Kubernetes:**
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: redis-config
data:
  redis.conf: |
    appendonly yes
    maxmemory 256mb
    maxmemory-policy allkeys-lru
```

### Alternatives Considered

- **Official redis:7 image**: Larger but includes more utilities; Alpine is sufficient
- **RDB persistence**: Faster but loses recent data on crash
- **No memory limit**: Risky on resource-constrained home servers
- **Separate Redis cluster**: Overkill for single-instance deployments

### References

- [Redis Docker Official Images](https://hub.docker.com/_/redis)
- [Redis Persistence](https://redis.io/docs/management/persistence/)
- [Redis Memory Optimization](https://redis.io/docs/management/optimization/memory-optimization/)

---

## 6. systemd Service Management

### Decision

Create systemd service unit file with Type=notify (ASP.NET Core native), After=network.target and redis.service dependencies, Restart=on-failure policy, and EnvironmentFile for configuration.

### Rationale

- **Type=notify**: ASP.NET Core sends readiness signal to systemd (proper lifecycle management)
- **Dependency ordering**: Ensures Redis starts before Lucia, network is available
- **Restart policy**: Automatically recover from crashes (home automation reliability)
- **EnvironmentFile**: Clean separation of configuration from service definition
- **DynamicUser**: Security isolation without manual user management

### Implementation Pattern

```ini
[Unit]
Description=Lucia AI Assistant
After=network.target redis.service
Requires=redis.service

[Service]
Type=notify
WorkingDirectory=/opt/lucia
ExecStart=/usr/bin/dotnet /opt/lucia/lucia.AgentHost.dll
EnvironmentFile=/etc/lucia/lucia.env
Restart=on-failure
RestartSec=10s
TimeoutStartSec=60s
TimeoutStopSec=30s

# Security
DynamicUser=yes
ProtectSystem=strict
ProtectHome=yes
NoNewPrivileges=yes
PrivateTmp=yes

# Logging
StandardOutput=journal
StandardError=journal
SyslogIdentifier=lucia

[Install]
WantedBy=multi-user.target
```

### Alternatives Considered

- **Type=simple**: Simpler but doesn't wait for ASP.NET Core readiness
- **User=lucia**: Requires manual user creation; DynamicUser is more maintainable
- **Restart=always**: Too aggressive; on-failure is more appropriate
- **No security hardening**: Less secure but hardening is best practice

### References

- [systemd Service Unit Configuration](https://www.freedesktop.org/software/systemd/man/systemd.service.html)
- [.NET systemd Integration](https://learn.microsoft.com/en-us/dotnet/core/extensions/systemd)
- [systemd Security](https://www.freedesktop.org/software/systemd/man/systemd.exec.html#Security)

---

## 7. OpenAI-Compatible LLM Endpoints

### Decision

Support multiple LLM providers via configurable base URLs and API keys, with documented examples for OpenAI, Azure OpenAI, Ollama, and LM Studio. Use environment variables for runtime configuration.

### Rationale

- **Provider flexibility**: Users can choose cloud (OpenAI, Azure) or local (Ollama, LM Studio) providers
- **Standardized interface**: OpenAI-compatible API means same configuration pattern
- **Privacy-first**: Local LLM options (Ollama) align with constitutional privacy principles
- **Environment-based config**: Easy to change providers without code modifications

### Configuration Patterns

**OpenAI:**
```env
OpenAI__ApiKey=sk-...
OpenAI__BaseUrl=https://api.openai.com/v1
OpenAI__ModelId=gpt-4o
```

**Azure OpenAI:**
```env
OpenAI__ApiKey=azure-key
OpenAI__BaseUrl=https://your-resource.openai.azure.com/
OpenAI__ModelId=gpt-4
```

**Ollama (Local):**
```env
OpenAI__ApiKey=ollama
OpenAI__BaseUrl=http://localhost:11434/v1
OpenAI__ModelId=llama3.2
```

**LM Studio (Local):**
```env
OpenAI__ApiKey=lm-studio
OpenAI__BaseUrl=http://localhost:1234/v1
OpenAI__ModelId=local-model
```

### Documentation Requirements

- Example configurations for all major providers
- Local LLM setup guides (Ollama installation, model download)
- Troubleshooting common connection issues
- Performance considerations (local vs cloud)

### References

- [OpenAI API Documentation](https://platform.openai.com/docs/api-reference)
- [Azure OpenAI Service](https://learn.microsoft.com/en-us/azure/ai-services/openai/)
- [Ollama Documentation](https://ollama.ai/docs)
- [LM Studio Documentation](https://lmstudio.ai/docs)

---

## Research Summary

All research topics have been completed with concrete decisions, rationale, and implementation patterns. Key findings:

✅ **Docker**: Multi-stage builds with security hardening  
✅ **Docker Compose**: Service orchestration with health checks and volumes  
✅ **Kubernetes**: Helm charts + raw manifests with proper resource management  
✅ **GitHub Actions**: Multi-platform builds with semantic versioning  
✅ **Redis**: Alpine image with AOF persistence and memory limits  
✅ **systemd**: Proper service lifecycle with security hardening  
✅ **LLM Providers**: Flexible configuration supporting cloud and local options

All decisions align with constitutional principles (Privacy-First, Observability, Documentation-First Research).

**Next Phase**: Phase 1 - Design & Contracts (data-model.md, contracts/, quickstart.md)
