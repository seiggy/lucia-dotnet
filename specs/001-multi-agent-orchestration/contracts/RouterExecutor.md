# Contract: RouterExecutor

**Feature Branch**: `001-multi-agent-orchestration`  
**Created**: 2025-10-13  
**Status**: Phase 1 - Design  
**Related**: [data-model.md](../data-model.md), [spec.md](../spec.md)

## Overview

RouterExecutor is a workflow executor that analyzes user requests using a user-configurable keyed IChatClient (local SLM or remote LLM) and produces structured routing decisions (AgentChoiceResult). It queries AgentRegistry dynamically to discover available agents and their capabilities, enabling runtime agent registration/deregistration without workflow rebuild.

## Class Signature

```csharp
namespace lucia.Agents.Orchestration;

/// <summary>
/// Executes routing decisions by invoking an LLM via <see cref="IChatClient"/> and emitting an <see cref="AgentChoiceResult"/>.
/// </summary>
public sealed class RouterExecutor : ReflectingExecutor<RouterExecutor>, 
    IMessageHandler<ChatMessage, AgentChoiceResult>
{
    public const string ExecutorId = "RouterExecutor";
    
    public RouterExecutor(
        IChatClient chatClient,
        AgentRegistry agentRegistry,
        ILogger<RouterExecutor> logger,
        IOptions<RouterExecutorOptions> options);
    
    public ValueTask<AgentChoiceResult> HandleAsync(
        ChatMessage message, 
        IWorkflowContext context, 
        CancellationToken cancellationToken = default);
}
```

## Constructor Dependencies

| Parameter | Type | Purpose |
|-----------|------|---------|
| `chatClient` | `IChatClient` | Keyed service instance for LLM routing (e.g., `ollama-phi3-mini`, `openai-gpt4o-mini`) |
| `agentRegistry` | `AgentRegistry` | Singleton registry for querying available agents dynamically |
| `logger` | `ILogger<RouterExecutor>` | Structured logging for routing decisions and errors |
| `options` | `IOptions<RouterExecutorOptions>` | Configuration including ChatClientKey, ConfidenceThreshold, prompts |

## HandleAsync Method Contract

### Input

- **message**: `ChatMessage`  
  User request message with `ChatRole.User` and text content

- **context**: `IWorkflowContext`  
  Workflow execution context for emitting events and telemetry

- **cancellationToken**: `CancellationToken`  
  Cancellation token for graceful shutdown

### Output

- **Returns**: `ValueTask<AgentChoiceResult>`  
  Routing decision with agent ID, confidence score, reasoning, and optional additional agents

### Behavior

1. **Query AgentRegistry**:
   - Fetch list of available agents via `GetAgentsAsync()`
   - If no agents available → return fallback AgentChoiceResult

2. **Build Agent Catalog**:
   - Format agent list with descriptions and capabilities
   - Include skill examples if configured
   - Use `AgentCatalogHeader` from options

3. **Construct LLM Prompt**:
   - System prompt from `RouterExecutorOptions.SystemPrompt`
   - User prompt with format: `{user request}` + `{agent catalog}`
   - Configure structured JSON output with `AgentChoiceResult` schema

4. **Invoke IChatClient**:
   - Call `GetResponseAsync()` with ChatOptions (temperature, max tokens, response format)
   - Retry up to `MaxAttempts` on malformed JSON
   - Parse response into `AgentChoiceResult`

5. **Validate Result**:
   - Check if selected `AgentId` exists in available agents
   - If unknown agent → return fallback result
   - Normalize `AdditionalAgents` list (remove primary, filter known agents)

6. **Evaluate Confidence**:
   - If `Confidence >= ConfidenceThreshold` → return result
   - If `Confidence < ConfidenceThreshold` → return clarification result

### Error Handling

- **No Agents Available**: Return fallback with reasoning "No registered agents available for routing."
- **Malformed LLM Output**: Retry up to `MaxAttempts`, then return fallback
- **Unknown Agent Selected**: Return fallback with reasoning "Model suggested unknown agent '{agentId}'."
- **LLM Exception**: Log exception, return fallback with error details

## LLM Prompt Template

### System Prompt (Default)

