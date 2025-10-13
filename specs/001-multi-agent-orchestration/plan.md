# Implementation Plan: Multi-Agent Orchestration

**Branch**: `001-multi-agent-orchestration` | **Date**: 2025-10-13 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-multi-agent-orchestration/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Implement workflow-based multi-agent orchestration using Microsoft Agent Framework 1.0 to enable automatic agent routing, context-preserving conversation handoffs, multi-domain coordination, and durable task persistence with Redis. This replaces the current manual agent selection model with intelligent routing that analyzes user intent and coordinates multiple specialized agents through workflow conditional edges, while maintaining conversation state across process restarts.

## Technical Context

**Language/Version**: C# 13 with nullable reference types enabled (targeting .NET 10 RC1, upgrading to RTM when available)  
**Primary Dependencies**: 
- Microsoft.Agents.AI.Workflows 1.0 (workflow engine with RouterExecutor, conditional edges)
- Microsoft.SemanticKernel 1.61.0 (LLM integration for routing decisions)
- StackExchange.Redis 2.x (task persistence layer)
- Microsoft.Extensions.AI (abstractions for AI services)
- OpenTelemetry.Exporter.* (tracing, metrics, logging)

**Storage**: Redis 7.x for TaskContext persistence (conversation state, agent selections, message history)  
**Testing**: xUnit + FakeItEasy (unit tests), Aspire.Hosting.Testing (integration tests), test coverage expected >80% for business logic  
**Target Platform**: Linux containers on Docker, Kubernetes deployment, .NET Aspire 9.4.0 orchestration for service discovery  
**Project Type**: Distributed multi-project solution (lucia.AgentHost Web API + lucia.Agents library + lucia.AppHost Aspire orchestrator)  
**Performance Goals**: 
- Routing decisions: <500ms p95
- Single-domain requests: <2s p95 end-to-end
- Concurrent orchestrations: 10+ without degradation
- Routing accuracy: 95%+ for unambiguous requests

**Constraints**: 
- Privacy-first: Local processing default, cloud LLMs optional
- A2A Protocol v0.3.0 compliance (JSON-RPC 2.0, taskId=null limitation)
- Home Assistant API rate limits respected
- OpenTelemetry instrumentation required for all operations
- No PII in logs/traces

**Scale/Scope**: 
- 3-5 specialized agents initially (light, music, climate, security, scene)
- Support 5+ conversation turns with context preservation
- Handle multi-domain coordination (2-3 agents per request)
- Production deployment on home lab Kubernetes with 10+ concurrent users

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### I. One Class Per File ✅ PASS
- **Status**: COMPLIANT - Standard practice, no concerns anticipated
- **Plan**: Each executor (RouterExecutor, AgentExecutorWrapper, ResultAggregatorExecutor), TaskContext, AgentChoiceResult, WorkflowState will be in separate files
- **Verification**: Code review will verify filename matches class name, no multiple public classes per file

### II. Test-First Development (TDD) ✅ PASS
- **Status**: COMPLIANT - TDD workflow mandated
- **Plan**: Write failing tests first for routing logic, context persistence, workflow state transitions before implementation
- **Verification**: PR will include test files with timestamps showing tests written before implementation code

### III. Documentation-First Research ✅ COMPLETE
- **Status**: PASSED - Documentation gathered and analyzed
- **Research Completed**:
  - ✅ Microsoft.Agents.AI.Workflows 1.0 via `microsoft.docs` MCP (conditional edges, switch-case pattern, workflow execution)
  - ✅ StackExchange.Redis via `context7` MCP (connection management, TTL strategy, resilience patterns)
  - ✅ Microsoft.SemanticKernel 1.61.0 via `microsoft.docs` MCP (structured output with function calling)
  - ✅ OpenTelemetry .NET via `context7` MCP (ActivitySource, Meter, compile-time logging)
- **Findings**: Documented in [research.md](./research.md) with code examples and architectural decisions

