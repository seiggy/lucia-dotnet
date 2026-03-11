# Technical Stack

> Last Updated: 2026-03-11
> Version: 1.2.0

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
- **Runtime Usage:** Redis for session/task persistence; MongoDB for traces, config overrides, and task records
- **Storage:** MongoDB databases `luciatraces`, `luciaconfig`, `luciatasks`

## Agent Stack

### LLM Providers
- **Online:** Azure OpenAI, OpenAI, Google Gemini, Anthropic Claude
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

### Cloud-Native Framework
- **Platform:** .NET Aspire
- **Version:** 13
- **Features:** Service discovery, resilience, observability

### Observability
- **Tracing:** OpenTelemetry
- **Metrics:** OpenTelemetry Metrics
- **Logging:** Microsoft.Extensions.Logging

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

### Configuration
- **Management:** ASP.NET Core Configuration
- **Secrets:** User Secrets (dev) / Kubernetes Secrets (prod)

## Code Repository
- **URL:** https://github.com/seiggy/lucia-dotnet
- **Structure:** Monorepo with multiple projects
- **Version Control:** Git with conventional commits