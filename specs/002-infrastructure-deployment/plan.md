# Implementation Plan: Infrastructure Deployment Utilities and Documentation

**Branch**: `002-infrastructure-deployment` | **Date**: 2025-10-24 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-infrastructure-deployment/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Provide comprehensive infrastructure deployment utilities and documentation enabling users to deploy Lucia in three primary scenarios: Docker Compose (home servers), Kubernetes (home lab clusters), and Linux systemd services. Includes GitHub Actions CI/CD pipeline for automated Docker image publishing to Docker Hub, sample configurations for all deployment methods, and detailed documentation covering prerequisites, setup, configuration, and troubleshooting. All infrastructure files will be organized in the `/infra` directory, with README.md updated to include deployment instructions and links.

## Technical Context

**Language/Version**: 
- Infrastructure: Docker (multi-stage builds), Docker Compose v3.8+, Kubernetes 1.28+, Bash/PowerShell scripts
- Application: C# 13 / .NET 10 (existing)
- Python: 3.12+ for Home Assistant component (existing)
- CI/CD: GitHub Actions YAML workflows

**Primary Dependencies**: 
- Docker Engine 24.0+
- Docker Compose v2.20+
- Kubernetes 1.28+ with Ingress Controller (nginx-ingress or Traefik)
- Helm 3.12+ (optional for K8s deployment)
- Redis 7.x (runtime dependency)
- OpenAI-compatible LLM endpoints (runtime configuration)
- systemd (for Linux service deployment)

**Storage**: 
- Redis for task persistence (24h TTL)
- Docker volumes for persistent configuration
- Kubernetes PersistentVolumeClaims for stateful data
- File-based configuration (appsettings.json, .env files)

**Testing**: 
- Docker: `docker compose up` smoke tests
- Kubernetes: `helm test` or kubectl apply validation
- Documentation: Manual walkthrough validation
- GitHub Actions: Workflow execution in CI environment

**Target Platform**: 
- Docker: Linux containers (amd64, arm64)
- Kubernetes: Home lab clusters (k3s, k8s, microk8s)
- Linux: Ubuntu 22.04+, Debian 12+, RHEL 9+ with systemd
- CI/CD: GitHub-hosted runners (ubuntu-latest)

**Project Type**: Infrastructure + Documentation (multi-format deployment configurations)

**Performance Goals**: 
- Docker image build time: <5 minutes
- Container startup time: <30 seconds
- Kubernetes deployment rollout: <2 minutes
- GitHub Actions workflow: <10 minutes end-to-end
- Documentation clarity: 95% successful first-time deployment rate

**Constraints**: 
- Must support rootless Docker deployments
- Kubernetes configurations must work with minimal resource clusters (2 CPU, 4GB RAM minimum)
- All secrets must use secure storage mechanisms (Docker secrets, K8s secrets, systemd credentials)
- Must not require internet access for runtime operation (only for image pull)
- Documentation must be accessible to users with basic Linux/Docker knowledge

**Scale/Scope**: 
- 4 deployment methods: Docker Compose, Kubernetes (raw manifests), Kubernetes (Helm), systemd
- 1 CI/CD pipeline (GitHub Actions)
- 3+ documentation guides (Docker, Kubernetes, Linux systemd)
- 5+ configuration templates (docker-compose.yml, Dockerfile, K8s manifests, systemd service file, .env examples)
- README.md updates with deployment section and quickstart

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### I. One Class Per File ✅ N/A

**Status**: NOT APPLICABLE - This feature is infrastructure and documentation only, no C# code changes required.

**Rationale**: All infrastructure files (Dockerfiles, YAML, shell scripts, markdown documentation) do not involve C# class definitions.

---

### II. Test-First Development ⚠️ ADAPTED

**Status**: ADAPTED - Traditional TDD not applicable to infrastructure; validation testing approach used instead.

**Approach**:
- **Docker**: Smoke tests using `docker compose up` and health check verification
- **Kubernetes**: `helm test` and manifest validation using `kubectl apply --dry-run`
- **GitHub Actions**: CI workflow execution tests
- **Documentation**: Manual walkthrough validation before PR merge

