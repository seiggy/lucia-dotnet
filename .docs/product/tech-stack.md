# Technical Stack

> Last Updated: 2026-09-13
> Version: 1.3.0

## Core Technologies

### Application Framework
- **Framework:** ASP.NET Core Web API
- **Version:** .NET 10
- **Language:** C# 14 with nullable reference types

### AI/ML Framework
- **Primary:** [Microsoft Agent Framework](https://learn.microsoft.com/agent-framework/)
- **Version:** 1.0.0-rc3
- **Orchestration:** Sequential & Fan-out/Fan-in custom orchestration workflow

### Database
- **Primary:** Redis + MongoDB (runtime)
- **Lightweight Options:** SQLite and PostgreSQL via direct ADO.NET providers (`Microsoft.Data.Sqlite`, `Npgsql`)
- **Runtime Usage:** Redis for session/task persistence; MongoDB for traces, config overrides, and task records
- **Storage:** MongoDB databases `luciatraces`, `luciaconfig`, `luciatasks`

## Agent Stack

### LLM Providers
- **Online:** Azure OpenAI, OpenAI, Google Gemini, Anthropic Claude
- **GitHub Copilot:** GitHub.Copilot.SDK 1.0.13 for GitHub authentication, model discovery, context tiers, and reasoning effort. The evaluation harness can use Copilot as its judge while keeping models under test on local backends.
- **Copilot Agent Framework adapter:** Microsoft.Agents.AI.GitHub.Copilot 1.20.0 preserves the existing `AIAgent` integration. Its shared abstractions require Microsoft.Agents.AI.Abstractions 1.20.0, Microsoft.Extensions.AI.Abstractions 10.9.0, and DI/Logging abstractions 10.0.11. The existing core and workflow versions remain unchanged; the required abstraction updates are API-compatible.
- **Evaluation judge:** Azure OpenAI v1 Responses API through the existing OpenAI and Microsoft.Extensions.AI packages, with an explicit legacy Chat Completions opt-out.
- **Offline:** OLLaMa and llama.CPP
- **Embeddings:** Support for Azure OpenAI, OpenAI, and local deployed Embeddings

### Agent Runtime
- **Core:** Microsoft Agent Framework Agents
- **Communication:** A2A (Agent-to-Agent) Protocol
- **Registry:** Custom agent registry with HTTP API

### Web Search (Plugins)
- **Integration:** Web Search for the General Agent is configured through the plugin library using either the SearXNG or Brave Plugins

### MCP Tool Servers
- **Protocol:** [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) over HTTP/SSE
- **Client:** Microsoft.Extensions.AI ModelContextProtocol (v0.9.0-preview.1)
- **Transports:** stdio (local processes), HTTP/SSE (remote, e.g. MetaMCP)
- **Dynamic Agents:** MCP tools are assigned to agent definitions and resolved at runtime via `IMcpToolRegistry`

## Frontend Stack

### Home Assistant Plugin
- **Language:** Python 3.12+
- **Framework:** Home Assistant Custom Component
- **API Client:** aiohttp for async HTTP

### Management UI
- **Framework:** React
- **Version:** Latest stable
- **Build Tool:** Vite

## Infrastructure

### Container Platform
- **Runtime:** Docker with Linux containers
- **Orchestration:** Kubernetes

### Jetson Appliance
- **Base:** Jetson Linux 36.5.2 on Jetson Orin Nano Super P3767-0005
- **Runtime:** Native self-contained .NET 10 AgentHost
- **Persistence:** Redis 8.2.9 with AOF for active work; SQLite for configuration, traces, schedules, and archives
- **Redis toolchain:** GCC 11 on Debian Bullseye, pinned by image digest, with an execution check against the Jetson rootfs before packaging
- **Runtime helpers:** Pinned Ubuntu 22.04 ARM64 curl and libcurl packages for manager health checks and updates
- **Installer:** Client-isolated microSD captive setup with explicit NVMe erase authorization
- **Updates:** Separate attested Lucia and Jetson OS assets published through GitHub Releases

### Cloud-Native Framework
- **Platform:** .NET Aspire
- **Version:** 13
- **Features:** Service discovery, resilience, observability

### Observability
- **Tracing:** OpenTelemetry
- **Metrics:** OpenTelemetry Metrics
- **Logging:** Microsoft.Extensions.Logging
- **Remote ingestion:** OpenTelemetry Collector Contrib 0.139.0 behind Caddy 2.10.2
- **Remote backends:** Grafana 12.2.0, Tempo 2.8.2, Prometheus 3.7.1, and Loki 3.6.2
- **Jetson infrastructure metrics:** OpenTelemetry hostmetrics plus PostgreSQL exporter 0.20.1 and Redis exporter 1.89.0, scraped by a local Collector and exported through authenticated OTLP/gRPC
- **Deployment rationale:** The remote stack keeps telemetry storage and analysis off the Jetson while bounded Collector queues absorb short backend outages. Exporters and the Jetson Collector stay on the private Compose network with no host ports.

## Home Assistant Integration

### API Integration
- **REST API:** Hand-written strongly-typed `HomeAssistantClient`
- **WebSocket:** Real-time event streaming (planned)
- **LLM API:** Home Assistant LLM integration endpoint
- **Conversation API:** Natural language processing

### Authentication
- **Method:** Long-lived access tokens
- **Storage:** Secure configuration management

## Development Tools

### Home Assistant Client Implementation
- **Approach:** Hand-written typed client in `lucia.HomeAssistant/Services`
- **Contract:** `IHomeAssistantClient` abstraction for integration usage

### Testing
- **Framework:** xUnit
- **Mocking:** FakeItEasy
- **Integration:** Aspire.Hosting.Testing; Testcontainers 4.11 (PostgreSQL and Redis), with SSH.NET pinned to 2026.0
- **Evaluation backends:** Ollama, local/remote OpenAI-compatible servers, Foundry Responses deployments, and OpenRouter. Per-test token usage and estimated USD costs are reported separately from quality; lower known cost breaks quality ties. OpenRouter rates come from its catalog, while Foundry rates are configured per deployment.

### CI/CD Pipeline
- **Platform:** GitHub Actions
- **Trigger:** Push to main/develop branches
- **Tests:** Unit and integration tests

## Deployment

### Environments
- **Production:** Kubernetes cluster (home lab)
- **Staging:** Docker Compose
- **Development:** .NET Aspire AppHost

### Service Discovery
- **Method:** .NET Aspire service discovery

### Configuration
- **Management:** ASP.NET Core Configuration
- **Secrets:** User Secrets (dev) / Kubernetes Secrets (prod)

## Code Repository
- **URL:** https://github.com/seiggy/lucia-dotnet
- **Structure:** Monorepo with multiple projects
- **Version Control:** Git with conventional commits