# Tasks: Infrastructure Deployment Utilities and Documentation

**Input**: Design documents from `/specs/002-infrastructure-deployment/`  
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅

**Tests**: NOT REQUIRED for this infrastructure feature (deployment validation approach used instead per constitution)

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each deployment method.

## Format: `[ID] [P?] [Story] Description`
- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1=Docker, US2=K8s, US3=systemd, US4=CI/CD)
- Include exact file paths in descriptions

## Path Conventions
- **Infrastructure**: `/infra/docker/`, `/infra/kubernetes/`, `/infra/systemd/`
- **CI/CD**: `.github/workflows/`
- **Documentation**: `/infra/docs/`, individual method READMEs

---

## Phase 1: Setup (Shared Infrastructure) ✅

**Purpose**: Project initialization and directory structure for all deployment methods

- [x] T001 Create /infra directory structure per plan.md (docker/, kubernetes/helm/, kubernetes/manifests/, systemd/, scripts/, docs/)
- [x] T002 [P] Create infrastructure overview in /infra/README.md with navigation to all deployment methods
- [x] T003 [P] Create deployment comparison documentation in /infra/docs/deployment-comparison.md

**Checkpoint**: Directory structure ready - deployment method implementation can now begin in parallel ✅

---

## Phase 2: Foundational (Blocking Prerequisites) ✅

**Purpose**: Core documentation and shared utilities

**⚠️ CRITICAL**: No deployment method is functional without configuration validation

- [x] T004 [P] Create configuration reference in /infra/docs/configuration-reference.md documenting all variables
- [x] T005 [P] Create health check utility script in /infra/scripts/health-check.sh
- [x] T006 [P] Create deployment validation script in /infra/scripts/validate-deployment.sh

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel ✅

---

## Phase 3: User Story 1 - Docker Container Deployment (Priority: P1) 🎯 MVP ✅

**Goal**: Enable Docker Compose deployment in under 15 minutes with Redis orchestration

**Independent Test**: Run `docker compose up -d`, verify services healthy, access http://localhost:5000/health

- [x] T007 [US1] Create multi-stage Dockerfile in /infra/docker/Dockerfile.agenthost with SDK build stage and ASP.NET runtime stage
- [x] T008 [US1] Create production docker-compose.yml in /infra/docker/docker-compose.yml (root) with lucia and redis services
- [x] T009 [US1] Create environment template in .env.example with all configuration variables
- [x] T010 [US1] Create Docker deployment guide in /infra/docker/DEPLOYMENT.md with installation, configuration, troubleshooting
- [x] T011 [US1] Create Docker testing guide in /infra/docker/TESTING.md with test procedures and debugging
- [x] T012 [US1] Create manual testing checklist in /infra/docker/TESTING-CHECKLIST.md
- [x] T013 [US1] Create automated verification script in /infra/docker/verify-mvp.sh
- [x] T014 [US1] Create Docker directory README in /infra/docker/README.md with overview and operations

**Checkpoint**: User Story 1 complete - Docker Compose deployment functional and documented (MVP delivered) ✅

---

## Phase 4: User Story 2 - Kubernetes Cluster Deployment (Priority: P2) ✅

**Goal**: Enable Kubernetes deployment in under 20 minutes with Helm chart or raw manifests

**Independent Test**: Run `helm install lucia ./infra/kubernetes/helm`, verify pods running, access via ingress

### Implementation for User Story 2 (Helm Chart)

- [x] T015 [US2] Create Helm Chart.yaml in /infra/kubernetes/helm/Chart.yaml with metadata ✅
- [x] T016 [US2] Create Helm values.yaml in /infra/kubernetes/helm/values.yaml with default configuration ✅
- [x] T017 [P] [US2] Create Helm development values in /infra/kubernetes/helm/values.dev.yaml ✅
- [x] T018 [P] [US2] Create Helm deployment template in /infra/kubernetes/helm/templates/deployment.yaml ✅
- [x] T019 [P] [US2] Create Helm service template in /infra/kubernetes/helm/templates/service.yaml ✅
- [x] T020 [P] [US2] Create Helm ingress template in /infra/kubernetes/helm/templates/ingress.yaml ✅
- [x] T021 [P] [US2] Create Helm ConfigMap template in /infra/kubernetes/helm/templates/configmap.yaml ✅
- [x] T022 [P] [US2] Create Helm Secret template in /infra/kubernetes/helm/templates/secret.yaml ✅
- [x] T023 [P] [US2] Create Helm Redis StatefulSet in /infra/kubernetes/helm/templates/redis-deployment.yaml ✅
- [x] T024 [P] [US2] Create Helm helpers in /infra/kubernetes/helm/templates/_helpers.tpl ✅
- [x] T025 [P] [US2] Create Helm NOTES.txt in /infra/kubernetes/helm/templates/NOTES.txt ✅
- [x] T026 [US2] Create Helm deployment guide in /infra/kubernetes/helm/README.md ✅

### Implementation for User Story 2 (Raw Manifests Alternative)

- [x] T027 [P] [US2] Create raw Kubernetes manifests in /infra/kubernetes/manifests/ (namespace, deployment, service, ingress, configmap, secret, redis, kustomization.yaml) ✅
- [x] T028 [P] [US2] Create raw manifests guide in /infra/kubernetes/README.md ✅

### Testing for User Story 2

- [ ] T029 [US2] Manual testing: Validate Helm deployment per spec.md User Story 2 acceptance scenarios
- [ ] T030 [P] [US2] Manual testing: Validate raw manifests deployment

**Checkpoint**: User Story 2 complete - Kubernetes deployment functional via Helm and raw manifests

---

## Phase 5: User Story 3 - Linux systemd Service Deployment (Priority: P2)

**Goal**: Enable systemd deployment in under 25 minutes as native Linux service

**Independent Test**: Run install.sh, start service with `systemctl start lucia`, verify with `systemctl status lucia`

### Implementation for User Story 3

- [x] T031 [P] [US3] Create systemd service file in /infra/systemd/lucia.service with proper dependencies and restart policy
- [x] T032 [P] [US3] Create systemd environment template in /infra/systemd/lucia.env.example
- [x] T033 [US3] Create systemd installation script in /infra/systemd/install.sh with automated setup
- [x] T034 [US3] Create systemd deployment guide in /infra/systemd/README.md with installation and service management
- [ ] T035 [US3] Manual testing: Validate systemd deployment per spec.md User Story 3 acceptance scenarios

**Checkpoint**: User Story 3 complete - systemd deployment functional with installation script

---

## Phase 6: User Story 4 - CI/CD Pipeline (Priority: P3)

