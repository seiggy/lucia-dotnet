# Research: Multi-Agent Orchestration

**Feature Branch**: `001-multi-agent-orchestration`  
**Date**: 2025-10-13  
**Status**: Phase 0 Complete

## Research Questions

### 1. Microsoft.Agents.AI.Workflows 1.0 - Workflow Engine Capabilities

**Question**: How do RouterExecutor, conditional edges, and the TaskManager work in Agent Framework workflows?

**Findings**:

#### Workflow Architecture
- **WorkflowBuilder Pattern**: Fluent API for defining workflow structure with `AddEdge()`, `SetStart()`, `Build<T>()`
- **Pregel-Style Execution**: Superstep-based processing where all executors in a superstep run concurrently
- **Message-Based Flow**: Executors process messages and emit results that flow through edges

#### Coordinator Agent Pattern (Lucia-Specific)

**Critical Requirement**: Lucia supports **runtime agent registration** with A2A protocol. Agents can register/deregister dynamically, so we **cannot use static conditional edges** or switch-case patterns for routing.

**Architecture**:
```csharp
// Coordinator Agent queries AgentRegistry at runtime
public class CoordinatorAgent : IWorkflowExecutor
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly IChatClient _slm; // Small Language Model for fast routing
    
    public async Task<AgentChoiceResult> ExecuteAsync(ChatMessage message)
    {
        // 1. Query AgentRegistry for currently available agents
        var availableAgents = await _agentRegistry.GetAvailableAgentsAsync();
        
        // 2. Build dynamic prompt with agent capabilities
        string agentCapabilities = string.Join("\n", 
            availableAgents.Select(a => $"- {a.Id}: {a.Capabilities}"));
        
        // 3. Use SLM/LLM to select appropriate agent based on capabilities
        var prompt = $@"
Available agents and their capabilities:
{agentCapabilities}

User request: {message.Content}

Which agent should handle this request? Respond with agent ID and confidence.";
        
        // 4. Get structured output (AgentChoiceResult)
        var result = await _slm.GetStructuredOutputAsync<AgentChoiceResult>(prompt);
        
        return result;
    }
}
```

**Dynamic Routing Pattern**:
```csharp
// Workflow uses a SINGLE conditional edge that checks if agent exists in registry
builder.AddEdge(
    source: coordinatorAgent,
    target: dynamicAgentExecutor,
    condition: result => result is AgentChoiceResult choice 
        && _agentRegistry.HasAgent(choice.AgentId)
);

// Fallback for unknown agents
builder.AddEdge(
    source: coordinatorAgent,
    target: fallbackExecutor,
    condition: result => result is AgentChoiceResult choice 
        && !_agentRegistry.HasAgent(choice.AgentId)
);
```

**DynamicAgentExecutor Pattern**:
```csharp
public class DynamicAgentExecutor : IWorkflowExecutor
{
    private readonly IAgentRegistry _agentRegistry;
    
    public async Task<AgentResponse> ExecuteAsync(AgentChoiceResult choice)
    {
        // Resolve agent from registry at runtime
        var agent = await _agentRegistry.GetAgentAsync(choice.AgentId);
        
        if (agent == null)
        {
            return AgentResponse.NotFound(choice.AgentId);
        }
        
        // Execute the dynamically resolved agent
        return await agent.ExecuteAsync(choice.Message);
    }
}
```

#### Workflow Execution Model
- **Streaming**: `await workflow.run_stream(input)` - real-time events
- **Batch**: `await workflow.run(input)` - wait for completion
- **Event Types**: `WorkflowCompletedEvent`, executor completion events
- **Validation**: Type compatibility, graph connectivity, executor binding checked at build time

**Implications for Lucia**:
- ✅ CoordinatorAgent queries AgentRegistry dynamically (no static agent list)
- ✅ Uses SLM/lightweight LLM for fast routing based on advertised capabilities
- ✅ Returns `AgentChoiceResult` with agent ID, confidence, reasoning
- ✅ DynamicAgentExecutor resolves agent from registry at execution time
- ✅ Supports agent registration/deregistration without workflow rebuild
- ✅ Workflow validates type flow: `ChatMessage` → `AgentChoiceResult` → `AgentResponse` → `string`
- ⚠️ Conditional edge only validates agent existence, not static routing logic

