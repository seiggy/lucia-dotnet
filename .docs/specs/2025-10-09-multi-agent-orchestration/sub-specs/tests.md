# Tests Specification

This is the tests coverage details for the spec detailed in @.docs/specs/2025-01-07-multi-agent-orchestration/spec.md

> Created: 2025-10-09  
> Version: 1.0.0

## Test Coverage

### Unit Tests

**RouterExecutorTests**
- Validate single-agent selection for lighting/music/climate intents with deterministic stubbed LLM responses.
- Ensure JSON schema enforcement retries on malformed model output before falling back to rule-based routing.
- Verify confidence threshold handling triggers clarification prompts when scores fall below configuration.

**AgentExecutorWrapperTests**
- Confirm agent thread state is persisted per `contextId` and recreated on demand without cross-contamination.
- Assert timeout and exception handling converts failures into structured `AgentResponse` payloads while emitting telemetry.
- Check cancellation tokens propagate to underlying agents and return within configured deadlines.

**ResultAggregatorExecutorTests**
- Merge multiple successful responses into an ordered natural language message respecting agent priority.
- Surface partial failure notices while preserving successful agent confirmations.
- Record execution timing metadata and forward it to workflow context for telemetry subscribers.

**OrchestrationContextManagerTests**
- Enforce maximum conversation history length and verify summarization or pruning logic kicks in at limits.
- Guarantee context cleanup removes idle threads after TTL expiration without leaking memory.

**RedisTaskStoreTests**
- Verify context payloads are serialized, compressed (when enabled), and retrievable by `taskId`.
- Confirm TTLs are applied correctly and expired keys are purged via background sweep.
- Assert optimistic concurrency (`etag`) prevents concurrent overwrite when multiple hosts resume the same task.
- Validate health-check probes surface connection issues.

**AgentCardResolverExtensionsTests**
- Ensure local `AgentCatalog` agents are returned without invoking A2A.
- Simulate missing catalog entries to confirm A2A fallback path constructs remote clients correctly.
- Verify logging differentiates between local and remote resolution.

**TaskAwareHostServiceTests**
- Confirm host hydrates context from Redis before dispatching to workflow.
- Ensure new tasks are persisted after execution to support future turns.
- Validate graceful shutdown flushes in-flight tasks and releases Redis locks.

### Integration Tests

**LuciaOrchestratorWorkflowTests**
- Full workflow invocation from JSON-RPC request through to aggregated response using in-memory workflow host.
- Multi-agent fan-out scenario (`lights + music`) validating concurrent execution and aggregated messaging.
- Conversation handoff scenario ensuring second request reuses prior context to infer location/device selections.
- Error-path test simulating agent timeout to confirm graceful degradation and telemetry capture.
- Restart scenario verifying task resumed successfully after host process restart with Redis state intact.

**DiagnosticsEndpointTests**
- `/internal/orchestration/health` returns HTTP 200 with component statuses when dependencies are healthy.
- `/internal/orchestration/routing-log` respects pagination, security requirements, and data contract schema.
- `/internal/orchestration/tasks/{taskId}` returns sanitized context payloads and honors authorization.
- `/internal/orchestration/tasks/{taskId}/rehydrate` enqueues a resume operation and responds with 202.

**MapA2AIntegrationTests**
- Ensure ASP.NET `MapA2A` extension wires the custom task-aware host and Redis store without double-registering `.well-known/agent.json` routes.
- Validate keyed `AIAgent` resolution flows through the new resolver extension and respects DI scopes.

### Mocking Requirements

- **IChatClient (LLM Router):** Use deterministic fakes returning predefined JSON to cover decision branches; include malformed payload variant to exercise retry logic.
- **AgentRegistry & AgentCatalog:** Provide in-memory fixtures representing Light, Music, Climate, and Security agents with capability metadata.
- **AIAgent Implementations:** Substitute lightweight doubles that record invocation arguments, simulate success/failure, and expose configurable delays for timeout testing.
- **OpenTelemetry Exporters:** Replace with test sinks to assert spans, metrics, and logs without requiring external collectors.
- **System Clock:** Inject controllable clock to validate TTL-based context expiry and metric timestamps.
- **Redis Connection Multiplexer:** Provide an in-memory substitute (e.g., `NRedisStack.Testing`) or embedded Redis container capable of simulating TTL expiry, connection drops, and optimistic concurrency errors.
- **TaskManager Queue:** Supply an in-memory implementation to assert enqueue/dequeue semantics when rehydrating tasks.
- **AgentCard Resolver Logging:** Capture logger output to verify local vs remote resolution paths for auditing.
