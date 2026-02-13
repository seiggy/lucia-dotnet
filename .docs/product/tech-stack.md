# Technical Stack

> Last Updated: 2025-08-06
> Version: 1.0.0

## Core Technologies

### Application Framework
- **Framework:** ASP.NET Core Web API
- **Version:** .NET 10
- **Language:** C# 13 with nullable reference types

### AI/ML Framework
- **Primary:** [Microsoft Agent Framework](https://learn.microsoft.com/agent-framework/)
- **Version:** 1.0.0
- **Orchestration:** MagenticOne multi-agent pattern

### Database
- **Primary:** In-Memory (current) / PostgreSQL (planned)
- **Version:** PostgreSQL 17+ (planned)
- **ORM:** Entity Framework Core (planned)

## Agent Stack

### LLM Providers
- **Online:** OpenAI (GPT-4o), Google Gemini, Anthropic Claude
- **Offline:** LLaMa and local models (planned)
- **Embeddings:** text-embedding-3-small (OpenAI)

### Agent Runtime
- **Core:** Microsoft Agent Framework Agents
- **Communication:** A2A (Agent-to-Agent) Protocol
- **Registry:** Custom agent registry with HTTP API

## Frontend Stack

### Home Assistant Plugin
- **Language:** Python 3.12+
- **Framework:** Home Assistant Custom Component
- **API Client:** aiohttp for async HTTP

### Management UI (Planned)
- **Framework:** React
- **Version:** Latest stable
- **Build Tool:** Vite

## Infrastructure

### Container Platform
- **Runtime:** Docker with Linux containers
- **Orchestration:** Kubernetes
- **Service Mesh:** Istio (optional)

### Cloud-Native Framework
- **Platform:** .NET Aspire
- **Version:** 10.0.0
- **Features:** Service discovery, resilience, observability

### Observability
- **Tracing:** OpenTelemetry
- **Metrics:** OpenTelemetry Metrics
- **Logging:** Microsoft.Extensions.Logging

## Home Assistant Integration

### API Integration
- **REST API:** Strongly-typed client via Roslyn source generators
- **WebSocket:** Real-time event streaming (planned)
- **LLM API:** Home Assistant LLM integration endpoint
- **Conversation API:** Natural language processing

### Authentication
- **Method:** Long-lived access tokens
- **Storage:** Secure configuration management

## Development Tools

### Source Generation
- **Tool:** C# Roslyn Source Generators
- **Purpose:** Generate type-safe Home Assistant API clients

### Testing
- **Framework:** xUnit
- **Mocking:** FakeItEasy
- **Integration:** Aspire.Hosting.Testing

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
- **Registry:** Consul (optional for K8s)

### Configuration
- **Management:** ASP.NET Core Configuration
- **Secrets:** User Secrets (dev) / Kubernetes Secrets (prod)

## Code Repository
- **URL:** https://github.com/seiggy/jarvis-dotnet
- **Structure:** Monorepo with multiple projects
- **Version Control:** Git with conventional commits