# Data Model: Multi-Agent Orchestration

**Feature Branch**: `001-multi-agent-orchestration`  
**Created**: 2025-10-13  
**Status**: Phase 1 - Design  
**Related**: [spec.md](./spec.md), [research.md](./research.md)

## Overview

This document defines the data models, entity schemas, and type definitions for the multi-agent orchestration feature. All models follow A2A Protocol compliance where applicable and use .NET 10 / C# 13 type conventions.

## Core Entities

### TaskContext (A2A-Compliant Task)

**Purpose**: Represents durable conversation state persisted in Redis, following A2A Protocol Task specification.

**C# Type Definition**:
```csharp
namespace lucia.Agents.Orchestration;

/// <summary>
/// A2A-compliant Task model for durable conversation state.
/// Serialized to Redis with key pattern: task:{id}
/// </summary>
public sealed class TaskContext
{
    /// <summary>
    /// Unique task identifier (A2A: id)
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }
    
    /// <summary>
    /// Client-generated session identifier (A2A: sessionId)
    /// </summary>
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }
    
    /// <summary>
    /// Current task status with state and optional message (A2A: status)
    /// </summary>
    [JsonPropertyName("status")]
    public required TaskStatus Status { get; set; }
    
    /// <summary>
    /// Message history log (A2A: history)
    /// </summary>
    [JsonPropertyName("history")]
    public List<Message>? History { get; set; }
    
    /// <summary>
    /// Collection of artifacts created by agents (A2A: artifacts)
    /// </summary>
    [JsonPropertyName("artifacts")]
    public List<Artifact>? Artifacts { get; set; }
    
    /// <summary>
    /// Extended metadata including agent selections, routing decisions, context variables (A2A: metadata)
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}
```

**JSON Example**:
```json
{
  "id": "task-abc-123",
  "sessionId": "session-xyz-789",
  "status": {
    "state": "working",
    "message": {
      "role": "assistant",
      "content": "Processing your request..."
    },
    "timestamp": "2025-10-13T10:30:00Z"
  },
  "history": [
    {
      "role": "user",
      "content": "Turn on the kitchen lights",
      "timestamp": "2025-10-13T10:29:45Z"
    }
  ],
  "artifacts": [],
  "metadata": {
    "agentSelections": ["light-agent"],
    "location": "kitchen",
    "routingConfidence": 0.95
  }
}
```

**Validation Rules**:
- `Id`: Required, non-empty string, unique per conversation
- `SessionId`: Required, non-empty string, client-generated
- `Status`: Required, valid TaskStatus object
- `History`: Optional, list of Message objects in chronological order
- `Artifacts`: Optional, list of Artifact objects created during task execution
- `Metadata`: Optional, freeform dictionary for extended data

**Redis Storage**:
- Key pattern: `task:{id}`
- TTL: 24 hours (configurable via `OrchestrationOptions:TaskContextTTL`)
- Serialization: System.Text.Json with source generation
- History pruning: Consider automatic pruning at 50+ messages

**Relationships**:
- Referenced by LuciaOrchestrator for conversation continuity
- Updated by AgentExecutorWrapper after each agent execution
- Restored from Redis on process restart for active conversations

---

### TaskStatus (A2A-Compliant Status)

**Purpose**: Represents the current status of a task with state, optional message, and timestamp.

**C# Type Definition**:
```csharp
namespace lucia.Agents.Orchestration;

/// <summary>
/// A2A-compliant task status model.
/// </summary>
public sealed class TaskStatus
{
    /// <summary>
    /// Current state of the task (A2A: state)
    /// </summary>
    [JsonPropertyName("state")]
    public required TaskState State { get; set; }
    
    /// <summary>
    /// Optional status update message provided to client (A2A: message)
    /// </summary>
    [JsonPropertyName("message")]
    public Message? Message { get; set; }
    
    /// <summary>
    /// ISO 8601 datetime value (A2A: timestamp)
    /// </summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
}
```

