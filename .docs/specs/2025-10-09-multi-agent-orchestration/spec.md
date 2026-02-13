# Spec Requirements Document

> Spec: Multi-Agent Orchestration with Microsoft Agent Framework Workflows  
> Created: 2025-10-09  
> Status: Planning

## Overview

Deliver an intelligent orchestration layer that automatically selects and coordinates Lucia agents using Microsoft Agent Framework workflows so users can issue natural, multi-domain smart home commands without manual agent switching, while persisting long-running task context in Redis and exposing a resilient host process for A2A message delivery.

## User Stories

### Seamless Command Routing

As a privacy-focused homeowner, I want Lucia to figure out which agent should handle my request so that I can use natural language without configuring agents manually.

Lucia analyzes each request, queries the agent registry for capabilities, and transparently routes execution to the best-fit agent while recording the reasoning for observability.

### Multi-Agent Collaboration

As a power user, I want Lucia to coordinate multiple agents for complex routines so that a single command can trigger lights, music, climate, and security actions together.

The orchestrator fans out requests to the appropriate agent wrappers in parallel, aggregates their responses, and returns a unified confirmation while tracking partial failures.

### Context-Preserving Handoffs

As an automation enthusiast, I want conversations to stay coherent when topics shift so that follow-up commands build on previous context without repeating myself.

Conversation context IDs thread through the workflow, allowing the orchestrator to keep agent-specific state and seamlessly hand off between agents mid-dialog.

Create a ThreadManager that handles context serialization, threadId lookup, and context deserialization by Id. Data should be serialized and stored in Redis for retrieval.

### Durable Task Persistence

As the operator of Lucia, I need orchestrated conversations and long-running workflows to survive process restarts so that multi-turn tasks can resume without data loss.

Implement a Redis-backed `ITaskStore` that serializes orchestration context by `taskId`, supports TTL-based cleanup, and powers a dedicated host service that hydrates tasks when A2A messages arrive.

## Spec Scope

1. **Router Executor** - Implement LLM-backed routing that returns a structured agent selection result with confidence and reasoning metadata.
2. **Workflow-Orchestrated Agent Wrappers** - Create reusable executor wrappers for registered agents that manage context, telemetry, and error handling.
3. **Result Aggregation & Observability** - Aggregate single or multi-agent responses, record execution metrics, and expose tracing hooks for diagnostics.
4. **Durable Task Persistence** - Deliver a Redis `ITaskStore` plus serialization contract for task context, supporting TTL management and operational observability.
5. **Task-Aware Host Process** - Build an agent host that wires A2A delivery into the workflow `TaskManager`, restores task context, and ensures compatibility with `MapA2A` extensions and local agent catalog resolution.
6. **Configuration & Safeguards** - Provide feature flags, performance thresholds, and graceful fallbacks to keep orchestration responsive under load.

## Out of Scope

- Building new domain agents beyond the existing Light, Music, Climate, and Security agents.
- Implementing MagenticOne orchestration patterns before they are available in the Agent Framework.
- Voice-specific UX flows or Home Assistant UI changes beyond configuration toggles.
- Persisting long-term conversational memory beyond the bounded workflow context window.

## Expected Deliverable

1. Workflow-driven orchestrator that passes integration tests for single-domain, multi-domain, and handoff scenarios through the A2A JSON-RPC endpoint.
2. Telemetry and configuration primitives that demonstrate routing confidence metrics, agent execution traces, and bounded recovery paths under simulated failures.
3. Redis-backed durability layer and task-aware host service validated with restart and resume scenarios, plus agent-card resolution that prefers local catalog entries before invoking remote A2A calls.