**Rationale**: Infrastructure-as-code cannot be unit tested in traditional TDD fashion. Instead, we use deployment validation, smoke tests, and manual verification to ensure correctness.

---

### III. Documentation-First Research ✅ REQUIRED

**Status**: REQUIRED - Must research best practices for Docker, Kubernetes, and GitHub Actions before implementation.

**Research Topics**:
- **Docker Multi-Stage Builds**: Context7 research on .NET Docker optimization patterns
- **Kubernetes Best Practices**: Context7 research on Helm chart structure, ConfigMaps vs Secrets, ingress patterns
- **GitHub Actions**: Context7 research on Docker build/push workflows, semantic versioning, multi-platform builds
- **Redis Configuration**: Context7 research on Redis Docker deployment, persistence, and connection strings
- **systemd Service Management**: Context7 research on service file configuration, dependency ordering, restart policies

**Enforcement**: All implementation must reference official documentation from Docker, Kubernetes, GitHub Actions, and Redis.

---

### IV. Privacy-First Architecture ✅ COMPLIANT

**Status**: COMPLIANT - All deployment methods maintain local-first privacy principles.

**Privacy Measures**:
- Configuration examples use placeholders for API keys (never hardcoded)
- Documentation emphasizes local LLM options (Ollama, LM Studio)
- Secrets management via Docker secrets, Kubernetes secrets, systemd credentials
- No telemetry data sent to external services without explicit configuration
- Redis data retention set to 24h TTL (no long-term data storage)

**Rationale**: Infrastructure deployment does not compromise privacy-first architecture; all configurations support local processing.

---

### V. Observability & Telemetry ✅ ENABLED

**Status**: ENABLED - OpenTelemetry instrumentation already exists in application; infrastructure exposes it properly.

**Implementation**:
- Docker Compose includes health check endpoints
- Kubernetes includes liveness and readiness probes
- Prometheus metrics exposure enabled via existing application telemetry
- Documentation includes observability stack setup (Prometheus, Grafana, Jaeger - optional)
- Log aggregation patterns documented for Docker (stdout/stderr) and Kubernetes (cluster logging)

**Rationale**: Infrastructure does not add new telemetry but ensures existing OpenTelemetry instrumentation is accessible in deployed environments.

---

### Constitution Compliance Summary

✅ **COMPLIANT** - This feature aligns with constitutional principles:
- No C# code changes (One Class Per File N/A)
- Infrastructure testing approach adapted appropriately (TDD Adapted)
- Documentation-first research required and enforced
- Privacy-first architecture maintained in all deployment methods
- Observability properly exposed in infrastructure configurations

**Post-Phase 1 Re-Check**: Required after research.md and contracts are generated to ensure no deviations introduced during design phase.

## Project Structure

### Documentation (this feature)