**JSON Example**:
```json
{
  "state": "completed",
  "message": {
    "role": "assistant",
    "content": "I've turned on the kitchen lights and started playing jazz music."
  },
  "timestamp": "2025-10-13T10:30:15Z"
}
```

**Validation Rules**:
- `State`: Required, valid TaskState enum value
- `Message`: Optional, valid Message object if present
- `Timestamp`: Optional, ISO 8601 datetime string

**State Transitions**:
```
submitted → working → completed
          ↓
          input-required → working → completed
          ↓
          failed
          ↓
          canceled
```

---

### TaskState (A2A-Compliant Enum)

**Purpose**: Enumeration of possible task states per A2A Protocol.

**C# Type Definition**:
```csharp
namespace lucia.Agents.Orchestration;

/// <summary>
/// A2A-compliant task state enumeration.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaskState>))]
public enum TaskState
{
    /// <summary>
    /// Task has been submitted and is queued for processing
    /// </summary>
    [JsonPropertyName("submitted")]
    Submitted,
    
    /// <summary>
    /// Task is actively being processed by an agent
    /// </summary>
    [JsonPropertyName("working")]
    Working,
    
    /// <summary>
    /// Task requires user input to continue
    /// </summary>
    [JsonPropertyName("input-required")]
    InputRequired,
    
    /// <summary>
    /// Task has completed successfully
    /// </summary>
    [JsonPropertyName("completed")]
    Completed,
    
    /// <summary>
    /// Task was canceled by user or system
    /// </summary>
    [JsonPropertyName("canceled")]
    Canceled,
    
    /// <summary>
    /// Task failed due to error
    /// </summary>
    [JsonPropertyName("failed")]
    Failed,
    
    /// <summary>
    /// Task state is unknown or indeterminate
    /// </summary>
    [JsonPropertyName("unknown")]
    Unknown
}
```

**Serialization**: Uses `JsonStringEnumConverter` to serialize as lowercase strings matching A2A Protocol.

---

### AgentChoiceResult

**Purpose**: Output from RouterExecutor containing routing decision with agent ID, confidence score, reasoning, and optional additional agents for multi-agent coordination.

**C# Type Definition**:
```csharp
namespace lucia.Agents.Orchestration;

/// <summary>
/// Routing decision result from RouterExecutor.
/// Used by AgentDispatchExecutor to determine execution order.
/// </summary>
public sealed class AgentChoiceResult
{
    /// <summary>
    /// Primary agent ID selected for handling the request
    /// </summary>
    [JsonPropertyName("agentId")]
    public required string AgentId { get; set; }
    
    /// <summary>
    /// Confidence score (0.0 - 1.0) in the routing decision
    /// </summary>
    [JsonPropertyName("confidence")]
    public required double Confidence { get; set; }
    
    /// <summary>
    /// Explanation of why this agent was selected
    /// </summary>
    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }
    
    /// <summary>
    /// Optional additional agent IDs for multi-agent coordination (executed after primary)
    /// </summary>
    [JsonPropertyName("additionalAgents")]
    public List<string>? AdditionalAgents { get; set; }
}
```

**JSON Example (Single Agent)**:
```json
{
  "agentId": "light-agent",
  "confidence": 0.95,
  "reasoning": "User request explicitly mentions 'lights' which matches light-agent capabilities (lighting control, scenes)."
}
```

**JSON Example (Multi-Agent)**:
```json
{
  "agentId": "light-agent",
  "confidence": 0.88,
  "reasoning": "Request requires both lighting adjustment and music playback.",
  "additionalAgents": ["music-agent"]
}
```

**Validation Rules**:
- `AgentId`: Required, non-empty string, must match available agent in AgentRegistry
- `Confidence`: Required, range [0.0, 1.0]
- `Reasoning`: Optional, human-readable explanation
- `AdditionalAgents`: Optional, list of valid agent IDs (excluding primary `AgentId`)

