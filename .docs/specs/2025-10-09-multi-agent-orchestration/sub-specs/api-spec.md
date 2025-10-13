# API Specification

This is the API specification for the spec detailed in @.docs/specs/2025-01-07-multi-agent-orchestration/spec.md

> Created: 2025-10-09  
> Version: 1.0.0

## Routes

### JSON-RPC Endpoint: `POST /a2a/lucia-orchestrator`
- **Protocol:** JSON-RPC 2.0 over HTTPS
- **Method:** `message/send`
- **Parameters:**
  - `message.kind` (string, required) – must be `"message"`.
  - `message.role` (string, required) – caller role, typically `"user"` or `"assistant"` for follow-ups.
  - `message.parts` (array, required) – ordered message parts; the primary text request is the first `{"kind":"text","text":"..."}` entry.
  - `message.messageId` (UUID, required) – unique identifier for the inbound request.
  - `message.contextId` (UUID, required) – conversation/thread identifier used for context preservation across turns.
  - `message.taskId` (UUID, optional) – downstream workflow correlation token. When supplied, the host service loads persisted context from Redis before invoking the workflow.
- **Success Response:**
  - `result.kind` = `"message"`
  - `result.role` = `"assistant"`
  - `result.parts[0].text` contains the aggregated natural language reply.
  - `result.metadata.agents_used` lists the agents invoked in the workflow (ordered).
  - `result.metadata.execution_time_ms` captures total orchestrator execution latency.
  - `result.metadata.task_state` indicates whether the request was `"fresh"`, `"resumed"`, or `"completed"` for long-running workflows.
- **Error Codes:**
  - `ROUTER_FAILURE` – Router executor failed after retry; response contains fallback guidance.
  - `AGENT_TIMEOUT` – One or more agents exceeded configured deadline; partial results may be returned.
  - `WORKFLOW_ERROR` – Unhandled exception inside the workflow runtime; caller advised to retry.

### Health + Diagnostics
- **Route:** `GET /internal/orchestration/health`
  - Returns consolidated status for router model readiness, agent registry availability, and telemetry exporters.
- **Route:** `GET /internal/orchestration/routing-log`
  - Queryable feed (paginated) of recent routing decisions including agent selection, confidence, and reasoning text for debugging.
- **Route:** `GET /internal/orchestration/tasks/{taskId}`
  - Returns persisted orchestration context stored in Redis for the specified `taskId`, redacting sensitive data. Requires internal authentication and is intended for operational troubleshooting.
- **Route:** `POST /internal/orchestration/tasks/{taskId}/rehydrate`
  - Forces the host service to reload task state from Redis and enqueue it for processing. Used when recovering from host failover scenarios.

## Data

- **AgentChoiceResult**
  - `agentId` (string, required) – Primary agent to route to.
  - `additionalAgents` (array<string>, optional) – Secondary agents for parallel execution.
  - `reasoning` (string, required) – LLM justification for observability.
  - `confidence` (double, required) – Value between 0.0 and 1.0; sub-threshold results trigger clarification or fallback.
- **AgentResponse**
  - `agentId` (string, required) – Executor wrapper identifier.
  - `content` (string, required) – Natural language response payload.
  - `success` (bool, required) – Indicates whether the agent completed successfully.
  - `errorMessage` (string, optional) – Error context for partial failures.
  - `executionTimeMs` (long, required) – Agent execution latency captured for metrics.
- **TaskPersistenceRecord**
  - `taskId` (UUID, required) – Identifier supplied by A2A/TaskManager.
  - `context` (object, required) – Serialized orchestration context payload using System.Text.Json.
  - `expiresAtUtc` (DateTime, required) – Expiration timestamp enforced by Redis TTL.
  - `etag` (string, optional) – Concurrency token used to prevent lost updates when multiple hosts resume the same task.
- **AgentResolutionResult**
  - `agentName` (string, required) – Logical agent identifier.
  - `source` (string, required) – Either `"local"` (from AgentCatalog) or `"a2a"` (remote invocation).
  - `endpoint` (Uri, optional) – Resolved endpoint for A2A calls when `source` is `"a2a"`.
  - `card` (`AgentCard`, required) – Card returned for downstream use.
- **OrchestrationError**
  - `errorCode` (string, required) – Machine-readable failure classification.
  - `userMessage` (string, required) – Friendly guidance returned to the caller.
  - `technicalDetails` (string, optional) – Diagnostic payload stored in telemetry.
  - `suggestedAction` (string, optional) – Follow-up actions for remediation.

## Controllers

- **`LuciaOrchestratorController` (JSON-RPC host)**
  - Resolves `LuciaOrchestrator` workflow and runs `ProcessRequestAsync`.
  - Normalizes inbound Home Assistant payloads, enforces schema validation, and writes success/error envelopes.
  - Emits structured logs containing routing confidence, agents used, and request duration.
- **`OrchestrationDiagnosticsController` (REST)**
  - Hosts health probe and routing log endpoints under `/internal/orchestration/*`.
  - Requires internal authentication (API key or mTLS) and is excluded from public exposure.
  - Surfaces aggregated metrics to facilitate operations dashboards.
- **`TaskHostController` (REST)**
  - Exposes task inspection and rehydration routes, delegating to the Redis `ITaskStore` and TaskManager queue.
  - Secured via internal authentication and rate-limited to prevent misuse.
  - Publishes structured events whenever a task is resumed or deleted for observability.