```
specs/002-infrastructure-deployment/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output - N/A for infrastructure feature
├── quickstart.md        # Phase 1 output - Deployment quickstart guide
├── contracts/           # Phase 1 output - Configuration contract schemas
│   ├── docker-compose-schema.yml
│   ├── kubernetes-values-schema.yml
│   └── systemd-env-schema.md
├── checklists/          # Quality validation checklists
│   └── requirements.md  # Existing requirements checklist
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Infrastructure Files (repository root)

```
infra/
├── README.md                          # Infrastructure overview and navigation
├── docker/
│   ├── Dockerfile                     # Multi-stage .NET build + runtime
│   ├── Dockerfile.dev                 # Development variant with hot reload
│   ├── docker-compose.yml             # Production deployment with Redis
│   ├── docker-compose.dev.yml         # Development override
│   ├── .env.example                   # Environment variable template
│   └── README.md                      # Docker deployment guide
├── kubernetes/
│   ├── helm/                          # Helm chart (recommended)
│   │   ├── Chart.yaml
│   │   ├── values.yaml                # Default configuration values
│   │   ├── values.dev.yaml            # Development overrides
│   │   ├── templates/
│   │   │   ├── deployment.yaml
│   │   │   ├── service.yaml
│   │   │   ├── ingress.yaml
│   │   │   ├── configmap.yaml
│   │   │   ├── secret.yaml
│   │   │   └── redis-deployment.yaml
│   │   └── README.md                  # Helm deployment guide
│   ├── manifests/                     # Raw Kubernetes YAML (alternative)
│   │   ├── namespace.yaml
│   │   ├── deployment.yaml
│   │   ├── service.yaml
│   │   ├── ingress.yaml
│   │   ├── configmap.yaml
│   │   ├── secret.yaml
│   │   ├── redis-deployment.yaml
│   │   └── kustomization.yaml
│   └── README.md                      # Kubernetes deployment guide
├── systemd/
│   ├── lucia.service                  # systemd service unit file
│   ├── lucia.env.example              # Environment variables template
│   ├── install.sh                     # Installation script
│   └── README.md                      # systemd deployment guide
├── scripts/
│   ├── health-check.sh                # Health check utility
│   ├── backup-config.sh               # Configuration backup script
│   └── validate-deployment.sh         # Deployment validation script
└── docs/
    ├── deployment-comparison.md       # Compare Docker vs K8s vs systemd
    ├── configuration-reference.md     # Complete config reference
    ├── troubleshooting.md             # Common issues and solutions
    ├── security-hardening.md          # Security best practices
    └── llm-providers.md               # LLM endpoint configuration guide
```

### GitHub Actions Workflows

```
.github/
└── workflows/
    ├── docker-build-push.yml          # Build and publish Docker images
    ├── helm-lint.yml                  # Validate Helm chart
    └── validate-infrastructure.yml    # Validate all infra configs
```

### Updated Root Documentation

```
README.md                              # Updated with deployment section
├── [Existing sections]
└── [New] 🚀 Deployment
    ├── Quick Start (Docker Compose)
    ├── Kubernetes Deployment
    ├── Linux Systemd Service
    ├── CI/CD Pipeline
    └── Links to /infra documentation