**Confidence Thresholds**:
- `>= 0.7`: High confidence, route directly to agent
- `< 0.7`: Low confidence, return clarification request (configurable via `RouterExecutorOptions.ConfidenceThreshold`)

**Special Agent IDs** (Fallback/Clarification):
- `clarification-agent`: Returned when confidence is below threshold
- `fallback-agent`: Returned when no suitable agent found or routing fails

---

### AgentResponse

**Purpose**: Structured response from agent execution containing success status, content, error details, and execution time.

**C# Type Definition**:
```csharp
namespace lucia.Agents.Orchestration;

/// <summary>
/// Response from agent execution via AgentExecutorWrapper.
/// Used by ResultAggregatorExecutor to format natural language responses.
/// </summary>
public sealed class AgentResponse
{
    /// <summary>
    /// Agent ID that produced this response
    /// </summary>
    [JsonPropertyName("agentId")]
    public required string AgentId { get; init; }
    
    /// <summary>
    /// Response content (natural language or structured data)
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
    
    /// <summary>
    /// Indicates whether agent execution was successful
    /// </summary>
    [JsonPropertyName("success")]
    public required bool Success { get; set; }
    
    /// <summary>
    /// Error message if execution failed (null if Success is true)
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Execution time in milliseconds
    /// </summary>
    [JsonPropertyName("executionTimeMs")]
    public long ExecutionTimeMs { get; set; }
}
```

**JSON Example (Success)**:
```json
{
  "agentId": "light-agent",
  "content": "I've turned on the kitchen lights to 100% brightness.",
  "success": true,
  "errorMessage": null,
  "executionTimeMs": 1250
}
```

**JSON Example (Failure)**:
```json
{
  "agentId": "music-agent",
  "content": "",
  "success": false,
  "errorMessage": "Music Assistant service is unavailable (HTTP 503)",
  "executionTimeMs": 5000
}
```

**Validation Rules**:
- `AgentId`: Required, non-empty string
- `Content`: Optional, contains response text or data
- `Success`: Required boolean
- `ErrorMessage`: Required if `Success` is false, null otherwise
- `ExecutionTimeMs`: Required, non-negative integer

**Aggregation Rules** (for ResultAggregatorExecutor):
- Single successful response → return `Content` directly
- Multiple successful responses → concatenate with natural language joining
- Any failed response → include error in aggregated message
- All failed responses → return fallback message

---

### WorkflowState

**Purpose**: Execution state for the orchestration workflow including current executor and pending operations.

**C# Type Definition**:
```csharp
namespace lucia.Agents.Orchestration;

/// <summary>
/// Internal workflow execution state (not persisted to Redis).
/// Used by Microsoft Agent Framework during workflow execution.
/// </summary>
public sealed class WorkflowState
{
    /// <summary>
    /// Current executor ID in the workflow
    /// </summary>
    public string? CurrentExecutor { get; set; }
    
    /// <summary>
    /// Pending operations or messages awaiting processing
    /// </summary>
    public List<object>? PendingOperations { get; set; }
    
    /// <summary>
    /// Workflow execution start time
    /// </summary>
    public DateTimeOffset StartTime { get; set; }
    
    /// <summary>
    /// Workflow metadata (correlation IDs, telemetry context)
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}
```

**Usage**: Internal to Microsoft Agent Framework workflow engine. Not exposed via API or persisted to Redis. Used for workflow debugging and telemetry correlation.

---

## Configuration Models

### RouterExecutorOptions

**Purpose**: Configuration options for RouterExecutor behavior.