- **`AgentResolutionController` (Optional REST)**
  - Provides internal diagnostics showing how the resolver handled specific agent cards (local versus A2A).
  - Useful for debugging agent catalog mismatches during deployments.

### Data Models

#### AgentChoiceResult
```csharp
/// <summary>
/// Result of the router's agent selection decision
/// </summary>
public sealed class AgentChoiceResult
{
    /// <summary>
    /// ID of the selected agent (matches agent name in registry)
    /// </summary>
    [JsonPropertyName("agentId")]
    public required string AgentId { get; set; }
    
    /// <summary>
    /// Explanation of why this agent was selected (for debugging/logging)
    /// </summary>
    [JsonPropertyName("reasoning")]
    public required string Reasoning { get; set; }
    
    /// <summary>
    /// Optional: Additional agents for parallel execution (Phase 2)
    /// </summary>
    [JsonPropertyName("additionalAgents")]
    public List<string>? AdditionalAgents { get; set; }
    
    /// <summary>
    /// Confidence score (0.0 - 1.0) for the selection
    /// </summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; } = 1.0;
}
```

#### AgentResponse
```csharp
/// <summary>
/// Response from an agent executor wrapper
/// </summary>
public sealed class AgentResponse
{
    /// <summary>
    /// ID of the agent that generated this response
    /// </summary>
    public required string AgentId { get; init; }
    
    /// <summary>
    /// The agent's response content
    /// </summary>
    public required string Content { get; init; }
    
    /// <summary>
    /// Whether the agent successfully processed the request
    /// </summary>
    public required bool Success { get; init; }
    
    /// <summary>
    /// Optional error message if Success is false
    /// </summary>
    public string? ErrorMessage { get; init; }
    
    /// <summary>
    /// Execution duration in milliseconds
    /// </summary>
    public long ExecutionTimeMs { get; init; }
}
```

#### OrchestrationContext
```csharp
/// <summary>
/// Context preserved across workflow execution
/// </summary>
public sealed class OrchestrationContext
{
    /// <summary>
    /// Conversation ID for threading (from A2A contextId)
    /// </summary>
    public required string ConversationId { get; init; }
    
    /// <summary>
    /// Agent threads for context preservation
    /// </summary>
    public Dictionary<string, AgentThread> AgentThreads { get; init; } = new();
    
    /// <summary>
    /// Previous agent that handled this conversation (for handoffs)
    /// </summary>
    public string? PreviousAgentId { get; set; }
    
    /// <summary>
    /// Conversation history (last N turns)
    /// </summary>
    public List<ChatMessage> History { get; init; } = new();
}

#### TaskPersistenceRecord
```csharp
/// <summary>
/// Redis persisted record for a long-running orchestration.
/// </summary>
public sealed record TaskPersistenceRecord
{
  public required Guid TaskId { get; init; }

  public required byte[] ContextPayload { get; init; }

  public required DateTimeOffset ExpiresAtUtc { get; init; }

  public string? ETag { get; init; }
}
```

#### AgentResolutionResult
```csharp
/// <summary>
/// Result of resolving an agent card to an <see cref="AIAgent"/>.
/// </summary>
public sealed record AgentResolutionResult
{
  public required string AgentName { get; init; }

  public required string Source { get; init; }

  public Uri? Endpoint { get; init; }

  public required AgentCard Card { get; init; }
}
```
```

### API Contracts

#### JSON-RPC Request (Input)
```json
{
  "jsonrpc": "2.0",
  "method": "message/send",
  "params": {
    "message": {
      "kind": "message",
      "role": "user",
      "parts": [
        {
          "kind": "text",
          "text": "Turn on the kitchen lights and play jazz music"
        }
      ],
      "messageId": "550e8400-e29b-41d4-a716-446655440000",
      "contextId": "550e8400-e29b-41d4-a716-446655440001",
      "taskId": null
    }
  },
  "id": 1
}
```

#### JSON-RPC Response (Output)
```json
{
  "jsonrpc": "2.0",
  "result": {
    "kind": "message",
    "role": "assistant",
    "parts": [
      {
        "kind": "text",
        "text": "I've turned on the kitchen lights and started playing jazz music."
      }
    ],
    "messageId": "550e8400-e29b-41d4-a716-446655440002",
    "contextId": "550e8400-e29b-41d4-a716-446655440001",
    "taskId": null,
    "metadata": {
      "agents_used": ["light-agent", "music-agent"],
      "execution_time_ms": 2347,
      "task_state": "resumed"
    }
  },
  "id": 1
}
```

### Configuration

#### appsettings.json
```json
{
  "Orchestration": {
    "RouterModel": "gpt-4o-mini",
    "EnableMultiAgentCoordination": true,
    "MaxParallelAgents": 3,
    "RoutingConfidenceThreshold": 0.7,
    "EnableContextPreservation": true,
    "MaxConversationHistory": 10
  },
  "OpenTelemetry": {
    "EnableWorkflowTracing": true,
    "EnableAgentMetrics": true
  }
}
```

## Purpose

- Document JSON-RPC contracts so that Home Assistant and other clients can integrate without reverse engineering the orchestrator behavior.
- Establish diagnostic endpoints and standard data shapes for routing insights, enabling observability tooling and regression detection.
- Provide engineers with authoritative references when implementing or extending workflow executors and their API surface.

