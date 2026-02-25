# lucia .NET Development Guidelines

Auto-generated from all feature plans. Last updated: 2025-10-13

## Active Technologies

- C# 13 / .NET 10 + Microsoft.Agents.AI.Workflows 1.0, StackExchange.Redis 2.8.16, OpenTelemetry.NET 1.10 (001-multi-agent-orchestration)
- Redis 7.x (task persistence with 24h TTL) (001-multi-agent-orchestration)

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

## Important Notes

- Product-specific files in `.docs/product/` override any global standards
- User's specific instructions override (or amend) instructions found in `.docs/specs/...`
- Always adhere to established patterns, code style, and best practices documented above
- Always lookup documentation for 3rd party libraries using the `context7` MCP
- Always lookup documentation for Microsoft related technologies, libraries, and SDKs using `microsoft.docs` MCP
- If coding standards do not exist in the `.docs/standards` directory, create the folder and run the `create_standards` task.

***IMPORTANT***: ONLY ONE CLASS PER FILE!!! NEVER PUT MORE THAN ONE CLASS IN A FILE !!!IMPORTANT!!!

<!-- MANUAL ADDITIONS END -->