```

**Structure Decision**: 

This feature uses a dedicated `/infra` directory structure to organize all deployment-related files separate from application code. This approach:
- Keeps infrastructure concerns isolated from application logic
- Provides clear navigation for users seeking deployment options
- Enables independent versioning and updates of infrastructure files
- Follows industry best practices (similar to Terraform, Ansible, Kubernetes projects)

The structure is organized by deployment method (docker, kubernetes, systemd) with shared utilities in `/scripts` and comprehensive documentation in `/docs`. GitHub Actions workflows live in standard `.github/workflows/` location per GitHub conventions.

README.md will be updated to add a new "Deployment" section that links to the infrastructure documentation, providing a clear entry point for users looking to deploy Lucia.

## Complexity Tracking

*Fill ONLY if Constitution Check has violations that must be justified*

**Status**: No violations - all constitutional principles are either compliant or appropriately adapted for infrastructure work.

No complexity tracking required for this feature.

---

## Phase 0: Research & Technical Discovery

### Research Tasks

Based on the Technical Context section and Documentation-First Research principle, the following research must be completed before implementation:

1. **Docker Multi-Stage Builds for .NET 10**
   - Research optimal multi-stage Dockerfile patterns for .NET 10 applications
   - Layer caching strategies for faster rebuilds
   - Security hardening (non-root user, minimal base images)
   - Multi-platform builds (amd64, arm64)

2. **Docker Compose Best Practices**
   - Service orchestration patterns for application + Redis
   - Environment variable management and .env file usage
   - Volume management for persistent configuration
   - Health checks and dependency ordering
   - Development vs production configurations

3. **Kubernetes Deployment Patterns**
   - Helm chart structure and best practices (Chart.yaml, values.yaml, templates)
   - ConfigMap vs Secret usage patterns for configuration management
   - Ingress controller configuration (nginx, Traefik)
   - Resource limits and requests for home lab deployments
   - Persistent volume claims for Redis data
   - Pod disruption budgets and rolling updates
   - Health probes (liveness, readiness, startup)

4. **GitHub Actions Docker Workflows**
   - Docker build and push action usage
   - Multi-platform builds using buildx
   - Semantic versioning strategies (tags from git tags)
   - Docker Hub authentication and secrets management
   - Caching strategies for faster builds
   - Test execution before push

5. **Redis Configuration & Deployment**
   - Redis Docker image selection (official vs Alpine)
   - Persistence configuration (RDB vs AOF)
   - Connection string formats for different deployment methods
   - Memory limits and eviction policies
   - Security (protected mode, password authentication)

6. **systemd Service Management**
   - Service unit file structure and directives
   - Dependency management (After=, Requires=)
   - Restart policies and failure handling
   - Environment file loading
   - User/group management for service isolation
   - Logging to journald

7. **OpenAI-Compatible LLM Endpoints**
   - Configuration patterns for multiple providers (OpenAI, Ollama, LM Studio, Azure OpenAI)
   - Base URL and API key configuration
   - Model selection and fallback strategies
   - Local LLM deployment considerations

### Research Outputs

All research findings will be documented in `research.md` with the following structure for each topic:

- **Decision**: What was chosen (e.g., "Use Alpine-based Redis 7.2 image")
- **Rationale**: Why it was chosen (e.g., "Smaller image size, security updates, official support")
- **Alternatives Considered**: What else was evaluated (e.g., "Official Redis image, custom build")
- **References**: Links to official documentation and Context7 queries used

**Phase 0 Status**: ✅ COMPLETE - All research topics completed and documented in [research.md](research.md)

---

## Phase 1: Design & Contracts

### Deliverables

**Phase 1 Status**: ✅ COMPLETE

#### 1. Data Model ✅

**File**: [data-model.md](data-model.md)

Defines six primary configuration entities:
- **ApplicationConfiguration**: Core app settings (Home Assistant, LLM, Redis)
- **DockerDeploymentConfiguration**: Docker-specific settings (image, ports, volumes)
- **KubernetesDeploymentConfiguration**: K8s settings (replicas, resources, ingress)
- **SystemdDeploymentConfiguration**: systemd service settings (paths, restart policy)
- **RedisConfiguration**: Redis deployment settings across all platforms
- **CICDConfiguration**: GitHub Actions pipeline settings

All entities include validation rules, relationships, and state transitions where applicable.

#### 2. Configuration Contracts ✅

**Directory**: [contracts/](contracts/)

Three configuration schema files created:

**[docker-compose-schema.yml](contracts/docker-compose-schema.yml)**
- docker-compose.yml structure and validation
- .env file format and required variables
- Validation rules for URLs, ports, tokens
- Example configurations and error messages

**[kubernetes-values-schema.yml](contracts/kubernetes-values-schema.yml)**
- Helm values.yaml complete schema
- Resource limits, ingress, secrets structure
- ConfigMap vs Secret separation
- Installation commands and examples

**[systemd-env-schema.md](contracts/systemd-env-schema.md)**
- systemd environment file format
- Required and optional variables
- File permissions and security
- Migration guides from other deployment methods

#### 3. Quickstart Guide ✅

**File**: [quickstart.md](quickstart.md)

Comprehensive deployment guide covering:
- Prerequisites checklist
- Three deployment methods with step-by-step instructions:
  - Docker Compose (~15 min)
  - Kubernetes (~20 min)
  - Linux systemd (~25 min)
- Verification steps and common commands
- Troubleshooting section
- Next steps after deployment

#### 4. Agent Context Update ✅

**Updated File**: `.github/copilot-instructions.md`

Agent context file updated to include:
- Infrastructure deployment technologies (Docker, Kubernetes, systemd)
- Configuration management patterns
- CI/CD pipeline context
- Reference to `/infra` directory structure

### Post-Phase 1 Constitution Re-Check

✅ **COMPLIANT** - No constitutional violations introduced during design phase:

- **One Class Per File**: N/A - No C# code
- **Test-First Development**: Adapted approach maintained (deployment validation)
- **Documentation-First Research**: All research completed before design
- **Privacy-First Architecture**: All configurations support local processing
- **Observability & Telemetry**: Health checks and probes included in all deployment methods

All Phase 1 artifacts align with research findings and constitutional principles.

---

## Phase 2: Implementation Tasks

**Status**: NOT STARTED - This phase is handled by `/speckit.tasks` command.

Phase 2 will generate `tasks.md` with concrete implementation steps for:

1. Creating `/infra` directory structure with all files
2. Writing Dockerfile with multi-stage builds
3. Creating docker-compose.yml and configuration templates
4. Developing Helm chart with values and templates
5. Creating Kubernetes raw manifests
6. Writing systemd service file and installation script
7. Implementing GitHub Actions CI/CD workflows
8. Writing comprehensive documentation guides
9. Updating README.md with deployment section
10. Creating utility scripts (health checks, validation, backup)

Each task will include:
- Specific file paths and contents
- Configuration examples from research
- Validation steps
- Links to relevant research/contracts

**Next Step**: Run `/speckit.tasks` to generate detailed implementation tasks.

---

## Implementation Readiness

### Prerequisites Met ✅

- [x] Feature specification complete ([spec.md](spec.md))
- [x] Constitution check passed
- [x] Research completed ([research.md](research.md))
- [x] Data model defined ([data-model.md](data-model.md))
- [x] Configuration contracts created ([contracts/](contracts/))
- [x] Quickstart guide written ([quickstart.md](quickstart.md))
- [x] Agent context updated (`.github/copilot-instructions.md`)

### Ready for Implementation ✅

All planning phases complete. Implementation can begin using the `/speckit.tasks` command to generate detailed task breakdowns.

### Key Implementation Notes

1. **File Organization**: All infrastructure files go in `/infra` directory with subdirectories per deployment method
2. **Configuration Security**: All examples must use placeholders; never commit real credentials
3. **Documentation Quality**: Every deployment method needs README with prerequisites, setup, verification, and troubleshooting
4. **Testing Strategy**: Each deployment method must be manually tested before PR
5. **README Updates**: Add deployment section with links to `/infra` documentation

### Success Criteria Reference

From [spec.md](spec.md):

- **SC-001**: Docker Compose deployment in <15 minutes ✓ Addressed in quickstart
- **SC-002**: Kubernetes deployment in <20 minutes ✓ Addressed in quickstart
- **SC-003**: systemd deployment in <25 minutes ✓ Addressed in quickstart
- **SC-004**: GitHub Actions workflow <10 minutes ✓ Addressed in research
- **SC-005**: All deployments pass health checks ✓ Addressed in contracts
- **SC-006**: <1 clarification per 10 users ✓ Addressed with comprehensive docs
- **SC-007**: 95% successful first deployment ✓ Addressed with quickstart guide
- **SC-008**: Valid configuration examples ✓ Addressed in all contract schemas

---

## Summary

**Planning Complete**: All required artifacts for the Infrastructure Deployment feature have been created.

**Artifacts Generated**:
1. ✅ [plan.md](plan.md) - This implementation plan
2. ✅ [research.md](research.md) - Technical research for all deployment methods
3. ✅ [data-model.md](data-model.md) - Configuration entities and relationships
4. ✅ [contracts/docker-compose-schema.yml](contracts/docker-compose-schema.yml) - Docker configuration contract
5. ✅ [contracts/kubernetes-values-schema.yml](contracts/kubernetes-values-schema.yml) - Kubernetes configuration contract
6. ✅ [contracts/systemd-env-schema.md](contracts/systemd-env-schema.md) - systemd configuration contract
7. ✅ [quickstart.md](quickstart.md) - User-facing deployment guide
8. ✅ Agent context updated in `.github/copilot-instructions.md`

**Branch**: `002-infrastructure-deployment`  
**Spec**: [spec.md](spec.md)  
**Requirements Checklist**: [checklists/requirements.md](checklists/requirements.md)

**Next Command**: `/speckit.tasks` to generate implementation tasks

---

**Planning Phase Complete** ✅  
Ready for task generation and implementation.