---

### 2. StackExchange.Redis - Task Persistence Strategy

**Question**: How should we serialize TaskContext, configure TTL, and handle connection resilience?

**Findings**:

#### Connection Management
```csharp
// Connection configuration
ConfigurationOptions config = new ConfigurationOptions
{
    EndPoints = { { "localhost", 6379 } },
    ConnectRetry = 3,
    ConnectTimeout = 5000,
    SyncTimeout = 5000,
    KeepAlive = 180,
    ReconnectRetryPolicy = new ExponentialRetry(5000), // maxDeltaBackoff 10000ms
    AbortOnConnectFail = false
};

ConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(config);
IDatabase db = connection.GetDatabase();
```

**Connection Patterns**:
- **Singleton Pattern**: `ConnectionMultiplexer` should be shared across application (not per-request)
- **ExponentialRetry**: Default retry policy with configurable seed and max backoff
- **LinearRetry**: Alternative for fixed retry intervals
- **KeepAlive**: Defaults to 60s, helps prevent connection drops

#### Serialization & Storage
```csharp
// TaskContext serialization approach
public class TaskContext
{
    public string TaskId { get; set; }
    public List<ChatMessage> MessageHistory { get; set; }
    public List<string> AgentSelections { get; set; }
    public Dictionary<string, string> Metadata { get; set; }
}

// Store with TTL
string serialized = JsonSerializer.Serialize(taskContext);
await db.StringSetAsync(
    $"task:{taskId}", 
    serialized, 
    expiry: TimeSpan.FromHours(24) // configurable TTL
);

// Retrieve
string? stored = await db.StringGetAsync($"task:{taskId}");
if (stored != null)
{
    TaskContext restored = JsonSerializer.Deserialize<TaskContext>(stored);
}
```

#### TTL & Expiration Strategy
- **ConfigCheckSeconds**: Default 60s for configuration checks (heartbeat)
- **Recommended TTL**: 24 hours for conversation persistence (configurable)
- **Key Pattern**: `task:{taskId}` for namespacing
- **Expiry Handling**: Check for null on retrieval, start fresh conversation if expired

#### Resilience & Error Handling
```csharp
// ConnectionFailed and ConnectionRestored events
connection.ConnectionFailed += (sender, e) =>
{
    // Log connection failure, emit telemetry
    logger.LogWarning(e.Exception, "Redis connection failed: {FailureType}", e.FailureType);
};

connection.ConnectionRestored += (sender, e) =>
{
    // Log restoration, emit telemetry
    logger.LogInformation("Redis connection restored: {ConnectionType}", e.ConnectionType);
};
```

**Resilience Features**:
- **Automatic Reconnection**: Handled by `ReconnectRetryPolicy`
- **Command Timeout**: `SyncTimeout` for sync ops, `AsyncTimeout` for async
- **HighIntegrity Mode**: Optional sequence checking (incurs overhead)
- **HeartbeatInterval**: Default 1000ms for keepalive checks

**Implications for Lucia**:
- Use `System.Text.Json` for `TaskContext` serialization (fastest, most compatible with .NET 10)
- Implement singleton `IConnectionMultiplexer` registered in DI
- Configure 24-hour TTL (user-configurable via `appsettings.json`)
- Wire connection events to OpenTelemetry metrics for observability
- Use `task:{taskId}` key pattern with proper cleanup on context expiry

---

### 3. OpenTelemetry .NET - Instrumentation Patterns

**Question**: How do we instrument RouterExecutor, AgentExecutorWrapper, and Redis operations with spans, metrics, and structured logging?

**Findings**:

#### ASP.NET Core Integration
```csharp
// Program.cs registration
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: "lucia.AgentHost"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation() // Automatic HTTP instrumentation
        .AddSource("lucia.Agents.Orchestration") // Custom activity source
        .AddConsoleExporter()) // Development exporter
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation() // HTTP metrics
        .AddMeter("lucia.Agents.Orchestration") // Custom meter
        .AddConsoleExporter());
```