**C# Type Definition**:
```csharp
namespace lucia.Agents.Orchestration;

/// <summary>
/// Configuration options for RouterExecutor.
/// Bound from appsettings.json "RouterExecutor" section.
/// </summary>
public sealed class RouterExecutorOptions
{
    /// <summary>
    /// Keyed IChatClient instance name for routing decisions
    /// </summary>
    public string ChatClientKey { get; set; } = "ollama-phi3-mini";
    
    /// <summary>
    /// Minimum confidence threshold for routing (0.0 - 1.0)
    /// Below this value, clarification is requested
    /// </summary>
    public double ConfidenceThreshold { get; set; } = 0.7;
    
    /// <summary>
    /// Maximum attempts for LLM routing before fallback
    /// </summary>
    public int MaxAttempts { get; set; } = 3;
    
    /// <summary>
    /// LLM temperature for routing decisions (0.0 - 1.0)
    /// </summary>
    public double? Temperature { get; set; } = 0.3;
    
    /// <summary>
    /// Maximum output tokens for LLM response
    /// </summary>
    public int? MaxOutputTokens { get; set; } = 500;
    
    /// <summary>
    /// System prompt for RouterExecutor LLM
    /// </summary>
    public string? SystemPrompt { get; set; }
    
    /// <summary>
    /// User prompt template (placeholders: {0} = user request, {1} = agent catalog)
    /// </summary>
    public string? UserPromptTemplate { get; set; }
    
    /// <summary>
    /// Agent catalog header text
    /// </summary>
    public string? AgentCatalogHeader { get; set; }
    
    /// <summary>
    /// Include agent capability tags in catalog
    /// </summary>
    public bool IncludeAgentCapabilities { get; set; } = true;
    
    /// <summary>
    /// Include skill examples in agent catalog
    /// </summary>
    public bool IncludeSkillExamples { get; set; } = true;
    
    /// <summary>
    /// Agent ID to use for clarification requests
    /// </summary>
    public string? ClarificationAgentId { get; set; }
    
    /// <summary>
    /// Clarification prompt template
    /// </summary>
    public string? ClarificationPromptTemplate { get; set; }
    
    /// <summary>
    /// Agent ID to use for fallback responses
    /// </summary>
    public string? FallbackAgentId { get; set; }
    
    /// <summary>
    /// Fallback reason template
    /// </summary>
    public string? FallbackReasonTemplate { get; set; }
    
    // Default constants defined in RouterExecutor class
    public const string DefaultClarificationAgentId = "clarification-agent";
    public const string DefaultFallbackAgentId = "fallback-agent";
}
```

**appsettings.json Example**:
```json
{
  "RouterExecutor": {
    "ChatClientKey": "ollama-phi3-mini",
    "ConfidenceThreshold": 0.7,
    "MaxAttempts": 3,
    "Temperature": 0.3,
    "IncludeAgentCapabilities": true,
    "IncludeSkillExamples": true
  }
}
```

---

### AgentExecutorWrapperOptions

**Purpose**: Configuration options for AgentExecutorWrapper behavior.

**C# Type Definition**:
```csharp
namespace lucia.Agents.Orchestration;

/// <summary>
/// Configuration options for AgentExecutorWrapper.
/// Bound from appsettings.json "AgentExecutorWrapper" section.
/// </summary>
public sealed class AgentExecutorWrapperOptions
{
    /// <summary>
    /// Default timeout for agent execution in milliseconds
    /// </summary>
    public int DefaultTimeoutMs { get; set; } = 30000; // 30 seconds
    
    /// <summary>
    /// Maximum retries for transient failures
    /// </summary>
    public int MaxRetries { get; set; } = 2;
    
    /// <summary>
    /// Retry delay in milliseconds
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;
    
    /// <summary>
    /// Enable telemetry for wrapper operations
    /// </summary>
    public bool EnableTelemetry { get; set; } = true;
}
```

---

### ResultAggregatorOptions

**Purpose**: Configuration options for ResultAggregatorExecutor behavior.

