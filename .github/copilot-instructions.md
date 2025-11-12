# lucia .NET Development Guidelines

Auto-generated from all feature plans. Last updated: 2025-10-13

## Active Technologies

- C# 13 / .NET 10 + Microsoft.Agents.AI.Workflows 1.0, StackExchange.Redis 2.8.16, OpenTelemetry.NET 1.10 (001-multi-agent-orchestration)
- Redis 7.x (task persistence with 24h TTL) (001-multi-agent-orchestration)

## Project Structure

```
lucia-dotnet/
├── lucia.AgentHost/           # ASP.NET Core Web API hosting orchestrated AI agents
│   ├── Extensions/
│   └── Program.cs
├── lucia.Agents/              # Domain-specific agent implementations
│   ├── Agents/
│   ├── Orchestration/         # NEW: RouterExecutor, AgentExecutorWrapper, ResultAggregatorExecutor, LuciaOrchestrator
│   ├── Registry/
│   ├── Services/
│   └── Skills/
├── lucia.AppHost/             # .NET Aspire orchestration host
├── lucia.ServiceDefaults/     # Shared resilience, telemetry, health checks
├── lucia.HomeAssistant/       # Home Assistant API client
├── lucia.Tests/               # xUnit tests
└── custom_components/lucia/   # Python Home Assistant custom component
```

## Commands

```powershell
# Build solution
dotnet build lucia-dotnet.sln

# Run AppHost (starts all services with Aspire)
dotnet run --project lucia.AppHost

# Run tests
dotnet test

# Start Redis (Docker)
docker run -d --name lucia-redis -p 6379:6379 redis:7-alpine

# Install Ollama models
ollama pull phi3:mini
ollama pull llama3.2:3b
```

## Code Style

### C# 13 / .NET 10

- **One Class Per File**: Each `.cs` file contains exactly one class definition
- **Nullable Reference Types**: Enabled project-wide, explicit nullability annotations
- **File-scoped Namespaces**: Use `namespace lucia.Agents.Orchestration;` format
- **Primary Constructors**: Prefer for simple dependency injection scenarios
- **Required Members**: Use `required` keyword for mandatory properties
- **Async/Await**: Suffix async methods with `Async`, return `ValueTask<T>` for hot paths
- **Logging**: Use compile-time `[LoggerMessage]` attributes for structured logging
- **Telemetry**: Instrument with OpenTelemetry spans, metrics, and structured logs

## Recent Changes
- 001-multi-agent-orchestration: Added C# 13 / .NET 10 + Microsoft.Agents.AI.Workflows 1.0, StackExchange.Redis 2.8.16, OpenTelemetry.NET 1.10

- 001-multi-agent-orchestration: Added C# 13 / .NET 10 + Microsoft.Agents.AI.Workflows 1.0, StackExchange.Redis 2.8.16, OpenTelemetry.NET 1.10

<!-- MANUAL ADDITIONS START -->

## lucia .NET Details

### Product Context
- Mission & Vision: [mission](../.docs/product/mission.md)
- Technical Architecture: [tech-stack](../.docs/product/tech-stack.md)
- Development Roadmap: [roadmap](../.docs/product/roadmap.md)
- Decision History: [decisions](../.docs/product/decisions.md)

### Development Standards
- Code Style: [.docs/standards/code-style.md](../.docs/standards/code-style.md)
- Best Practices: [.docs/standards/best-practices.md](../.docs/standards/best-practices.md)

### Project Management
- Active Specs: [.docs/specs/](../.docs/specs/)
- memory: Memory MCP tool - stores graph data for project relationships and WIP
- todo-md: ToDo MCP Tool - maintains a list of active work items and tasks

## Workflow Instructions

When asked to work on this codebase:

1. First, check `todo-md` for any existing ongoing tasks
2. Then, pull existing context, notes, and details from the `memory` MCP tool
3. Then, follow the appropriate instruction file:
	- Use `sequential-thinking` to follow instructions
	- Use `todo-md` and `memory` MCP tools to maintain context and state
	- For creating and modifying specifications: [create-spec.instructions.md](./instructions/create-spec.instructions.md)
	- For tasks and implementation execution: [execute-tasks.instructions.md](./instructions/execute-tasks.instructions.md)
4. Always, adhere to the standards in the files listed above
5. Always use `context7` and `microsoft.docs` to validate SDKs, libraries, and implementation
6. IMPORTANT - use `todo-md` and `memory` MCP tools to track and maintain tasks

## Important Notes

- Product-specific files in `.docs/product/` override any global standards
- User's specific instructions override (or amend) instructions found in `.docs/specs/...`
- Always adhere to established patterns, code style, and best practices documented above
- Always lookup documentation for 3rd party libraries using the `context7` MCP
- Always lookup documentation for Microsoft related technologies, libraries, and SDKs using `microsoft.docs` MCP
- If coding standards do not exist in the `.docs/standards` directory, create the folder and run the `create_standards` task.

***IMPORTANT***: ONLY ONE CLASS PER FILE!!! NEVER PUT MORE THAN ONE CLASS IN A FILE !!!IMPORTANT!!!

<!-- MANUAL ADDITIONS END -->