```
You are an intelligent routing assistant for a multi-agent home automation system.
Your job is to analyze user requests and select the most appropriate agent to handle the request based on agent capabilities.

Respond with structured JSON containing:
- agentId: The ID of the best agent to handle this request
- confidence: A number between 0.0 and 1.0 indicating your confidence in this choice
- reasoning: A brief explanation of why you selected this agent
- additionalAgents: (optional) An array of additional agent IDs if multiple agents are needed

Be conservative with confidence scores. If the request is ambiguous or doesn't clearly match any agent's capabilities, use a confidence score below 0.7.
```

### User Prompt Template (Default)

```
Available agents and their capabilities:

{1}

User request: {0}

Which agent should handle this request? Respond with JSON following the AgentChoiceResult schema.
```

### Agent Catalog Format

```
Available agents:

- light-agent: Controls lighting devices and scenes. Capabilities: adjusting brightness, changing colors, creating lighting scenes.
  example: Turn on the kitchen lights
  example: Set bedroom lights to 30%
  example: Activate movie scene

- music-agent: Controls music playback via Music Assistant. Capabilities: play/pause, volume control, track selection, playlist management.
  example: Play some jazz music
  example: Pause the music
  example: Turn up the volume

- climate-agent: Manages HVAC and temperature control. Capabilities: adjusting thermostat, setting target temperature, controlling climate zones.
  example: Set temperature to 72 degrees
  example: Turn on the AC
```

## Structured Output Schema

RouterExecutor uses `ChatResponseFormatJson` with the following JSON schema:

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "properties": {
    "agentId": {
      "type": "string",
      "description": "The ID of the selected agent"
    },
    "confidence": {
      "type": "number",
      "minimum": 0.0,
      "maximum": 1.0,
      "description": "Confidence score for the routing decision"
    },
    "reasoning": {
      "type": "string",
      "description": "Explanation for agent selection"
    },
    "additionalAgents": {
      "type": "array",
      "items": { "type": "string" },
      "description": "Optional additional agents for multi-agent coordination"
    }
  },
  "required": ["agentId", "confidence"],
  "additionalProperties": false
}
```

## Configuration Example

```json
{
  "ConnectionStrings": {
    "ollama-phi3-mini": "Endpoint=http://localhost:11434;Model=phi3:mini;Provider=ollama",
    "openai-gpt4o-mini": "Endpoint=https://api.openai.com;AccessKey=sk-...;Model=gpt-4o-mini;Provider=openai"
  },
  "RouterExecutor": {
    "ChatClientKey": "ollama-phi3-mini",
    "ConfidenceThreshold": 0.7,
    "MaxAttempts": 3,
    "Temperature": 0.3,
    "MaxOutputTokens": 500,
    "IncludeAgentCapabilities": true,
    "IncludeSkillExamples": true,
    "ClarificationAgentId": "clarification-agent",
    "FallbackAgentId": "fallback-agent"
  }
}
```

## DI Registration

```csharp
// ServiceCollectionExtensions.AddLuciaAgents
public static void AddLuciaAgents(this IHostApplicationBuilder builder)
{
    // Register keyed IChatClient
    var routerClientKey = builder.Configuration["RouterExecutor:ChatClientKey"] 
        ?? "ollama-phi3-mini";
    builder.AddKeyedChatClient(routerClientKey);
    
    // Register RouterExecutorOptions
    builder.Services.AddOptions<RouterExecutorOptions>()
        .Bind(builder.Configuration.GetSection("RouterExecutor"))
        .ValidateDataAnnotations()
        .ValidateOnStart();
    
    // Register RouterExecutor (resolved from keyed IChatClient)
    builder.Services.AddSingleton<RouterExecutor>(sp =>
    {
        var chatClient = sp.GetRequiredKeyedService<IChatClient>(routerClientKey);
        var registry = sp.GetRequiredService<AgentRegistry>();
        var logger = sp.GetRequiredService<ILogger<RouterExecutor>>();
        var options = sp.GetRequiredService<IOptions<RouterExecutorOptions>>();
        return new RouterExecutor(chatClient, registry, logger, options);
    });
}
```

## Telemetry & Observability

### OpenTelemetry Spans

```csharp
using var activity = ActivitySource.StartActivity(
    "RouterExecutor.Route",
    ActivityKind.Internal,
    tags: new ActivityTagsCollection
    {
        { "messaging.operation", "route" },
        { "messaging.message.type", "ChatMessage" },
        { "agent.available_count", availableAgents.Count }
    }
);

