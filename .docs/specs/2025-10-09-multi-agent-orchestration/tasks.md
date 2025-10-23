# Implementation Tasks

This is the implementation task list for the spec detailed in @.docs/specs/2025-01-07-multi-agent-orchestration/spec.md

> Created: 2025-10-09  
> Version: 1.0.0

## Phase 1 – Basic Routing Enablement

**Goal:** Single-agent routing with conditional edges

### Tasks

1. **LUCIA-ORCH-001.1 – RouterExecutor Foundation**
   - [x] Scaffold executor with Microsoft Agent Framework workflow primitives. *(RouterExecutor derives from `ReflectingExecutor<RouterExecutor>` and implements message handler, 2025-10-09)*
   - [x] Implement `ReflectingExecutor<RouterExecutor>` *(see `lucia.Agents/Orchestration/RouterExecutor.cs`, 2025-10-09)*
   - [x] Add `IMessageHandler<ChatMessage, AgentChoiceResult>` *(RouterExecutor implements required interface, 2025-10-09)*
   - [x] Integrate `IChatClient` prompt construction and structured JSON output handling. *(Prompt builder and schema-based parsing complete, 2025-10-09)*
   - [x] Implement retry and fallback semantics for malformed or low-confidence responses. *(MaxAttempts loop, clarification/fallback paths verified by tests, 2025-10-09)*
   - [x] Unit tests covering lighting, music, ambiguous, and failure scenarios. *(2025-10-09: `dotnet test lucia-dotnet.sln` passing)*

2. **LUCIA-ORCH-001.2 – AgentExecutorWrapper Library**
   - [x] Implement `ReflectingExecutor<AgentExecutorWrapper>` *(Wrapper added in `lucia.Agents/Orchestration/AgentExecutorWrapper.cs`, 2025-10-09)*
   - [x] Add `IMessageHandler<ChatMessage, AgentResponse>` *(Wrapper now handles workflow messages returning `AgentResponse`, 2025-10-09)*
   - [x] Create A2A Agent Execution Wrapper that can handle `AIAgent` execution, or A2A remote execution calls using JSON-RPC. *(Local thread reuse plus remote `ITaskManager.SendMessageAsync` integration implemented, 2025-10-09)*
   - [x] Add timeout and cancellation support *(Linked CTS with configurable timeout enforced; timeout path verified by tests, 2025-10-09)*
   - [x] Emit workflow events for observability *(Invoker now records invoked/completed/failed events on `IWorkflowContext`, 2025-10-09)*
   - [x] Handle agent-specific errors gracefully *(Graceful failure responses with logging and error propagation, 2025-10-09)*
   - [x] Unit tests for wrapper functionality *(See `lucia.Tests/AgentExecutorWrapperTests.cs`; suite passing via `dotnet test`, 2025-10-09)*
   - [x] Verify thread state management via automated tests. *(Thread reuse and recreation covered in tests; execution logs confirmed via `RecordingWorkflowContext`, 2025-10-09)*

3. **LUCIA-ORCH-001.3 – Result Aggregator**
   - [x] Implement `ReflectingExecutor<ResultAggregatorExecutor>` *(see `lucia.Agents/Orchestration/ResultAggregatorExecutor.cs`, 2025-10-09)*
   - [x] Add `IMessageHandler<AgentResponse, string>` *(Result aggregator now handles workflow outputs, 2025-10-09)*
   - [x] Format natural language responses *(Success messages prioritized with configurable ordering, 2025-10-09)*
   - [x] Handle error responses *(Partial failures appended with telemetry + events, 2025-10-09)*
   - [x] Unit tests for aggregation *(See `lucia.Tests/ResultAggregatorExecutorTests.cs`; `dotnet test` passing 2025-10-09)*

4. **LUCIA-ORCH-001.4 – LuciaOrchestrator Workflow Assembly**
   - [ ] Remove NotImplementedException placeholders
   - [ ] Initialize RouterExecutor, AgentExecutorWrappers, ResultAggregator
   - [ ] Build workflow with WorkflowBuilder
   - [ ] Add conditional routing edges (switch-case pattern)
   - [ ] Implement ProcessRequestAsync with workflow execution
   - [ ] Add logging and telemetry
   - [ ] Integration tests for full orchestration flow

5. **LUCIA-ORCH-001.5 – Context Lifecycle Management**
   - [ ] Implement OrchestrationContext model
   - [ ] Store context in workflow state
   - [ ] Preserve taskId from A2A protocol
   - [ ] Manage agent threads per conversation
   - [ ] Add context cleanup for old conversations
   - [ ] Unit tests for context preservation

6. **LUCIA-ORCH-001.6 Update Service Registration**
   - [ ] Register RouterExecutor in DI container
   - [ ] Register AgentExecutorWrappers for each agent
   - [ ] Update LuciaOrchestrator initialization
   - [ ] Verify all dependencies resolve correctly

7. **LUCIA-ORCH-001.7 Integration Testing**
   - [ ] Test single-agent routing (light, music)
   - [ ] Test conversation handoffs
   - [ ] Test error scenarios
   - [ ] Test context preservation
   - [ ] Load testing for performance validation

