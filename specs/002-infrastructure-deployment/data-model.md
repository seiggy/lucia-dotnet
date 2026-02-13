# Data Model: Infrastructure Configuration

**Feature**: Infrastructure Deployment Utilities and Documentation  
**Date**: 2025-10-24  
**Status**: Complete

This document defines the configuration entities and their relationships for Lucia deployment across different platforms.

---

## Configuration Entities

### 1. ApplicationConfiguration

Represents the complete application configuration required for Lucia to function.

**Properties:**
- `HomeAssistantBaseUrl` (string, required): Base URL of Home Assistant instance (e.g., "http://192.168.1.100:8123")
- `HomeAssistantAccessToken` (string, required, sensitive): Long-lived access token for HA API
- `OpenAIApiKey` (string, required, sensitive): API key for OpenAI or compatible provider
- `OpenAIBaseUrl` (string, optional): Base URL for OpenAI-compatible endpoint (default: "https://api.openai.com/v1")
- `OpenAIModelId` (string, required): Model identifier (e.g., "gpt-4o", "llama3.2")
- `OpenAIEmbeddingModelId` (string, required): Embedding model (e.g., "text-embedding-3-small")
- `RedisConnectionString` (string, required): Redis connection string (e.g., "localhost:6379")
- `RedisPassword` (string, optional, sensitive): Redis authentication password
- `LogLevel` (string, optional): Logging verbosity (default: "Information")

**Validation Rules:**
- URLs must be valid HTTP/HTTPS endpoints
- Tokens/keys must not be empty strings
- Model IDs must match provider's available models

**Relationships:**
- References external services: Home Assistant, LLM Provider, Redis

---

### 2. DockerDeploymentConfiguration

Represents Docker-specific deployment configuration.

**Properties:**
- `ImageName` (string): Docker image name (e.g., "seiggy/lucia")
- `ImageTag` (string): Image version tag (e.g., "latest", "v1.0.0")
- `ContainerPort` (int): Internal container port (default: 8080)
- `HostPort` (int): Mapped host port (default: 7235)
- `Volumes` (array): Volume mount definitions
- `HealthCheckPath` (string): Health check endpoint (default: "/health")
- `RestartPolicy` (string): Container restart behavior (default: "unless-stopped")

**Validation Rules:**
- Ports must be in range 1-65535
- Image name must follow Docker naming conventions
- Volume paths must be absolute or relative to compose file

**Relationships:**
- Extends ApplicationConfiguration
- References Redis container configuration

---

### 3. KubernetesDeploymentConfiguration

Represents Kubernetes-specific deployment configuration.

**Properties:**
- `Namespace` (string): K8s namespace (default: "lucia")
- `ReplicaCount` (int): Number of pod replicas (default: 1)
- `ImagePullPolicy` (string): When to pull images (default: "IfNotPresent")
- `ResourceRequests` (object): CPU/memory requests
  - `Cpu` (string): CPU request (default: "500m")
  - `Memory` (string): Memory request (default: "1Gi")
- `ResourceLimits` (object): CPU/memory limits
  - `Cpu` (string): CPU limit (default: "1000m")
  - `Memory` (string): Memory limit (default: "2Gi")
- `IngressEnabled` (bool): Enable ingress (default: true)
- `IngressHost` (string): Ingress hostname (e.g., "lucia.local.domain")
- `IngressClassName` (string): Ingress controller (e.g., "nginx", "traefik")
- `PersistentVolumeSize` (string): Redis PVC size (default: "1Gi")

**Validation Rules:**
- Namespace must be valid K8s name (lowercase, alphanumeric, hyphens)
- Resource values must be valid K8s quantity format
- Ingress host must be valid hostname

**Relationships:**
- Extends ApplicationConfiguration
- References Redis StatefulSet configuration
- References Ingress controller

---

### 4. SystemdDeploymentConfiguration

Represents Linux systemd service deployment configuration.