// After routing decision
activity?.SetTag("agent.selected", result.AgentId);
activity?.SetTag("agent.confidence", result.Confidence);
activity?.SetTag("agent.additional_count", result.AdditionalAgents?.Count ?? 0);
activity?.SetStatus(ActivityStatusCode.Ok);
```

### Metrics

```csharp
// Counter: routing decisions
RoutingDecisions.Add(1,
    new("agent.id", agentId),
    new("confidence.bucket", GetConfidenceBucket(confidence)));

// Histogram: routing latency
RoutingLatency.Record(durationMs,
    new("agent.id", agentId));

// Histogram: confidence distribution
RoutingConfidence.Record(confidence,
    new("agent.id", agentId));
```

### Structured Logging

```csharp
[LoggerMessage(
    EventId = 1001,
    Level = LogLevel.Information,
    Message = "Routing decision: selected agent '{AgentId}' with confidence {Confidence:F2}")]
public static partial void LogRoutingDecision(
    this ILogger logger, string agentId, double confidence);

[LoggerMessage(
    EventId = 1002,
    Level = LogLevel.Warning,
    Message = "Low confidence routing: agent '{AgentId}' confidence {Confidence:F2}, returning clarification")]
public static partial void LogLowConfidenceRouting(
    this ILogger logger, string agentId, double confidence);

[LoggerMessage(
    EventId = 1003,
    Level = LogLevel.Warning,
    Message = "RouterExecutor invoked with no registered agents; falling back")]
public static partial void LogNoAgentsAvailable(this ILogger logger);
```

## Testing Strategy

### Unit Tests

```csharp
public class RouterExecutorTests
{
    [Fact]
    public async Task HandleAsync_WithClearRequest_ReturnsHighConfidenceResult()
    {
        // Arrange: Mock IChatClient to return high confidence result
        // Act: Call HandleAsync with "Turn on lights"
        // Assert: AgentId is "light-agent", Confidence >= 0.9
    }
    
    [Fact]
    public async Task HandleAsync_WithAmbiguousRequest_ReturnsClarification()
    {
        // Arrange: Mock IChatClient to return low confidence result
        // Act: Call HandleAsync with ambiguous request
        // Assert: AgentId is "clarification-agent"
    }
    
    [Fact]
    public async Task HandleAsync_WithNoAgents_ReturnsFallback()
    {
        // Arrange: Mock AgentRegistry to return empty list
        // Act: Call HandleAsync
        // Assert: AgentId is "fallback-agent"
    }
    
    [Fact]
    public async Task HandleAsync_WithMalformedLLMOutput_RetriesAndFallsBack()
    {
        // Arrange: Mock IChatClient to return invalid JSON
        // Act: Call HandleAsync
        // Assert: Method retries MaxAttempts times, then returns fallback
    }
}
```

### Integration Tests

```csharp
public class RouterExecutorIntegrationTests
{
    [Fact]
    public async Task RouterExecutor_WithRealOllama_RoutesCorrectly()
    {
        // Arrange: Real Ollama connection, real AgentRegistry
        // Act: Send various user requests
        // Assert: Correct agents selected with reasonable confidence
    }
}
```

## Performance Requirements

- **Routing Latency**: <500ms p95 (SC-010)
- **LLM Call Timeout**: 5000ms default
- **Memory Usage**: <10MB per routing operation
- **Concurrent Requests**: Support 10+ simultaneous routing decisions

## Security Considerations

- **API Keys**: Never log IChatClient access keys
- **PII**: Redact user message content from telemetry (only log length, not content)
- **Prompt Injection**: Validate agent catalog doesn't contain user-controlled data
- **Rate Limiting**: Implement rate limiting for LLM calls to prevent abuse

## Future Enhancements

- [ ] Hybrid routing: SLM primary with LLM fallback for low confidence
- [ ] Routing history analysis: Learn from past decisions
- [ ] Agent capability scoring: Weight agents by past performance
- [ ] Multi-turn context: Use conversation history in routing decisions
- [ ] A/B testing: Compare different prompts and models