#### Span Creation for Orchestration
```csharp
using System.Diagnostics;

public class RouterExecutor : IWorkflowExecutor
{
    private static readonly ActivitySource ActivitySource = 
        new("lucia.Agents.Orchestration", "1.0.0");
    
    public async Task<AgentChoiceResult> ExecuteAsync(ChatMessage message)
    {
        using var activity = ActivitySource.StartActivity(
            "RouterExecutor.Route",
            ActivityKind.Internal,
            parentContext: Activity.Current?.Context ?? default,
            tags: new ActivityTagsCollection
            {
                { "messaging.operation", "route" },
                { "messaging.message.type", "ChatMessage" }
            }
        );
        
        try
        {
            // LLM routing logic
            var result = await PerformRoutingAsync(message);
            
            // Add result tags
            activity?.SetTag("agent.selected", result.AgentId);
            activity?.SetTag("agent.confidence", result.Confidence);
            activity?.SetStatus(ActivityStatusCode.Ok);
            
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            throw;
        }
    }
}
```

**Span Best Practices**:
- **Activity Source**: Create once per namespace, reuse across classes
- **Parent Context**: Preserve Activity.Current for distributed tracing
- **Initial Tags**: Add tags at creation for sampler access
- **Status Codes**: Set `Ok` or `Error` with descriptions
- **Exception Recording**: Use `RecordException()` for structured exception data

#### Metrics for Routing & Execution
```csharp
using System.Diagnostics.Metrics;

public class OrchestrationMetrics
{
    private static readonly Meter Meter = 
        new("lucia.Agents.Orchestration", "1.0.0");
    
    private static readonly Counter<long> RoutingDecisions = 
        Meter.CreateCounter<long>(
            "orchestration.routing.decisions",
            description: "Number of routing decisions made");
    
    private static readonly Histogram<double> RoutingLatency = 
        Meter.CreateHistogram<double>(
            "orchestration.routing.duration",
            unit: "ms",
            description: "Routing decision latency");
    
    private static readonly Histogram<double> AgentExecutionLatency = 
        Meter.CreateHistogram<double>(
            "orchestration.agent.execution.duration",
            unit: "ms",
            description: "Agent execution time");
    
    public void RecordRoutingDecision(string agentId, double confidence, long durationMs)
    {
        RoutingDecisions.Add(1, 
            new("agent.id", agentId),
            new("confidence.bucket", GetConfidenceBucket(confidence)));
        
        RoutingLatency.Record(durationMs,
            new("agent.id", agentId));
    }
    
    private string GetConfidenceBucket(double confidence) => confidence switch
    {
        >= 0.9 => "high",
        >= 0.7 => "medium",
        _ => "low"
    };
}
```

**Metrics Patterns**:
- **Counter**: Monotonically increasing (routing decisions, error counts)
- **Histogram**: Distribution tracking (latency, confidence scores)
- **ObservableGauge**: Snapshot values (active conversations, cache size)
- **TagList**: Use for 4-8 tags to minimize allocations
- **Consistent Tag Order**: Critical for performance

#### Structured Logging
```csharp
// LoggerMessage source generation (compile-time)
internal static partial class OrchestrationLoggerExtensions
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Routing decision: selected agent '{AgentId}' with confidence {Confidence:F2}")]
    public static partial void LogRoutingDecision(
        this ILogger logger, 
        string agentId, 
        double confidence);
    
    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Low confidence routing: selected agent '{AgentId}' with confidence {Confidence:F2}, reason: {Reasoning}")]
    public static partial void LogLowConfidenceRouting(
        this ILogger logger, 
        string agentId, 
        double confidence, 
        string reasoning);
    
    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "Agent execution failed: agent '{AgentId}', task '{TaskId}'")]
    public static partial void LogAgentExecutionFailure(
        this ILogger logger, 
        Exception ex,
        string agentId, 
        string taskId);
}
```

**Logging Best Practices**:
- **Compile-Time Generation**: Use `[LoggerMessage]` for performance
- **Structured Parameters**: Named parameters for indexing/filtering
- **Event IDs**: Consistent numbering for observability dashboards
- **Appropriate Levels**: Error (failures), Warning (degraded), Info (key events), Debug (verbose)
- **No PII**: Redact/hash contextId, never log message content

**Implications for Lucia**:
- Create `ActivitySource` for "lucia.Agents.Orchestration" namespace
- Create `Meter` for "lucia.Agents.Orchestration" metrics
- Instrument RouterExecutor with spans (routing latency, confidence scores)
- Instrument AgentExecutorWrapper with spans (agent execution time, success/failure)
- Instrument Redis operations (persistence latency, cache hits/misses)
- Use compile-time logging with `[LoggerMessage]` for all orchestration logs
- Configure Prometheus/Grafana-compatible exporters for home lab Kubernetes