**Goal**: Automate Docker image building and publishing to Docker Hub in under 10 minutes

**Independent Test**: Push to main branch, verify GitHub Actions workflow builds and publishes image to Docker Hub

### Implementation for User Story 4

- [x] T036 [P] [US4] Create Docker build workflow in .github/workflows/docker-build-push.yml with multi-platform support
- [x] T037 [P] [US4] Create Helm lint workflow in .github/workflows/helm-lint.yml
- [x] T038 [P] [US4] Create infrastructure validation workflow in .github/workflows/validate-infrastructure.yml
- [ ] T039 [US4] Manual testing: Validate CI/CD workflows per spec.md User Story 4 acceptance scenarios

**Checkpoint**: User Story 4 complete - Automated CI/CD pipeline functional

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Documentation improvements and utilities that enhance all deployment methods

- [ ] T040 [P] Create troubleshooting guide in /infra/docs/troubleshooting.md covering common issues across all methods
- [ ] T041 [P] Create security hardening guide in /infra/docs/security-hardening.md
- [ ] T042 [P] Create LLM providers guide in /infra/docs/llm-providers.md with configuration examples
- [ ] T043 [P] Create configuration backup script in /infra/scripts/backup-config.sh
- [ ] T044 Update root README.md with deployment section linking to /infra documentation
- [ ] T045 Create pull request with comprehensive description and testing checklist

**Checkpoint**: All polish tasks complete - feature ready for review

---

## Task Summary

**Total Tasks**: 45 tasks organized by user story priority

**Phase Breakdown**:
- **Phase 1 - Setup** (3 tasks): Foundation and directory structure
- **Phase 2 - Foundational** (3 tasks): Shared utilities and configuration reference
- **Phase 3 - User Story 1** (8 tasks): Docker Compose deployment (MVP) 🎯
- **Phase 4 - User Story 2** (16 tasks): Kubernetes deployment (Helm + raw manifests)
- **Phase 5 - User Story 3** (5 tasks): Linux systemd service deployment
- **Phase 6 - User Story 4** (4 tasks): GitHub Actions CI/CD pipeline
- **Phase 7 - Polish** (6 tasks): Cross-cutting documentation and utilities

**Parallelizable Tasks**: 26 tasks marked with [P] can be worked on simultaneously within their phase

**MVP Scope**: Phase 1 + Phase 2 + Phase 3 (User Story 1 - Docker deployment) = 14 tasks

---

## Dependencies & Execution Order

### Story Completion Order (Priority-Based)

```
Phase 1 (Setup) → Phase 2 (Foundational) → Phase 3 (US1 - P1) → Phase 4 (US2 - P2) ∥ Phase 5 (US3 - P2) → Phase 6 (US4 - P3) → Phase 7 (Polish)
```

**Key Dependencies**:
1. Setup phase MUST complete before any user story work
2. Foundational phase provides shared utilities for all stories
3. User Stories 1, 2, 3 are INDEPENDENT after foundational phase (can parallelize)
4. User Story 4 (CI/CD) depends on Docker implementation from US1
5. Polish phase can begin once core stories are functional

### Parallel Execution Within Phases

**Phase 3 (US1 - Docker)**: T008, T010, T011, T012 can run in parallel after T007, T009 complete

**Phase 4 (US2 - Kubernetes)**: 
- Helm templates (T018-T025) can all run in parallel after T015, T016 complete
- Raw manifests (T027, T028) can run in parallel with Helm work

**Phase 5 (US3 - systemd)**: T031, T032 can run in parallel, then T033, T034

**Phase 6 (US4 - CI/CD)**: T036, T037, T038 can all run in parallel

**Phase 7 (Polish)**: T040, T041, T042, T043 can all run in parallel

---

## Independent Test Criteria

Each user story can be tested independently:

**User Story 1 (Docker)**:
- Test: `cd infra/docker && docker compose up -d`
- Verify: Services healthy, application accessible on port 7235, health endpoint responds
- Time: Complete in <15 minutes per spec.md SC-001

**User Story 2 (Kubernetes)**:
- Test: `helm install lucia ./infra/kubernetes/helm -f values.local.yaml`
- Verify: Pods running, service accessible via ingress, ConfigMap/Secret loaded
- Time: Complete in <20 minutes per spec.md SC-002

**User Story 3 (systemd)**:
- Test: `sudo ./infra/systemd/install.sh && sudo systemctl start lucia`
- Verify: Service active, logs in journald, configuration loaded from env file
- Time: Complete in <25 minutes per spec.md SC-003

**User Story 4 (CI/CD)**:
- Test: Push commit to main branch, observe GitHub Actions workflow
- Verify: Image builds, tests pass, published to Docker Hub with correct tags
- Time: Workflow completes in <10 minutes per spec.md SC-004

---

## Implementation Strategy

**MVP First (Recommended)**:
1. Complete Phase 1 (Setup) - T001 to T003
2. Complete Phase 2 (Foundational) - T004 to T006  
3. Complete Phase 3 (User Story 1 - Docker) - T007 to T014
4. **Ship MVP**: Users can now deploy via Docker Compose

**Incremental Delivery**:
- After MVP: Add Phase 4 (Kubernetes) for advanced users
- Then: Add Phase 5 (systemd) for traditional deployments
- Then: Add Phase 6 (CI/CD) for automated releases
- Finally: Phase 7 (Polish) for documentation refinement

**Parallel Development** (if multiple developers):
- After Phase 2: Developer A → User Story 1 (Docker), Developer B → User Story 2 (Kubernetes), Developer C → User Story 3 (systemd)
- Stories are fully independent and can be implemented/tested in parallel

---

## Success Criteria Mapping

Tasks directly address all success criteria from spec.md:

- **SC-001** (Docker <15 min): Phase 3 tasks T007-T014
- **SC-002** (K8s <20 min): Phase 4 tasks T015-T030
- **SC-003** (systemd <25 min): Phase 5 tasks T031-T035
- **SC-004** (CI/CD <10 min): Phase 6 tasks T036-T039
- **SC-005** (Functional deployments): All health checks in T005, validated in manual testing tasks
- **SC-006** (Documentation clarity): All README tasks and Phase 7 polish
- **SC-007** (95% success rate): Validation scripts T006, quickstart guides in all READMEs
- **SC-008** (Valid configurations): Template files T011, T017, T032 validated by T006

---

## Format Validation ✅