**Properties:**
- `ServiceName` (string): systemd service name (default: "lucia")
- `WorkingDirectory` (string): Application directory (e.g., "/opt/lucia")
- `ExecutablePath` (string): Path to dotnet executable (default: "/usr/bin/dotnet")
- `ApplicationDll` (string): Main DLL path (e.g., "/opt/lucia/lucia.AgentHost.dll")
- `EnvironmentFile` (string): Path to env file (default: "/etc/lucia/lucia.env")
- `User` (string): Run-as user (default: "DynamicUser")
- `RestartPolicy` (string): Restart behavior (default: "on-failure")
- `RestartSec` (int): Restart delay in seconds (default: 10)

**Validation Rules:**
- Paths must be absolute Linux paths
- Service name must be valid systemd unit name
- User must exist or be "DynamicUser"

**Relationships:**
- Extends ApplicationConfiguration
- Depends on Redis systemd service

---

### 5. RedisConfiguration

Represents Redis deployment configuration across all platforms.

**Properties:**
- `ImageName` (string): Redis Docker image (default: "redis:7-alpine")
- `Port` (int): Redis port (default: 6379)
- `PersistenceEnabled` (bool): Enable AOF persistence (default: true)
- `MaxMemory` (string): Memory limit (default: "256mb")
- `MaxMemoryPolicy` (string): Eviction policy (default: "allkeys-lru")
- `Password` (string, optional, sensitive): Authentication password

**Validation Rules:**
- Port must be in range 1-65535
- MaxMemory must be valid Redis memory format (e.g., "256mb", "1gb")
- MaxMemoryPolicy must be valid Redis policy

**State Transitions:**
- Initialization: Empty → Running (after startup)
- Persistence: Running → Persisting (during AOF rewrites)
- Failure: Running → Failed → Restarting (health check failure)

---

### 6. CICDConfiguration

Represents GitHub Actions CI/CD pipeline configuration.

**Properties:**
- `DockerHubUsername` (string, sensitive): Docker Hub username
- `DockerHubToken` (string, sensitive): Docker Hub access token
- `ImageRepository` (string): Docker image repository (e.g., "seiggy/lucia")
- `BuildPlatforms` (array): Target platforms (default: ["linux/amd64", "linux/arm64"])
- `TagStrategy` (string): Tagging approach (default: "semantic")
- `TestsEnabled` (bool): Run tests before push (default: true)

**Validation Rules:**
- Username and token required for push operations
- Repository must be valid Docker Hub path
- Platforms must be valid Docker platform identifiers

**Relationships:**
- Produces DockerDeploymentConfiguration artifacts
- Consumes source code and test suite

---

## Configuration Flow Diagrams

### Docker Deployment Flow

```
User
  └─> docker-compose.yml (references .env)
       ├─> lucia service (ApplicationConfiguration)
       │   └─> Dockerfile (build time)
       └─> redis service (RedisConfiguration)
```

### Kubernetes Deployment Flow

```
User
  └─> helm install (values.yaml)
       ├─> Deployment (KubernetesDeploymentConfiguration)
       │   ├─> ConfigMap (non-sensitive ApplicationConfiguration)
       │   └─> Secret (sensitive keys)
       ├─> Service (ClusterIP)
       ├─> Ingress (external access)
       └─> StatefulSet (Redis)
           └─> PersistentVolumeClaim
```

### systemd Deployment Flow

```
User
  └─> install.sh script
       ├─> Copy binaries to WorkingDirectory
       ├─> Create lucia.service (SystemdDeploymentConfiguration)
       ├─> Create lucia.env (ApplicationConfiguration)
       └─> systemctl enable lucia
```

---

## Configuration Validation

All deployment methods must validate configuration before starting services:

**Required Validations:**
1. All required properties are present
2. URLs are reachable (pre-flight check)
3. Credentials are valid (test connection)
4. Ports are available (not already bound)
5. Disk space sufficient for volumes/persistence

**Error Handling:**
- Clear error messages indicating missing/invalid configuration
- Suggestion of corrective actions (e.g., "Add HomeAssistantAccessToken to .env file")
- Graceful degradation when possible (e.g., skip optional Redis password)

---

## Summary

The data model defines six primary configuration entities organized by deployment method (Docker, Kubernetes, systemd) plus shared concerns (Application, Redis, CI/CD). All entities include validation rules and clear relationships. Configuration flows illustrate how users interact with each deployment method and how configuration propagates through the system.

**Next**: Phase 1 Contracts - Create schema files for each configuration entity
