# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Purpose

Lucia is a Agent Framework-based agentic solution that serves as an autonomous whole-home automation manager for Home Assistant. The application acts as an intelligent assistant that integrates with Home Assistant Core APIs to provide automated home management capabilities.

### Home Assistant Integration Points

The application integrates with Home Assistant through:

- **LLM API**: Primary integration point for AI-powered responses and automation decisions
  - Documentation: https://developers.home-assistant.io/docs/core/llm/
- **Conversation API**: Handles natural language interactions and intent processing
  - Documentation: https://developers.home-assistant.io/docs/intent_conversation_api
- **WebSocket API**: Real-time event streaming and state updates from Home Assistant
- **REST API**: Direct access to Home Assistant entities, services, and configuration

The system operates as an autonomous assistant that can understand natural language commands, monitor home state, and execute automation decisions through Home Assistant's various APIs.

## Architecture Overview

This is a .NET 10 + Aspire application with the following structure:

- **lucia.AgentHost**: Main Web API application (ASP.NET Core) that hosts and runs the AIAgents
- **lucia.AppHost**: .NET Aspire orchestrator for development. Gives telemetry, and debugging views and tools during development.
- **lucia.ServiceDefaults**: Shared library containing common services (OpenTelemetry, health checks, service discovery, resilience)
- **lucia.Tests**: Integration test project using xUnit and Aspire.Hosting.Testing

The application uses .NET Aspire for cloud-native development with built-in observability, service discovery, and resilience patterns. The ServiceDefaults project includes OpenTelemetry tracing/metrics, health checks, and HTTP client configurations with resilience handlers.

## Key Technologies

- .NET 9 with C# nullable reference types enabled
- ASP.NET Core Web API with OpenAPI/Swagger
- .NET Aspire for orchestration and service defaults
- **Microsoft Semantic Kernel**: Core AI framework for agentic behaviors and LLM integration
- **Multi-LLM Support**: Support for both online and offline LLMs through Semantic Kernel:
  - **Online**: OpenAI, Google Gemini, Anthropic Claude
  - **Offline/Local**: LLaMa and other local models
- **Home Assistant APIs**: WebSocket and REST API integration for home automation
- **C# Roslyn Code Generators**: Generate strongly-typed API clients for Home Assistant REST API
- xUnit for testing with FakeItEasy for mocking
- OpenTelemetry for observability
- Docker support with Linux containers

## Common Commands

### Build and Run
```bash
# Build the entire solution
dotnet build

# Run the main application directly
dotnet run --project lucia-dotnet

# Run via Aspire AppHost (recommended for development)
dotnet run --project lucia.AppHost
```

### Testing
```bash
# Run all tests
dotnet test

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test lucia.Tests
```

### Development
```bash
# Restore packages
dotnet restore

# Clean solution
dotnet clean

# Watch for changes (main app)
dotnet watch --project lucia-dotnet

# Watch for changes (AppHost)
dotnet watch --project lucia.AppHost
```

## Service Endpoints

When running via AppHost, the application will be available at:
- HTTP: http://localhost:5211
- HTTPS: https://localhost:7000

Health check endpoints (development only):
- `/health` - Overall health status
- `/alive` - Liveness check

## Configuration

The application uses standard ASP.NET Core configuration with:
- `appsettings.json` and `appsettings.Development.json`
- Environment variables
- User secrets (configured for AppHost project)

## Important Notes

- The main web API currently has an empty Controllers folder - controllers need to be implemented for Home Assistant integration endpoints
- Integration tests are set up but commented out - uncomment and update the template code when ready to use
- OpenTelemetry is configured but requires `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable for external exporters
- Docker support is included with Linux target OS
- The application uses implicit usings and nullable reference types throughout

## Home Assistant Integration Requirements

When implementing Home Assistant integration:

1. **LLM API Integration**: Implement endpoints that conform to Home Assistant's LLM API specification for AI-powered responses
2. **Conversation API**: Handle natural language processing and intent recognition for home automation commands
3. **WebSocket Client**: Establish persistent connection to Home Assistant for real-time event monitoring and state updates
4. **REST API Client**: Generate strongly-typed API client using C# Roslyn code generators
   - REST API Documentation: https://developers.home-assistant.io/docs/api/rest
   - Use source generators to create type-safe clients from OpenAPI specifications
5. **Authentication**: Support Home Assistant's long-lived access tokens for API authentication
6. **State Management**: Maintain synchronized state between Lucia and Home Assistant entities
7. **Event Processing**: Process and respond to Home Assistant events autonomously using Semantic Kernel agents

## LLM Provider Configuration

The application should support multiple LLM providers through Semantic Kernel's standardized interfaces:

### Online LLM Providers
- **OpenAI**: GPT-4, GPT-3.5-turbo, and other OpenAI models
- **Google Gemini**: Gemini Pro and other Google AI models  
- **Anthropic Claude**: Claude 3 and other Anthropic models

### Offline/Local LLM Support
- **LLaMa**: Local LLaMa model variants
- **Other Local Models**: Any model compatible with Semantic Kernel's local inference capabilities

### Configuration Requirements
- Support for API key management and secure credential storage
- Model selection and fallback mechanisms
- Performance optimization for local vs. remote inference
- Cost management and usage monitoring for cloud providers

## Agent OS Documentation

### Product Context
- **Mission & Vision:** @.agent-os/product/mission.md
- **Technical Architecture:** @.agent-os/product/tech-stack.md
- **Development Roadmap:** @.agent-os/product/roadmap.md
- **Decision History:** @.agent-os/product/decisions.md

### Development Standards
- **Code Style:** @~/.agent-os/standards/code-style.md
- **Best Practices:** @~/.agent-os/standards/best-practices.md

### Project Management
- **Active Specs:** @.agent-os/specs/
- **Spec Planning:** Use `@~/.agent-os/instructions/create-spec.md`
- **Tasks Execution:** Use `@~/.agent-os/instructions/execute-tasks.md`

## Workflow Instructions

When asked to work on this codebase:

1. **First**, check @.agent-os/product/roadmap.md for current priorities
2. **Then**, follow the appropriate instruction file:
   - For new features: @.agent-os/instructions/create-spec.md
   - For tasks execution: @.agent-os/instructions/execute-tasks.md
3. **Always**, adhere to the standards in the files listed above

## Important Notes

- Product-specific files in `.agent-os/product/` override any global standards
- User's specific instructions override (or amend) instructions found in `.agent-os/specs/...`
- Always adhere to established patterns, code style, and best practices documented above
