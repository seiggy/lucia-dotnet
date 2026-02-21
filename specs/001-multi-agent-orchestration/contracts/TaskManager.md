# Contract: TaskManager Integration

**Feature Branch**: `001-multi-agent-orchestration`  
**Created**: 2025-10-13  
**Status**: Phase 1 - Design  
**Related**: [data-model.md](../data-model.md)

## Overview

TaskManager integration enables persistence of A2A-compliant TaskContext in Redis, supporting task history, resume-after-restart, and cross-session context preservation.

## Redis Schema

**Key Pattern**: `lucia:task:{taskId}`  
**TTL**: 24 hours (86400 seconds)  
**Serialization**: JSON with System.Text.Json

## TaskContext Persistence

```csharp
// Save task
await redis.StringSetAsync(
    $"lucia:task:{taskId}",
    JsonSerializer.Serialize(taskContext, jsonOptions),
    TimeSpan.FromHours(24));

// Load task
var json = await redis.StringGetAsync($"lucia:task:{taskId}");
var taskContext = JsonSerializer.Deserialize<TaskContext>(json, jsonOptions);

// Delete task (cleanup)
await redis.KeyDeleteAsync($"lucia:task:{taskId}");
```

## TaskManager Usage

```csharp
// In LuciaOrchestrator.HandleAsync
var task = await taskManager.GetTaskAsync(taskId, cancellationToken);
if (task == null)
{
    task = new TaskContext
    {
        Id = taskId,
        SessionId = sessionId,
        Status = new TaskStatus { State = TaskState.InProgress },
        History = [/* conversation messages */],
        Artifacts = [],
        Metadata = new Dictionary<string, object>
        {
            ["workflowId"] = workflowId,
            ["userId"] = userId
        }
    };
}

// Execute workflow...

// Update task after workflow completion
task.Status.State = TaskState.Completed;
task.Status.CompletedAt = DateTimeOffset.UtcNow;
await taskManager.UpdateTaskAsync(task, cancellationToken);
```

## Configuration

```json
{
  "Redis": {
    "ConnectionString": "localhost:6379",
    "InstanceName": "lucia:",
    "TaskTtlHours": 24,
    "ConnectRetryCount": 3,
    "ConnectTimeout": 5000
  }
}
```

## A2A Compliance

TaskContext structure follows A2A Task schema:

- `id` (string): Unique task identifier
- `sessionId` (string): Session grouping
- `status` (TaskStatus): Current state (InProgress, Completed, Failed, Cancelled)
- `history` (Message[]): Conversation messages
- `artifacts` (Artifact[]): Generated files, structured data
- `metadata` (Dictionary): Workflow tracking, user context

## Telemetry

- **Span**: `TaskManager.SaveTask`, `TaskManager.LoadTask` with tags: `task.id`, `task.state`
- **Metrics**: `task_save_duration`, `task_load_duration`, `task_cache_hits`, `task_cache_misses`

## Testing

- Integration tests with Redis container (Testcontainers)
- Serialization round-trip tests
- TTL expiration tests
- Connection retry tests