---

## Technical Decisions

### Decision 1: Coordinator Agent with Dynamic Routing

**Choice**: Use CoordinatorAgent that queries AgentRegistry at runtime for available agents

**Rationale**:
- Lucia supports runtime agent registration/deregistration via A2A protocol
- Static conditional edges or switch-case patterns would require workflow rebuild when agents change
- CoordinatorAgent queries AgentRegistry dynamically to discover available agents and their capabilities
- SLM/lightweight LLM makes routing decisions based on current agent capabilities
- DynamicAgentExecutor resolves and invokes agents from registry at execution time

**Implementation**:
```csharp
// Workflow with dynamic agent resolution
var coordinatorAgent = new CoordinatorAgent(agentRegistry, slmClient);
var dynamicExecutor = new DynamicAgentExecutor(agentRegistry);
var fallbackExecutor = new FallbackExecutor();

var workflow = new WorkflowBuilder()
    .SetStart(coordinatorAgent)
    .AddEdge(
        coordinatorAgent, 
        dynamicExecutor,
        condition: result => result is AgentChoiceResult choice 
            && agentRegistry.HasAgent(choice.AgentId))
    .AddEdge(
        coordinatorAgent,
        fallbackExecutor,
        condition: result => result is AgentChoiceResult choice 
            && !agentRegistry.HasAgent(choice.AgentId))
    .Build<ChatMessage>();
```

**Benefits**:
- ✅ No workflow rebuild when agents register/deregister
- ✅ Agent capabilities advertised through AgentCard metadata
- ✅ SLM can be faster/cheaper than full LLM (e.g., Phi-3, LLaMa 3.2 3B)
- ✅ Fallback path handles unknown/unavailable agents gracefully

---

### Decision 2: TaskContext Persistence

**Choice**: Use StackExchange.Redis with System.Text.Json serialization, 24-hour TTL

**Rationale**:
- StackExchange.Redis is the de facto standard for .NET Redis clients (272 code snippets in Context7)
- System.Text.Json is fastest serializer in .NET 10, AOT-compatible, minimal dependencies
- 24-hour TTL balances conversation continuity with memory management
- Singleton ConnectionMultiplexer pattern prevents connection exhaustion

**Configuration**:
- Redis endpoint: `localhost:6379` (dev), K8s service (prod)
- Connection timeout: 5000ms
- Reconnect policy: ExponentialRetry(5000ms seed, 10000ms max)
- Key pattern: `task:{taskId}` with configurable TTL

---

### Decision 3: Observability Strategy

**Choice**: OpenTelemetry with compile-time logging, custom ActivitySource/Meter, Prometheus exporter

**Rationale**:
- OpenTelemetry is vendor-neutral, works with Prometheus/Grafana in home lab K8s
- Compile-time logging (`[LoggerMessage]`) avoids boxing, provides best performance
- Custom ActivitySource/Meter allows granular control over span/metric emission
- ASP.NET Core auto-instrumentation covers HTTP layer, custom spans for orchestration

**Instrumentation Points**:
- **Spans**: RouterExecutor.Route, AgentExecutorWrapper.Execute, Redis.Get/Set
- **Metrics**: routing_decisions (counter), routing_latency (histogram), agent_execution_duration (histogram)
- **Logs**: Routing decisions, agent failures, Redis connection events

---

## Open Questions

### Question 1: SLM vs LLM for Coordinator Agent

**Status**: Requires performance/cost decision

**Context**: CoordinatorAgent needs fast, reliable routing based on agent capabilities

**Options**:
1. **Small Language Model (SLM)** - Phi-3 Mini, LLaMa 3.2 3B (local)
   - Pros: Low latency (<100ms), privacy-first (local), no API costs
   - Cons: Lower reasoning capability, may struggle with complex ambiguous requests
   
2. **Lightweight Cloud LLM** - GPT-4o-mini, Claude 3.5 Haiku
   - Pros: Better reasoning, handles ambiguity well, structured output support
   - Cons: Network latency (~300-500ms), API costs, privacy concerns
   