8. **LUCIA-ORCH-001.8 – Redis Task Store Implementation**
   - [ ] Implement `RedisTaskStore` satisfying `ITaskStore`
   - [ ] Define serialization contract for orchestration context payloads (compression optional)
   - [ ] Configure TTL policies, health checks, and connection resiliency (StackExchange.Redis)
   - [ ] Provide integration tests covering persist/resume and concurrent access scenarios

9. **LUCIA-ORCH-001.9 – Task-Aware Host Service & MapA2A Wiring**
   - [ ] Extend AgentHost to hydrate context from Redis before dispatching workflows
   - [ ] Ensure `MapA2A` extension registers the task-aware host without duplicating `.well-known` routes
   - [ ] Add diagnostics endpoints for task inspection and rehydration triggers
   - [ ] Validate graceful shutdown flushes in-flight tasks

10. **LUCIA-ORCH-001.10 – AgentCard Resolver Extension**
    - [ ] Implement extension resolving `AgentCard` to `AIAgent` with local catalog preference
    - [ ] Add fallback path that constructs remote A2A clients when not locally available
    - [ ] Emit telemetry/logging describing resolution source
    - [ ] Cover with unit tests using catalog and mock A2A scenarios

## Phase 2 – Multi-Agent Coordination

**Goal:** Parallel agent execution for multi-domain requests

### Tasks

1. **LUCIA-ORCH-002.1 – Multi-Select Routing Enhancements**
   - [ ] Extend router output model for `additionalAgents` and confidence scoring per agent.
   - [ ] Update routing prompt for multi-agent scenarios
   - [ ] Add logic to identify multi-domain requests
   - [ ] Unit tests for multi-agent selection

2. **LUCIA-ORCH-002.2 – Fan-Out/Fan-In Workflow Patterns**
   - [ ] Add fan-out edges from router to multiple agents
   - [ ] Implement fan-in edge to aggregator
   - [ ] Handle parallel agent execution
   - [ ] Add timeout handling for slow agents
   - [ ] Integration tests for parallel execution

3. **LUCIA-ORCH-002.3 – Advanced Aggregation & Messaging**
   - [ ] Support aggregating multiple agent responses
   - [ ] Implement intelligent response merging
   - [ ] Handle partial failures (some agents succeed, others fail)
   - [ ] Format unified natural language response
   - [ ] Unit tests for multi-response aggregation

4. **LUCIA-ORCH-002.4 – Performance & Resilience Hardening**
   - [ ] Add caching for agent capability descriptions
   - [ ] Optimize router prompt for faster inference
   - [ ] Add parallel execution profiling
   - [ ] Performance regression tests

5. **LUCIA-ORCH-002.5 - Advanced Error Handling**
   - [ ] Graceful degradation for partial failures
   - [ ] Retry logic for transient errors
   - [ ] Circuit breaker for failing agents
   - [ ] Comprehensive error logging

## Phase 3 – Optional Enhancements

1. **LUCIA-ORCH-003.1 – Sequential Routine Support**
   - [ ] Implement sequential execution for complex scenarios
   - [ ] Add "go to bed" automation example
   - [ ] Chain multiple agents in sequence
   - [ ] Integration tests for sequential workflows

2. **LUCIA-ORCH-003.2 – Observability Deep Dive**
   - [ ] Add OpenTelemetry spans for workflow execution
   - [ ] Emit custom metrics for routing decisions
   - [ ] Track agent selection distribution
   - [ ] Dashboard for orchestration insights
   
3. **LUCIA-ORCH-003.3 – Configuration UX & Documentation**
   - [ ] Surface orchestration flags in Home Assistant configuration.
   - [ ] Publish developer guide and update end-user docs.
   - [ ] Enable/disable multi-agent coordination
   - [ ] Adjust routing confidence threshold
   - [ ] Test configuration changes
   - [ ] Document routing prompt customization
   - [ ] Add architecture diagrams
   
---

## Success Criteria

### Functional Requirements

- [ ] ? Router correctly selects appropriate agent for single-domain requests
- [ ] ? Agents execute successfully and return natural language responses
- [ ] ? Context (contextId) preserved across conversation turns
- [ ] ? Conversation handoffs work seamlessly between agents
- [ ] ? Multi-domain requests route to multiple agents (Phase 2)
- [ ] ? Parallel agent execution completes within performance targets
- [ ] ? Error handling provides graceful fallbacks
- [ ] ? Logging and telemetry provide observability
- [ ] ? Redis task store persists and resumes long-running workflows across restarts
- [ ] ? Task-aware host integrates with MapA2A and exposes diagnostics endpoints
- [ ] ? AgentCard resolver selects local agents before remote A2A invocation

### Performance Requirements

- [ ] ? Total response time < 2s (p95)
- [ ] ? Router decision time < 500ms (p95)
- [ ] ? Workflow overhead < 50ms (p95)
- [ ] ? Supports 10+ concurrent requests
- [ ] ? Memory usage < 10MB per request

### User Experience Requirements

- [ ] ? No manual agent selection required
- [ ] ? Natural language commands work intuitively
- [ ] ? Multi-domain commands execute correctly
- [ ] ? Error messages are helpful and actionable
- [ ] ? Response quality matches single-agent performance

### Developer Experience Requirements

- [ ] ? New agents automatically integrate with orchestrator
- [ ] ? Clear documentation for extending orchestration
- [ ] ? Comprehensive test coverage
- [ ] ? Easy to debug routing decisions
- [ ] ? Observable via OpenTelemetry

---
