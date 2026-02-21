# Contract: AgentExecutorWrapper

**Feature Branch**: `001-multi-agent-orchestration`  
**Created**: 2025-10-13  
**Status**: Phase 1 - Design  
**Related**: [data-model.md](../data-model.md), [RouterExecutor.md](./RouterExecutor.md)

## Overview

AgentExecutorWrapper wraps AIAgent instances to provide consistent context propagation, telemetry, timeout handling, error management, and A2A message delivery integration for workflow execution.

## Class Signature

```csharp
namespace lucia.Agents.Orchestration;

public sealed class AgentExecutorWrapper : ReflectingExecutor<AgentExecutorWrapper>,
    IMessageHandler<ChatMessage, AgentResponse>
{
    public AgentExecutorWrapper(
        string agentId,
        IServiceProvider serviceProvider,
        ILogger<AgentExecutorWrapper> logger,
        IOptions<AgentExecutorWrapperOptions> options,
        AIAgent? localAgent = null,
        AgentCard? remoteCard = null,
        TaskManager? taskManager = null,
        TimeProvider? timeProvider = null);
    
    public ValueTask<AgentResponse> HandleAsync(
        ChatMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default);
}
```

## Responsibilities

1. **Timeout Management**: Enforce `DefaultTimeoutMs` with CancellationTokenSource
2. **Error Handling**: Catch exceptions, return structured AgentResponse with error details
3. **Telemetry**: Emit OpenTelemetry spans for agent execution with timing and success metrics
4. **A2A Integration**: Invoke local AIAgent or remote A2A agent via TaskManager
5. **Context Propagation**: Maintain Activity.Current for distributed tracing
6. **Retry Logic**: Implement retry for transient failures (MaxRetries configuration)

## HandleAsync Behavior

1. Check if local AIAgent available → invoke directly
2. If remote AgentCard only → create A2A client via TaskManager
3. Measure execution time with Stopwatch
4. Apply timeout with CancellationTokenSource (linked to parent token)
5. Catch exceptions, log, and return failed AgentResponse
6. Emit telemetry span with result tags

## Configuration

```json
{
  "AgentExecutorWrapper": {
    "DefaultTimeoutMs": 30000,
    "MaxRetries": 2,
    "RetryDelayMs": 1000,
    "EnableTelemetry": true
  }
}
```

## Telemetry

- **Span**: `AgentExecutorWrapper.Execute` with tags: `agent.id`, `agent.type` (local/remote), `agent.success`, `agent.duration_ms`
- **Metrics**: `agent_execution_duration` (histogram), `agent_execution_errors` (counter)

## Testing

- Unit tests with FakeIt Easy mocked AIAgent and TaskManager
- Integration tests with real agents and timeouts
- Error handling tests for network failures and timeouts