**C# Type Definition**:
```csharp
namespace lucia.Agents.Orchestration;

/// <summary>
/// Configuration options for ResultAggregatorExecutor.
/// Bound from appsettings.json "ResultAggregator" section.
/// </summary>
public sealed class ResultAggregatorOptions
{
    /// <summary>
    /// Default fallback message when all agents fail
    /// </summary>
    public string DefaultFallbackMessage { get; set; } = 
        "I encountered an issue processing your request. Please try again.";
    
    /// <summary>
    /// Message template for partial failures
    /// </summary>
    public string PartialFailureTemplate { get; set; } = 
        "{successMessage} However, {failureMessage}";
    
    /// <summary>
    /// Enable natural language joining for multiple responses
    /// </summary>
    public bool EnableNaturalLanguageJoining { get; set; } = true;
}
```

---

## Entity Relationships

```
TaskContext (persisted in Redis)
  ↓ contains
  TaskStatus
    ↓ contains
    TaskState (enum)
  ↓ contains
  Message[] (history)
  ↓ contains
  Artifact[]
  ↓ contains
  Metadata (including agentSelections)

LuciaOrchestrator
  ↓ builds workflow with
  RouterExecutor → AgentDispatchExecutor → ResultAggregatorExecutor
  ↓ uses
  AgentRegistry (query available agents)
  ↓ uses
  TaskManager (A2A task persistence)

RouterExecutor
  ↓ produces
  AgentChoiceResult
  ↓ queries
  AgentRegistry
  ↓ uses
  IChatClient (keyed instance)

AgentDispatchExecutor
  ↓ receives
  AgentChoiceResult
  ↓ resolves
  AgentExecutorWrapper (per agent)
  ↓ produces
  AgentResponse (per agent)

AgentExecutorWrapper
  ↓ wraps
  AIAgent
  ↓ produces
  AgentResponse
  ↓ updates
  TaskContext (via TaskManager)

ResultAggregatorExecutor
  ↓ receives
  AgentResponse[]
  ↓ produces
  string (natural language response)
```

---

## Serialization Strategy

### System.Text.Json Configuration

All models use System.Text.Json with the following options:

```csharp
public static readonly JsonSerializerOptions SerializerOptions = new()
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters =
    {
        new JsonStringEnumConverter() // For TaskState enum
    }
};
```

### Source Generation (Planned)

For optimal performance, use source-generated serialization:

```csharp
[JsonSerializable(typeof(TaskContext))]
[JsonSerializable(typeof(AgentChoiceResult))]
[JsonSerializable(typeof(AgentResponse))]
internal partial class OrchestrationJsonContext : JsonSerializerContext
{
}
```

Usage:
```csharp
var json = JsonSerializer.Serialize(taskContext, OrchestrationJsonContext.Default.TaskContext);
var restored = JsonSerializer.Deserialize(json, OrchestrationJsonContext.Default.TaskContext);
```

---

## Validation Summary

| Entity | Required Fields | Optional Fields | Constraints |
|--------|----------------|-----------------|-------------|
| TaskContext | Id, SessionId, Status | History, Artifacts, Metadata | Id unique, SessionId non-empty |
| TaskStatus | State | Message, Timestamp | Valid TaskState enum |
| AgentChoiceResult | AgentId, Confidence | Reasoning, AdditionalAgents | Confidence ∈ [0.0, 1.0], AgentId exists in registry |
| AgentResponse | AgentId, Success, ExecutionTimeMs | Content, ErrorMessage | ErrorMessage required if Success=false |
| WorkflowState | - | All fields optional | Internal use only |

---

## Next Steps

- [ ] Implement TaskContext serialization/deserialization with Redis
- [ ] Create RouterExecutorOptions binding in Program.cs
- [ ] Implement AgentChoiceResult validation in RouterExecutor
- [ ] Define Message and Artifact types (A2A protocol dependencies)
- [ ] Add JSON schema validation for A2A compliance testing
- [ ] Create unit tests for serialization round-trips
- [ ] Document TaskContext lifecycle and state transitions