All 45 tasks follow the required checklist format:
- ✅ Checkbox: All tasks start with `- [ ]`
- ✅ Task ID: Sequential T001-T045
- ✅ [P] marker: 26 parallelizable tasks correctly marked
- ✅ [Story] label: All user story tasks labeled (US1, US2, US3, US4)
- ✅ File paths: All implementation tasks include exact file paths
- ✅ Descriptions: Clear actions with specific deliverables

**Ready for Implementation** ✅

---

## Detailed Task Requirements

For detailed implementation requirements, validation criteria, and references for each task, refer to:
- [plan.md](plan.md) - Technical stack and project structure
- [spec.md](spec.md) - User stories and acceptance criteria
- [research.md](research.md) - Technical decisions and best practices
- [data-model.md](data-model.md) - Configuration entities
- [contracts/](contracts/) - Configuration schemas
- [quickstart.md](quickstart.md) - Deployment workflows and testing scenarios

Each task above maps to specific sections in these documents providing complete context for implementation.

```
infra/
├── README.md (placeholder - populated later)
├── docker/
├── kubernetes/
│   ├── helm/
│   │   └── templates/
│   └── manifests/
├── systemd/
├── scripts/
└── docs/
```

**Validation**: All directories exist and are committed to git

**References**:
- [plan.md - Project Structure](plan.md#project-structure)

---

### - [ ] SETUP-002 [P] Create Infrastructure Overview Documentation

Create `/infra/README.md` with overview and navigation to all deployment methods.

**File**: `/infra/README.md`

**Content Requirements**:
- Overview of Lucia deployment options
- Quick comparison table (Docker vs K8s vs systemd)
- Links to specific deployment guides
- Prerequisites checklist (hardware, software)
- Decision tree for choosing deployment method

**Validation**: README provides clear navigation to all deployment options

**References**:
- [quickstart.md](quickstart.md) - Prerequisites and decision guidance
- [plan.md - Project Structure](plan.md#project-structure)

---

### - [ ] SETUP-003 [P] Create Deployment Comparison Documentation

Create `/infra/docs/deployment-comparison.md` comparing all deployment methods.

**File**: `/infra/docs/deployment-comparison.md`

**Content Requirements**:
- Detailed comparison matrix (complexity, resources, scalability, maintenance)
- Use cases for each method
- Migration paths between methods
- Performance characteristics
- Security considerations

**Validation**: Document helps users choose appropriate deployment method

**References**:
- [research.md](research.md) - All deployment method research
- [quickstart.md - Choose Your Deployment Method](quickstart.md#choose-your-deployment-method)

---

## P1 (User Story 1): Docker Compose Deployment

**Goal**: Enable Docker Compose deployment in <15 minutes (SC-001)  
**Priority**: P1 (MVP - highest priority)

### - [ ] P1-001 Create Multi-Stage Dockerfile

Create production-ready Dockerfile with optimized layer caching.

**File**: `/infra/docker/Dockerfile`

**Requirements**:
- Multi-stage build (SDK stage + runtime stage)
- Use `mcr.microsoft.com/dotnet/sdk:10.0-rc` for build stage
- Use `mcr.microsoft.com/dotnet/aspnet:10.0-rc` for runtime stage
- Copy solution file and restore before copying source (layer caching)
- Build `lucia.AgentHost` project specifically
- Non-root user in runtime stage
- EXPOSE port 8080
- Health check support via /health endpoint

**Validation**:
- Build succeeds: `docker build -f infra/docker/Dockerfile -t lucia:test .`
- Image runs: `docker run -p 8080:8080 lucia:test`
- Health endpoint responds: `curl http://localhost:8080/health`
- Image size <500MB

**References**:
- [research.md - Docker Multi-Stage Builds](research.md#1-docker-multi-stage-builds-for-net-10)
- [data-model.md - DockerDeploymentConfiguration](data-model.md#2-dockerdeploymentconfiguration)

---

### - [ ] P1-002 Create Development Dockerfile

Create development variant with hot reload support.

**File**: `/infra/docker/Dockerfile.dev`

**Requirements**:
- Single-stage build using SDK image
- Volume mount support for source code
- dotnet watch run configuration
- Port 8080 exposed
- Optimized for development workflow (faster rebuilds)

**Validation**:
- Build succeeds
- Hot reload works when source files change

**References**:
- [research.md - Docker Multi-Stage Builds](research.md#1-docker-multi-stage-builds-for-net-10)

---

### - [ ] P1-003 Create Production docker-compose.yml

Create production Docker Compose configuration with Redis orchestration.

**File**: `/infra/docker/docker-compose.yml`

**Requirements** (per [docker-compose-schema.yml](contracts/docker-compose-schema.yml)):
- Version 3.8+
- `lucia` service:
  - Build from `Dockerfile`
  - Ports: `7235:8080`
  - Environment variables from `.env` file
  - Depends on Redis with `service_healthy` condition
  - Health check with curl to `/health`
  - Restart policy: `unless-stopped`
  - Volume mount for config directory (read-only)
- `redis` service:
  - Image: `redis:7-alpine`
  - Health check with `redis-cli ping`
  - Volume for persistent data
  - Memory limit: 256MB
  - Command: `redis-server --maxmemory 256mb --maxmemory-policy allkeys-lru`

**Validation**:
- Compose up succeeds: `docker compose -f infra/docker/docker-compose.yml up -d`
- Services are healthy
- Lucia connects to Redis
- Configuration from .env loads correctly

**References**:
- [contracts/docker-compose-schema.yml](contracts/docker-compose-schema.yml)
- [research.md - Docker Compose Best Practices](research.md#2-docker-compose-best-practices)
- [data-model.md - ApplicationConfiguration](data-model.md#1-applicationconfiguration)

---

### - [ ] P1-004 [P] Create Development docker-compose Override

Create development overrides for local development workflow.

**File**: `/infra/docker/docker-compose.dev.yml`

**Requirements**:
- Override `lucia` service to use `Dockerfile.dev`
- Volume mount source code for hot reload
- Additional ports if needed (debugging)
- Development-friendly logging (verbose)
- Optional: mount local LLM endpoints

**Validation**:
- Development compose up succeeds
- Hot reload works for code changes
- Can debug from IDE

**References**:
- [research.md - Docker Compose Best Practices](research.md#2-docker-compose-best-practices)

---

### - [ ] P1-005 Create .env.example Template

Create example environment file with all configuration variables.

**File**: `/infra/docker/.env.example`

**Requirements** (per [docker-compose-schema.yml](contracts/docker-compose-schema.yml)):
- All required variables with placeholder values:
  - `HomeAssistant__BaseUrl=http://YOUR_HA_IP:8123`
  - `HomeAssistant__AccessToken=YOUR_LONG_LIVED_TOKEN`
  - `OpenAI__ApiKey=sk-proj-YOUR_KEY_HERE`
  - `OpenAI__BaseUrl=https://api.openai.com/v1`
  - `OpenAI__ModelId=gpt-4o`
  - `OpenAI__EmbeddingModelId=text-embedding-3-small`
  - `Redis__ConnectionString=redis:6379`
  - `Redis__Password=` (optional)
  - `Logging__LogLevel__Default=Information`
- Comments explaining each variable
- Examples for different LLM providers (OpenAI, Ollama, Azure)
- Security warnings for sensitive values

**Validation**:
- All required variables present
- Comments are clear and helpful
- Examples cover common scenarios

**References**:
- [contracts/docker-compose-schema.yml - .env File Schema](contracts/docker-compose-schema.yml)
- [data-model.md - ApplicationConfiguration](data-model.md#1-applicationconfiguration)

---

### - [ ] P1-006 Create Docker Deployment Documentation

Create comprehensive Docker deployment guide.

**File**: `/infra/docker/README.md`

**Content Requirements** (per FR-005):
- Prerequisites (Docker version, resources)
- Quick start (5-step process)
- Detailed installation instructions
- Configuration guide (environment variables)
- Network setup (port mapping, bridge networks)
- Volume management (persistent config, Redis data)
- Health checks and monitoring
- Common deployment scenarios (local LLM, remote Redis)
- Troubleshooting section (common errors)
- Update/upgrade procedures
- Security best practices

**Validation**:
- New user can deploy in <15 minutes following guide
- All configuration options documented
- Troubleshooting covers edge cases

**References**:
- [quickstart.md - Docker Compose Deployment](quickstart.md#docker-compose-deployment)
- [spec.md - User Story 1 Acceptance Scenarios](spec.md#user-story-1---docker-container-deployment-priority-p1)
- [research.md - Docker Compose Best Practices](research.md#2-docker-compose-best-practices)

---

### - [ ] P1-007 [P] Create .dockerignore File

Create `.dockerignore` to optimize Docker build context.

**File**: `/infra/docker/.dockerignore`

**Requirements**:
- Exclude build artifacts (bin/, obj/)
- Exclude IDE files (.vs/, .vscode/)
- Exclude documentation (.docs/, specs/)
- Exclude git (.git/)
- Exclude test files (*.Tests/)
- Include only necessary files for build

**Validation**:
- Docker build context size significantly reduced
- Build time improves

**References**:
- [research.md - Docker Multi-Stage Builds](research.md#1-docker-multi-stage-builds-for-net-10)

---

### - [ ] P1-008 Manual Testing: Docker Deployment

Manually test complete Docker deployment workflow.

**Test Scenarios**:
1. Fresh deployment following README
2. Configuration changes and container restart
3. Redis persistence (stop/start)
4. Health check validation
5. Log output verification
6. Resource consumption monitoring

**Acceptance Criteria** (per spec.md):
- [ ] Services start successfully with `docker compose up`
- [ ] Application accessible on configured port
- [ ] Connects to OpenAI-compatible endpoints
- [ ] Redis state persists across restarts
- [ ] Deployment completed in <15 minutes

**Validation**: All acceptance scenarios from User Story 1 pass

**References**:
- [spec.md - User Story 1 Acceptance Scenarios](spec.md#user-story-1---docker-container-deployment-priority-p1)

---

## P2 (User Story 2): Kubernetes Deployment

**Goal**: Enable Kubernetes deployment in <20 minutes (SC-002)  
**Priority**: P2 (Second priority)

### - [ ] P2-001 Create Helm Chart.yaml

Create Helm chart metadata.

**File**: `/infra/kubernetes/helm/Chart.yaml`

**Requirements**:
- apiVersion: v2
- name: lucia
- version: 1.0.0
- appVersion: 1.0.0
- description: Lucia AI-powered Home Assistant agent orchestration
- type: application
- keywords: [homeassistant, ai, agents, llm]
- maintainers list
- home URL (GitHub repo)

**Validation**:
- Valid Chart.yaml: `helm lint infra/kubernetes/helm`

**References**:
- [research.md - Kubernetes Deployment Patterns](research.md#3-kubernetes-deployment-patterns)
- [contracts/kubernetes-values-schema.yml](contracts/kubernetes-values-schema.yml)

---

### - [ ] P2-002 Create Helm values.yaml

Create default Helm values configuration.

**File**: `/infra/kubernetes/helm/values.yaml`

**Requirements** (per [kubernetes-values-schema.yml](contracts/kubernetes-values-schema.yml)):
- `replicaCount: 1`
- `image`:
  - `repository: seiggy/lucia`
  - `pullPolicy: IfNotPresent`
  - `tag: latest`
- `service`:
  - `type: ClusterIP`
  - `port: 80`
  - `targetPort: 8080`
- `ingress`:
  - `enabled: true`
  - `className: nginx`
  - `hosts` with default hostname
- `resources`:
  - `requests`: 500m CPU, 1Gi memory
  - `limits`: 1000m CPU, 2Gi memory
- `redis`:
  - `enabled: true`
  - `persistence`: 1Gi PVC
- All configuration via ConfigMap/Secret structure

**Validation**:
- YAML syntax valid
- Lint passes: `helm lint infra/kubernetes/helm`

**References**:
- [contracts/kubernetes-values-schema.yml](contracts/kubernetes-values-schema.yml)
- [data-model.md - KubernetesDeploymentConfiguration](data-model.md#3-kubernetesdeploymentconfiguration)

---

### - [ ] P2-003 [P] Create Helm values.dev.yaml

Create development overrides for values.yaml.

**File**: `/infra/kubernetes/helm/values.dev.yaml`

**Requirements**:
- Lower resource limits for local K8s
- Development-friendly logging
- Ingress for local domain (.local)
- Disable persistence for faster iteration

**Validation**: Overrides apply correctly with `-f values.dev.yaml`

**References**:
- [contracts/kubernetes-values-schema.yml](contracts/kubernetes-values-schema.yml)

---

### - [ ] P2-004 Create Helm Deployment Template

Create Kubernetes Deployment resource template.

**File**: `/infra/kubernetes/helm/templates/deployment.yaml`

**Requirements**:
- Use values from `values.yaml`
- Pod template with:
  - Container: lucia-agent
  - Image from values
  - Ports: 8080
  - Environment variables from ConfigMap and Secret
  - Resource limits/requests from values
  - Liveness probe: `/health` endpoint
  - Readiness probe: `/health` endpoint
  - Startup probe: `/health` endpoint
- Replica count from values
- Rolling update strategy

**Validation**:
- Template renders: `helm template infra/kubernetes/helm`
- Valid Kubernetes YAML

**References**:
- [research.md - Kubernetes Deployment Patterns](research.md#3-kubernetes-deployment-patterns)
- [contracts/kubernetes-values-schema.yml - Deployment Structure](contracts/kubernetes-values-schema.yml)

---

### - [ ] P2-005 [P] Create Helm Service Template

Create Kubernetes Service resource template.

**File**: `/infra/kubernetes/helm/templates/service.yaml`

**Requirements**:
- Service type from values (ClusterIP/NodePort/LoadBalancer)
- Port mapping (80 -> 8080 by default)
- Selector matching deployment labels
- SessionAffinity: ClientIP (for agent consistency)

**Validation**: Template renders with valid Service spec

**References**:
- [research.md - Kubernetes Deployment Patterns](research.md#3-kubernetes-deployment-patterns)

---

### - [ ] P2-006 [P] Create Helm Ingress Template

Create Kubernetes Ingress resource template.

**File**: `/infra/kubernetes/helm/templates/ingress.yaml`

**Requirements**:
- Conditional: only if `ingress.enabled`
- Ingress class from values
- Annotations from values
- Host and path configuration from values
- TLS configuration if specified
- Backend service reference

**Validation**: Template renders only when ingress enabled

**References**:
- [research.md - Kubernetes Deployment Patterns](research.md#3-kubernetes-deployment-patterns)
- [contracts/kubernetes-values-schema.yml - Ingress](contracts/kubernetes-values-schema.yml)

---

### - [ ] P2-007 [P] Create Helm ConfigMap Template

Create ConfigMap for non-sensitive configuration.

**File**: `/infra/kubernetes/helm/templates/configmap.yaml`

**Requirements**:
- Non-sensitive configuration:
  - `OpenAI__BaseUrl`
  - `OpenAI__ModelId`
  - `OpenAI__EmbeddingModelId`
  - `Redis__ConnectionString`
  - `Logging__LogLevel__Default`
- Values from Helm values.yaml
- Used by Deployment as env vars

**Validation**: ConfigMap contains all non-sensitive config

**References**:
- [data-model.md - ApplicationConfiguration](data-model.md#1-applicationconfiguration)
- [contracts/kubernetes-values-schema.yml - ConfigMap](contracts/kubernetes-values-schema.yml)

---

### - [ ] P2-008 [P] Create Helm Secret Template

Create Secret for sensitive configuration.

**File**: `/infra/kubernetes/helm/templates/secret.yaml`

**Requirements**:
- Sensitive configuration:
  - `HomeAssistant__AccessToken`
  - `OpenAI__ApiKey`
  - `Redis__Password`
- Values base64-encoded
- Users provide values in values.yaml or during install
- Used by Deployment as env vars

**Validation**: Secret properly encoded and referenced

**References**:
- [data-model.md - ApplicationConfiguration](data-model.md#1-applicationconfiguration)
- [contracts/kubernetes-values-schema.yml - Secret](contracts/kubernetes-values-schema.yml)

---

### - [ ] P2-009 [P] Create Helm Redis StatefulSet Template

Create StatefulSet for Redis with persistence.

**File**: `/infra/kubernetes/helm/templates/redis-deployment.yaml`

**Requirements**:
- StatefulSet for stable network identity
- Image: `redis:7-alpine`
- PersistentVolumeClaim template (1Gi by default)
- Resource limits (256Mi memory)
- Health checks (redis-cli ping)
- Service for internal DNS
- ConfigMap for redis.conf if needed

**Validation**: StatefulSet creates and Redis is accessible

**References**:
- [research.md - Redis Configuration & Deployment](research.md#5-redis-configuration--deployment)
- [data-model.md - RedisConfiguration](data-model.md#5-redisconfiguration)

---

### - [ ] P2-010 Create Helm _helpers.tpl

Create template helpers for common patterns.

**File**: `/infra/kubernetes/helm/templates/_helpers.tpl`

**Requirements**:
- `lucia.fullname` template
- `lucia.labels` template (common labels)
- `lucia.selectorLabels` template
- `lucia.serviceAccountName` template (if needed)

**Validation**: Helpers used consistently across templates

**References**:
- [research.md - Kubernetes Deployment Patterns](research.md#3-kubernetes-deployment-patterns)

---

### - [ ] P2-011 Create Helm NOTES.txt

Create post-install notes for users.

**File**: `/infra/kubernetes/helm/templates/NOTES.txt`

**Requirements**:
- Success message
- How to get service URL
- Next steps (configure secrets, access application)
- Troubleshooting commands

**Validation**: Notes display after `helm install`

**References**:
- [research.md - Kubernetes Deployment Patterns](research.md#3-kubernetes-deployment-patterns)

---

### - [ ] P2-012 Create Raw Kubernetes Manifests (Alternative)

Create raw YAML manifests as alternative to Helm.

**Files**:
- `/infra/kubernetes/manifests/namespace.yaml`
- `/infra/kubernetes/manifests/deployment.yaml`
- `/infra/kubernetes/manifests/service.yaml`
- `/infra/kubernetes/manifests/ingress.yaml`
- `/infra/kubernetes/manifests/configmap.yaml`
- `/infra/kubernetes/manifests/secret.yaml`
- `/infra/kubernetes/manifests/redis-deployment.yaml`
- `/infra/kubernetes/manifests/kustomization.yaml`

**Requirements**:
- Equivalent functionality to Helm chart
- Use placeholders for user-specific values
- Kustomization for overlays
- Namespace: `lucia`

**Validation**:
- Apply succeeds: `kubectl apply -k infra/kubernetes/manifests`
- All resources created

**References**:
- [research.md - Kubernetes Deployment Patterns](research.md#3-kubernetes-deployment-patterns)
- Helm templates created above

---

### - [ ] P2-013 Create Kubernetes Deployment Documentation (Helm)

Create Helm deployment guide.

**File**: `/infra/kubernetes/helm/README.md`

**Content Requirements** (per FR-006):
- Prerequisites (kubectl, Helm, K8s cluster)
- Installation steps:
  1. Add values to `values.yaml` or create `values.local.yaml`
  2. `helm install lucia ./infra/kubernetes/helm -f values.local.yaml`
  3. Verify deployment
- Ingress setup (nginx-ingress, Traefik)
- Secrets management (create secrets before install)
- Scaling instructions
- Upgrade procedures
- Troubleshooting (pod logs, describe resources)
- Configuration reference

**Validation**: User can deploy in <20 minutes

**References**:
- [quickstart.md - Kubernetes Deployment](quickstart.md#kubernetes-deployment)
- [spec.md - User Story 2 Acceptance Scenarios](spec.md#user-story-2---kubernetes-cluster-deployment-priority-p2)

---

### - [ ] P2-014 [P] Create Kubernetes Deployment Documentation (Raw Manifests)

Create raw manifest deployment guide.

**File**: `/infra/kubernetes/README.md`

**Content Requirements**:
- Prerequisites
- Installation steps using kubectl
- Kustomize usage
- Differences from Helm approach
- When to use raw manifests vs Helm

**Validation**: Alternative deployment path documented

**References**:
- [quickstart.md - Kubernetes Deployment](quickstart.md#kubernetes-deployment)

---

### - [ ] P2-015 Manual Testing: Kubernetes Deployment (Helm)

Manually test Helm deployment workflow.

**Test Scenarios**:
1. Fresh Helm install
2. Configuration via values.yaml
3. Ingress access
4. Pod restarts and recovery
5. Upgrade workflow
6. Uninstall and cleanup

**Acceptance Criteria** (per spec.md):
- [ ] All resources created successfully
- [ ] Application accessible through ingress
- [ ] Pod restarts automatically on failure
- [ ] ConfigMap/Secret updates trigger restart
- [ ] Deployment completed in <20 minutes

**Validation**: All acceptance scenarios from User Story 2 pass

**References**:
- [spec.md - User Story 2 Acceptance Scenarios](spec.md#user-story-2---kubernetes-cluster-deployment-priority-p2)

---

### - [ ] P2-016 [P] Manual Testing: Kubernetes Deployment (Raw Manifests)

Test raw manifest deployment workflow.

**Test Scenarios**:
1. Fresh kubectl apply
2. Configuration via manifests
3. Resource validation
4. Kustomize overlays

**Acceptance Criteria**: Same as P2-015 but using kubectl/kustomize

**Validation**: Raw manifest deployment works equivalently to Helm

---

## P2 (User Story 3): Linux systemd Deployment

**Goal**: Enable systemd deployment in <25 minutes (SC-003)  
**Priority**: P2 (Parallel with Kubernetes)

### - [ ] P2-017 Create systemd Service Unit File

Create systemd service file for Lucia.

**File**: `/infra/systemd/lucia.service`

**Requirements** (per [research.md](research.md#6-systemd-service-management)):
- `[Unit]`:
  - Description
  - After=network-online.target redis.service
  - Wants=network-online.target
  - Requires=redis.service
- `[Service]`:
  - Type=notify (for ASP.NET Core)
  - ExecStart=/usr/bin/dotnet /opt/lucia/lucia.AgentHost.dll
  - WorkingDirectory=/opt/lucia
  - EnvironmentFile=/etc/lucia/lucia.env
  - Restart=on-failure
  - RestartSec=10s
  - User=lucia (DynamicUser=yes alternative)
  - NoNewPrivileges=true
  - PrivateTmp=true
- `[Install]`:
  - WantedBy=multi-user.target

**Validation**:
- Syntax check: `systemd-analyze verify lucia.service`
- Service loads: `systemctl status lucia`

**References**:
- [research.md - systemd Service Management](research.md#6-systemd-service-management)
- [data-model.md - SystemdDeploymentConfiguration](data-model.md#4-systemddeploymentconfiguration)

---

### - [ ] P2-018 Create systemd Environment File Template

Create environment variable template for systemd.

**File**: `/infra/systemd/lucia.env.example`

**Requirements** (per [systemd-env-schema.md](contracts/systemd-env-schema.md)):
- All required environment variables:
  - HomeAssistant__BaseUrl
  - HomeAssistant__AccessToken
  - OpenAI__ApiKey
  - OpenAI__BaseUrl
  - OpenAI__ModelId
  - OpenAI__EmbeddingModelId
  - Redis__ConnectionString
  - Redis__Password (optional)
  - Logging__LogLevel__Default
- Format: `KEY=VALUE` (no spaces, no quotes unless needed)
- Comments explaining each variable
- Security note about file permissions (600)

**Validation**:
- Syntax valid for systemd EnvironmentFile
- All required variables present

**References**:
- [contracts/systemd-env-schema.md](contracts/systemd-env-schema.md)
- [data-model.md - ApplicationConfiguration](data-model.md#1-applicationconfiguration)

---

### - [ ] P2-019 Create systemd Installation Script

Create automated installation script for Linux systems.

**File**: `/infra/systemd/install.sh`

**Requirements**:
- Check prerequisites (.NET 10 runtime, Redis)
- Create `/opt/lucia` directory
- Copy application binaries
- Create `lucia` user (or use DynamicUser)
- Copy systemd service file to `/etc/systemd/system/`
- Create `/etc/lucia/` for configuration
- Copy `lucia.env.example` to `/etc/lucia/lucia.env`
- Set file permissions (service file 644, env file 600)
- Reload systemd daemon
- Provide next steps (edit .env, start service)

**Validation**:
- Script runs without errors
- Service installed correctly
- User can enable and start service

**References**:
- [research.md - systemd Service Management](research.md#6-systemd-service-management)
- [quickstart.md - Linux systemd Deployment](quickstart.md#linux-systemd-deployment)

---

### - [ ] P2-020 Create systemd Deployment Documentation

Create comprehensive systemd deployment guide.

**File**: `/infra/systemd/README.md`

**Content Requirements** (per FR-007):
- Prerequisites (.NET runtime, Redis, systemd)
- Installation methods:
  1. Automated (install.sh script)
  2. Manual step-by-step
- Dependency setup (Redis installation/configuration)
- Configuration guide (editing lucia.env)
- Service management:
  - Enable on boot: `systemctl enable lucia`
  - Start/stop: `systemctl start/stop lucia`
  - Status: `systemctl status lucia`
  - Logs: `journalctl -u lucia -f`
- Log management (journald integration)
- Troubleshooting (service fails, permission issues)
- Update/upgrade procedures
- Security hardening (user isolation, file permissions)

**Validation**: User can deploy in <25 minutes

**References**:
- [quickstart.md - Linux systemd Deployment](quickstart.md#linux-systemd-deployment)
- [spec.md - User Story 3 Acceptance Scenarios](spec.md#user-story-3---linux-systemd-service-deployment-priority-p2)
- [research.md - systemd Service Management](research.md#6-systemd-service-management)

---

### - [ ] P2-021 Manual Testing: systemd Deployment

Manually test systemd deployment workflow.

**Test Scenarios**:
1. Fresh installation using install.sh
2. Manual installation step-by-step
3. Configuration via lucia.env
4. Service start on boot
5. Log access via journalctl
6. Service restart after crash
7. Configuration update and restart

**Acceptance Criteria** (per spec.md):
- [ ] Service starts on boot
- [ ] `systemctl status lucia` shows healthy
- [ ] Logs accessible via `journalctl -u lucia`
- [ ] Configuration updates take effect on restart
- [ ] Deployment completed in <25 minutes

**Validation**: All acceptance scenarios from User Story 3 pass

**References**:
- [spec.md - User Story 3 Acceptance Scenarios](spec.md#user-story-3---linux-systemd-service-deployment-priority-p2)

---

## P3 (User Story 4): CI/CD Pipeline

**Goal**: Automate Docker image publishing in <10 minutes (SC-004)  
**Priority**: P3 (Lower priority, maintainer-focused)

### - [ ] P3-001 Create GitHub Actions Docker Build Workflow

Create automated Docker build and push workflow.

**File**: `.github/workflows/docker-build-push.yml`

**Requirements** (per [research.md](research.md#4-github-actions-docker-workflows)):
- Trigger:
  - `push` to `main` branch
  - Tag push matching `v*.*.*` pattern
  - Manual workflow_dispatch
- Jobs:
  - **build-and-push**:
    - Runs on: ubuntu-latest
    - Steps:
      1. Checkout code
      2. Set up Docker Buildx
      3. Login to Docker Hub (using secrets.DOCKER_HUB_TOKEN)
      4. Extract metadata (tags, labels)
      5. Build and push multi-platform (amd64, arm64)
      6. Tag with:
         - `latest` (for main branch)
         - Semantic version `v1.2.3` (for tags)
         - Commit SHA `sha-abc123` (for traceability)
- Secrets required:
  - `DOCKER_HUB_USERNAME`
  - `DOCKER_HUB_TOKEN`

**Validation**:
- Workflow syntax valid
- Test with workflow_dispatch
- Build succeeds and pushes to Docker Hub

**References**:
- [research.md - GitHub Actions Docker Workflows](research.md#4-github-actions-docker-workflows)
- [data-model.md - CICDConfiguration](data-model.md#6-cicdconfiguration)
- [spec.md - User Story 4](spec.md#user-story-4---cicd-pipeline-for-container-publishing-priority-p3)

---

### - [ ] P3-002 [P] Create GitHub Actions Helm Lint Workflow

Create Helm chart validation workflow.

**File**: `.github/workflows/helm-lint.yml`

**Requirements**:
- Trigger: PR changes to `infra/kubernetes/helm/**`
- Jobs:
  - **lint**:
    - Runs on: ubuntu-latest
    - Steps:
      1. Checkout code
      2. Set up Helm
      3. Run `helm lint infra/kubernetes/helm`
      4. Validate with `helm template`
      5. Check schema with kubeval (optional)

**Validation**:
- Workflow catches invalid Helm charts
- Runs on relevant PRs

**References**:
- [research.md - Kubernetes Deployment Patterns](research.md#3-kubernetes-deployment-patterns)

---

### - [ ] P3-003 [P] Create GitHub Actions Infrastructure Validation Workflow

Create workflow to validate all infrastructure configurations.

**File**: `.github/workflows/validate-infrastructure.yml`

**Requirements**:
- Trigger: PR changes to `infra/**`
- Jobs:
  - **validate-docker**: Validate Dockerfiles and compose files
  - **validate-k8s**: Validate Kubernetes manifests
  - **validate-systemd**: Check systemd service file syntax
- Use tools: hadolint, yamllint, kubeval, systemd-analyze

**Validation**:
- Workflow catches configuration errors
- Runs on relevant PRs

**References**:
- [research.md](research.md) - All deployment methods

---

### - [ ] P3-004 Manual Testing: CI/CD Pipeline

Test GitHub Actions workflows.

**Test Scenarios**:
1. Push to main triggers Docker build
2. Tag push (v1.0.0) creates versioned image
3. Failed build prevents image push
4. Multi-platform build succeeds
5. Helm lint catches invalid charts
6. Infrastructure validation catches errors

**Acceptance Criteria** (per spec.md):
- [ ] Workflow builds and publishes on main push
- [ ] Version tags create tagged images
- [ ] Build failures stop workflow
- [ ] Published images work correctly
- [ ] Workflow completes in <10 minutes

**Validation**: All acceptance scenarios from User Story 4 pass

**References**:
- [spec.md - User Story 4 Acceptance Scenarios](spec.md#user-story-4---cicd-pipeline-for-container-publishing-priority-p3)

---

## Polish Phase: Documentation and Quality

### - [ ] POLISH-001 Create Configuration Reference Documentation

Create comprehensive configuration reference.

**File**: `/infra/docs/configuration-reference.md`

**Content Requirements**:
- Complete list of all configuration variables
- For each variable:
  - Name, type, required/optional
  - Description
  - Valid values/format
  - Default value
  - Examples
  - Security classification (sensitive/non-sensitive)
- Organized by category (HA, LLM, Redis, Logging)

**Validation**: All configuration options documented

**References**:
- [data-model.md - ApplicationConfiguration](data-model.md#1-applicationconfiguration)
- All env/config schemas

---

### - [ ] POLISH-002 [P] Create Troubleshooting Documentation

Create troubleshooting guide for common issues.

**File**: `/infra/docs/troubleshooting.md`

**Content Requirements**:
- Common issues organized by deployment method
- For each issue:
  - Symptoms
  - Root cause
  - Solution steps
  - Prevention
- Debugging techniques (logs, health checks, connectivity tests)
- Edge case handling (from spec.md)

**Validation**: Covers all edge cases from spec

**References**:
- [spec.md - Edge Cases](spec.md#edge-cases)
- All deployment guides

---

### - [ ] POLISH-003 [P] Create Security Hardening Documentation

Create security best practices guide.

**File**: `/infra/docs/security-hardening.md`

**Content Requirements**:
- Secrets management (never commit, use proper storage)
- Network security (firewall rules, TLS)
- Container security (non-root, minimal images)
- Kubernetes security (RBAC, network policies)
- File permissions (systemd env files)
- API key rotation procedures
- Redis authentication

**Validation**: Comprehensive security guidance

**References**:
- [research.md](research.md) - Security notes in all sections
- [data-model.md - ApplicationConfiguration](data-model.md#1-applicationconfiguration) sensitive fields

---

### - [ ] POLISH-004 [P] Create LLM Providers Configuration Guide

Create guide for configuring different LLM providers.

**File**: `/infra/docs/llm-providers.md`

**Content Requirements**:
- Configuration examples for each provider:
  - OpenAI
  - Azure OpenAI
  - Ollama (local)
  - LM Studio (local)
  - Other OpenAI-compatible endpoints
- For each provider:
  - Base URL format
  - API key requirements
  - Supported models
  - Configuration example
  - Troubleshooting tips

**Validation**: Users can configure any supported provider

**References**:
- [research.md - OpenAI-Compatible LLM Endpoints](research.md#7-openai-compatible-llm-endpoints)
- All env.example files

---

### - [ ] POLISH-005 Create Health Check Utility Script

Create shell script for validating deployment health.

**File**: `/infra/scripts/health-check.sh`

**Requirements**:
- Check Lucia application health endpoint
- Check Redis connectivity
- Check Home Assistant connectivity
- Check LLM endpoint connectivity
- Report status for each component
- Exit code indicates overall health

**Validation**: Script correctly identifies unhealthy deployments

**References**:
- [research.md - Docker Compose Best Practices](research.md#2-docker-compose-best-practices) health checks

---

### - [ ] POLISH-006 [P] Create Configuration Backup Script

Create script for backing up deployment configuration.

**File**: `/infra/scripts/backup-config.sh`

**Requirements**:
- Backup all configuration files (env files, values.yaml)
- Exclude sensitive values (warn user to backup separately)
- Create timestamped backup archive
- Support for all deployment methods

**Validation**: Backup can restore configuration

**References**:
- All deployment configurations

---

### - [ ] POLISH-007 [P] Create Deployment Validation Script

Create script for validating deployment configuration before applying.

**File**: `/infra/scripts/validate-deployment.sh`

**Requirements**:
- Validate configuration file syntax
- Check required variables present
- Validate URL formats
- Check for common mistakes (placeholders not replaced)
- Support for Docker, Kubernetes, systemd configs

**Validation**: Script catches common configuration errors

**References**:
- All configuration schemas

---

### - [ ] POLISH-008 Update Root README.md with Deployment Section

Update project README with deployment information.

**File**: `/README.md`

**Changes**:
- Add new "🚀 Deployment" section after existing sections
- Content:
  - Overview: Three deployment methods available
  - **Quick Start (Docker Compose)**:
    - One-liner command or link to `/infra/docker/README.md`
  - **Kubernetes Deployment**:
    - Brief description, link to `/infra/kubernetes/README.md`
  - **Linux systemd Service**:
    - Brief description, link to `/infra/systemd/README.md`
  - **CI/CD Pipeline**:
    - Brief description for contributors
  - Link to `/infra/README.md` for comprehensive deployment guide

**Validation**:
- README provides clear entry point to deployment docs
- Links work correctly

**References**:
- [plan.md - Updated Root Documentation](plan.md#updated-root-documentation)
- [quickstart.md](quickstart.md)

---

### - [ ] POLISH-009 Create Pull Request with All Changes

Create comprehensive PR for infrastructure deployment feature.

**PR Requirements**:
- Title: "feat: Add infrastructure deployment utilities and documentation"
- Description:
  - Link to spec: [spec.md](spec.md)
  - Summary of changes
  - Deployment methods implemented (Docker, K8s, systemd, CI/CD)
  - Documentation created
  - Testing performed
  - **Breaking changes**: None
  - **User impact**: Enables deployment for end users
- Checklist:
  - [ ] All tasks completed
  - [ ] Documentation reviewed for clarity
  - [ ] Manual testing completed for all deployment methods
  - [ ] Configuration examples validated
  - [ ] No sensitive information committed
  - [ ] README.md updated

**Validation**: PR ready for review

**References**:
- [spec.md](spec.md)
- All tasks above

---

## Task Summary

**Total Tasks**: 51 tasks across 6 phases

**Phase Breakdown**:
- **Setup (3 tasks)**: Foundation and overview documentation
- **P1 Docker (8 tasks)**: Docker Compose deployment (MVP) - User Story 1
- **P2 Kubernetes (16 tasks)**: Kubernetes deployment - User Story 2
- **P2 systemd (5 tasks)**: Linux systemd deployment - User Story 3
- **P3 CI/CD (4 tasks)**: GitHub Actions pipeline - User Story 4
- **Polish (9 tasks)**: Additional documentation and utilities

**Parallelizable Tasks**: 15 tasks marked with `[P]` can be worked on simultaneously

**Priority Execution**:
1. **Setup Phase** → Foundation for all work
2. **P1 (User Story 1)** → Docker deployment (MVP, highest value)
3. **P2 (User Stories 2 & 3)** → Kubernetes and systemd (parallel if possible)
4. **P3 (User Story 4)** → CI/CD pipeline (maintainer-focused)
5. **Polish Phase** → Quality and documentation refinement

---

## Success Criteria Mapping

Task completion directly addresses all success criteria:

- **SC-001** (Docker <15 min): P1-001 through P1-008
- **SC-002** (K8s <20 min): P2-001 through P2-016
- **SC-003** (systemd <25 min): P2-017 through P2-021
- **SC-004** (CI/CD <10 min): P3-001 through P3-004
- **SC-005** (Functional deployments): All health checks in deployment tasks
- **SC-006** (Documentation clarity): All README files and POLISH phase
- **SC-007** (95% success rate): All quickstart guides and validation scripts
- **SC-008** (Valid configurations): All example files and validation tasks

---

## Testing Strategy

**Per Deployment Method**:
1. Fresh deployment following documentation
2. Configuration changes and service restart
3. Persistence validation (Redis data)
4. Health check validation
5. Error scenario handling
6. Time to deploy measurement

**Infrastructure Validation**:
- Docker build succeeds
- Compose up succeeds with health checks
- Helm install succeeds
- kubectl apply succeeds
- systemd service loads and starts
- GitHub Actions workflows execute

**Documentation Validation**:
- New user can complete deployment
- All configuration options work
- Troubleshooting resolves common issues
- Examples are copy-paste ready

---

## Implementation Notes

1. **Configuration Security**: Never commit real credentials; all examples use placeholders
2. **Multi-Platform**: Docker images support both amd64 and arm64
3. **Documentation Quality**: Every deployment method needs complete README
4. **Testing Required**: Manual testing mandatory for each deployment method before PR
5. **File Permissions**: Ensure proper permissions for sensitive config files (600)
6. **Health Checks**: All deployment methods must expose health endpoints
7. **Telemetry**: OpenTelemetry integration already exists, ensure infrastructure exposes it

---

**Implementation Ready** ✅  
All tasks defined with clear requirements, validation criteria, and references to research/specifications.

**Next Step**: Begin with SETUP phase, then proceed to P1 (Docker) as MVP.