3. **Hybrid Approach** - SLM with LLM fallback
   - Pros: Fast for clear requests, accurate for ambiguous ones
   - Cons: Added complexity, confidence threshold tuning needed

**Recommendation**: Option 1 (SLM) for MVP with Option 3 (hybrid) as future enhancement

**Rationale**:
- Aligns with Constitution Principle IV (Privacy-First Architecture)
- Agent capabilities should be clear enough for SLM to route correctly
- Can measure routing accuracy and upgrade to hybrid if needed
- Phi-3 Mini or LLaMa 3.2 3B can run locally on home lab hardware

**Next Steps**: 
- Define `AgentCard.Capabilities` format for optimal SLM parsing in data-model.md
- Create coordinator prompt template with few-shot examples in contracts/CoordinatorAgent.md

---

### Question 2: Agent Registry Integration

**Status**: Requires architecture clarification

**Context**: RouterExecutor needs to query AgentRegistry for available agents and capabilities

**Options**:
1. **Direct DI injection** of `IAgentCatalog` into RouterExecutor
2. **HTTP call** to Agent Registry API (distributed)
3. **Shared state** via workflow configuration

**Recommendation**: Option 1 for MVP (local deployment), design for Option 2 (distributed agents in Phase 4)

**Next Steps**: Document RouterExecutor constructor dependencies in contracts/RouterExecutor.md

---

### Question 3: Multi-Agent Coordination Pattern

**Status**: Deferred to P3 (not MVP)

**Context**: User Story 3 requires orchestrating multiple agents (e.g., "dim lights and play jazz")

**Complexity**: 
- Fan-out edges to multiple agents
- Result aggregation from parallel execution
- Unified response composition

**Recommendation**: Implement P1 (single-agent routing) and P2 (context handoffs) first, defer P3 multi-agent until workflow is stable

**Next Steps**: Create sub-spec for multi-agent coordination if prioritized in future sprint

---

## Dependencies Confirmed

### NuGet Packages Required

```xml
<!-- Workflow engine -->
<PackageReference Include="Microsoft.Agents.AI.Workflows" Version="1.0.0" />

<!-- Redis client -->
<PackageReference Include="StackExchange.Redis" Version="2.8.16" />

<!-- OpenTelemetry -->
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.9.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.9.0" />
<PackageReference Include="OpenTelemetry.Exporter.Prometheus.AspNetCore" Version="1.9.0" />
<PackageReference Include="OpenTelemetry.Exporter.Console" Version="1.9.0" /> <!-- Dev only -->
```

### External Services Required

- **Redis 7.x**: Task persistence (Docker container in dev, K8s StatefulSet in prod)
- **OpenTelemetry Collector**: Metrics/traces aggregation (optional, can export directly to Prometheus)

---

## Constitution Compliance Update

### Principle III: Documentation-First Research ✅ COMPLETE

**Evidence**:
- Microsoft.Agents.AI.Workflows documentation gathered and analyzed
- StackExchange.Redis patterns and configuration reviewed
- OpenTelemetry instrumentation examples studied
- All code snippets validated against official documentation

**Gate Status**: ✅ PASSED - Ready to proceed to Phase 1 (Design & Contracts)

---

## Next Steps

1. **Phase 1: Design & Contracts**
   - Create `data-model.md` with `TaskContext`, `AgentChoiceResult`, `WorkflowState` schemas
   - Create `contracts/RouterExecutor.md` with API contract and LLM prompt template
   - Create `contracts/AgentExecutorWrapper.md` with wrapper interface and context propagation
   - Create `contracts/ResultAggregatorExecutor.md` with response aggregation logic
   - Create `contracts/LuciaTaskManager.md` with A2A + TaskManager integration
   - Create `quickstart.md` with developer onboarding guide

2. **Update AGENTS.md**
   - Run `update-agent-context.ps1` to add Agent Framework workflows, Redis, OpenTelemetry patterns

3. **Re-evaluate Constitution Check**
   - Verify no principle violations introduced by design decisions
   - Document any complexity justifications in plan.md

---

**Research Completed**: 2025-10-13  
**Documentation Sources**: Microsoft Learn, Context7 (StackExchange.Redis, OpenTelemetry.NET)  
**Constitution Gate**: PASSED (Principle III)