### IV. Privacy-First Architecture ✅ PASS
- **Status**: COMPLIANT by design
- **Plan**: 
  - TaskContext stored in local Redis (user-controlled infrastructure)
  - LLM routing uses configured provider (local models supported)
  - No PII in telemetry (contextId hashed, content redacted)
  - Home Assistant tokens in secure config only
- **Verification**: Security review will verify no unintended data exfiltration, telemetry redaction correct

### V. Observability & Telemetry ✅ PASS
- **Status**: COMPLIANT - Instrumentation planned
- **Plan**: 
  - OpenTelemetry spans for routing, agent execution, Redis operations
  - Metrics for routing confidence, agent latency, error rates
  - Structured logging with appropriate levels (Error/Warning/Info/Debug)
  - Correlation IDs propagated across distributed calls
- **Verification**: Integration tests will verify span creation, metric emission, log output

### Constitution Gate Status
- **Overall**: ✅ PASSED - All principles compliant, ready for Phase 1 (Design & Contracts)
- **Phase 0 Complete**: Research findings documented with validated code patterns from official sources

## Project Structure

### Documentation (this feature)

```
specs/001-multi-agent-orchestration/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   ├── RouterExecutor.md
│   ├── AgentExecutorWrapper.md
│   ├── ResultAggregatorExecutor.md
│   └── TaskManager.md
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```
lucia.Agents/
├── Orchestration/
│   ├── RouterExecutor.cs           # NEW: Workflow executor for agent routing decisions
│   ├── AgentExecutorWrapper.cs     # NEW: Wrapper for agent execution with context propagation
│   ├── ResultAggregatorExecutor.cs # NEW: Aggregates responses from multiple agents
│   ├── TaskContext.cs              # NEW: Serializable conversation state model
│   ├── AgentChoiceResult.cs        # NEW: Router output (agent ID, confidence, reasoning)
│   ├── WorkflowState.cs            # NEW: Workflow execution state
│   ├── AgentResponse.cs            # NEW: Structured agent execution response
│   └── RoutingDecision.cs          # NEW: Observability record for routing choices
├── Services/
│   ├── LuciaTaskManager.cs         # NEW: Task-aware host service integrating A2A with TaskManager
│   └── AgentCardResolver.cs        # MODIFY: Check local catalog before creating A2A clients
└── Extensions/
    └── OrchestrationExtensions.cs  # NEW: DI registration for orchestration components

lucia.AgentHost/
├── Extensions/
│   └── RedisExtensions.cs          # NEW: Redis configuration and connection management
└── Program.cs                      # MODIFY: Register orchestration services

lucia.AppHost/
└── AppHost.cs                      # MODIFY: Add Redis container resource

lucia.Tests/
├── Orchestration/
│   ├── RouterExecutorTests.cs           # NEW: Unit tests for routing logic
│   ├── AgentExecutorWrapperTests.cs     # NEW: Unit tests for agent wrapper
│   ├── ResultAggregatorExecutorTests.cs # NEW: Unit tests for result aggregation
│   ├── TaskContextTests.cs              # NEW: Unit tests for context serialization
│   └── LuciaTaskManagerTests.cs         # NEW: Integration tests for task management
└── Integration/
    └── MultiAgentOrchestrationTests.cs  # NEW: End-to-end orchestration tests
```

**Structure Decision**: This feature extends the existing distributed multi-project solution architecture. The `lucia.Agents` library receives the core orchestration components (executors, state models) following the existing pattern where agent-related logic lives in the Agents project. The `lucia.AgentHost` Web API is modified to register Redis persistence and orchestration services. Integration tests in `lucia.Tests` verify the complete workflow. This structure maintains separation of concerns: orchestration logic separate from host infrastructure, testable components, and clear boundaries aligned with One Class Per File principle.

## Complexity Tracking

*Fill ONLY if Constitution Check has violations that must be justified*

**No constitutional violations detected.** All principles compliant or pending documentation research gate (Principle III). No complexity justifications required at this stage.
