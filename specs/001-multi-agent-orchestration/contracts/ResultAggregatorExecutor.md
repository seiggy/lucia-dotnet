# Contract: ResultAggregatorExecutor

**Feature Branch**: `001-multi-agent-orchestration`  
**Created**: 2025-10-13  
**Status**: Phase 1 - Design  
**Related**: [data-model.md](../data-model.md)

## Overview

ResultAggregatorExecutor collects AgentResponse messages from workflow execution and formats a natural language confirmation for the user.

## Class Signature

```csharp
namespace lucia.Agents.Orchestration;

public sealed class ResultAggregatorExecutor : ReflectingExecutor<ResultAggregatorExecutor>,
    IMessageHandler<IEnumerable<AgentResponse>, ChatMessage>
{
    public ResultAggregatorExecutor(
        ILogger<ResultAggregatorExecutor> logger,
        IOptions<ResultAggregatorOptions> options);
    
    public ValueTask<ChatMessage> HandleAsync(
        IEnumerable<AgentResponse> responses,
        IWorkflowContext context,
        CancellationToken cancellationToken = default);
}
```

## Responsibilities

1. **Response Aggregation**: Collect all AgentResponse messages from workflow
2. **Natural Language Formatting**: Generate user-friendly confirmation message
3. **Error Reporting**: Include failed agent details if any failures occurred
4. **Telemetry**: Emit span for aggregation with response count

## HandleAsync Behavior

1. Check if any responses failed → include error summary
2. If all succeeded → format success message listing completed actions
3. If all failed → return fallback error message
4. Return ChatMessage with aggregated content

## Configuration

```json
{
  "ResultAggregator": {
    "FallbackMessage": "I encountered issues processing your request.",
    "SuccessTemplate": "I've completed {0} action(s): {1}",
    "PartialTemplate": "{0} succeeded, but {1} failed: {2}"
  }
}
```

## Example Outputs

**All Success**:
```
I've completed 2 actions: turned on living room lights, set thermostat to 72°F.
```

**Partial Success**:
```
I turned on the lights, but couldn't adjust the thermostat: device unavailable.
```

## Telemetry

- **Span**: `ResultAggregatorExecutor.Aggregate` with tags: `response.count`, `response.failed_count`

## Testing

- Unit tests for all success, all failed, partial success scenarios
- Template formatting tests
- Edge case tests (empty responses, null content)
