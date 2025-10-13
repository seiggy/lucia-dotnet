# Implementation Tasks: Multi-Agent Orchestration

**Feature Branch**: `001-multi-agent-orchestration`  
**Created**: 2025-10-13  
**Status**: Ready for Implementation  
**Spec**: [spec.md](./spec.md)

## Task Overview

**Total Tasks**: 45  
**Phases**: 7  
**Estimated Duration**: 3-4 weeks  
**MVP Scope**: Phase 3 (US1 - Automatic Agent Routing)

### Tasks by User Story

- **Setup & Foundation**: 10 tasks (T001-T010)
- **US1 - Automatic Agent Routing (P1)**: 12 tasks (T011-T022) - MVP
- **US4 - Durable Task Persistence (P2)**: 8 tasks (T023-T030)
- **US2 - Context-Preserving Handoffs (P2)**: 7 tasks (T031-T037)
- **US3 - Multi-Domain Coordination (P3)**: 6 tasks (T038-T043)
- **Polish & Integration**: 2 tasks (T044-T045)

---

## Phase 1: Setup & Configuration

**Goal**: Configure project infrastructure, dependencies, and test frameworks  
**Completion Criteria**: All dependencies installed, test infrastructure ready, DI container configured

### T001 - [Setup] Add NuGet Package References [P]
**File**: `lucia.Agents/lucia.Agents.csproj`  
**Description**: Add required NuGet packages for orchestration
```xml
<PackageReference Include="Microsoft.Agents.AI.Workflows" Version="1.0.0" />
<PackageReference Include="StackExchange.Redis" Version="2.8.16" />
<PackageReference Include="OpenTelemetry" Version="1.10.0" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.10.0" />
<PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.10.0" />
```
**Acceptance**: Project builds without errors after package restore

---

### T002 - [Setup] Configure Redis Connection in AppHost [P]
**File**: `lucia.AppHost/AppHost.cs`  
**Description**: Add Redis resource to Aspire AppHost
```csharp
var redis = builder.AddRedis("redis")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

builder.AddProject<Projects.lucia_AgentHost>("agent-host")
    .WithReference(redis);
```
**Acceptance**: Redis container starts with AppHost, connection string available to AgentHost

---

### T003 - [Setup] Configure ChatClient Connection Strings
**Files**: 
- `lucia.AppHost/appsettings.Development.json`
- `lucia.AgentHost/appsettings.Development.json`

**Description**: Add connection strings for keyed IChatClient instances
```json
{
  "ConnectionStrings": {
    "ollama-phi3-mini": "Endpoint=http://localhost:11434;Model=phi3:mini;Provider=ollama",
    "ollama-llama3-2-3b": "Endpoint=http://localhost:11434;Model=llama3.2:3b;Provider=ollama",
    "redis": "localhost:6379"
  }
}
```
**Acceptance**: Configuration loads without errors, connection strings accessible via IConfiguration

---

### T004 - [Setup] Create Test Infrastructure Base Classes [P]
**File**: `lucia.Tests/TestDoubles/TestBase.cs`  
**Description**: Create base test class with common setup (FakeIt Easy, xUnit, test data builders)
```csharp
public abstract class TestBase
{
    protected ILogger<T> CreateLogger<T>() => A.Fake<ILogger<T>>();
    protected IOptions<T> CreateOptions<T>(T value) where T : class => Options.Create(value);
}
```
**Acceptance**: Test classes can inherit TestBase and access helper methods

---

### T005 - [Setup] Create Configuration Options Classes [P]
**Files**:
- `lucia.Agents/Orchestration/RouterExecutorOptions.cs`
- `lucia.Agents/Orchestration/AgentExecutorWrapperOptions.cs`
- `lucia.Agents/Orchestration/ResultAggregatorOptions.cs`

**Description**: Create configuration POCO classes for each orchestration component  
**Reference**: [data-model.md](./data-model.md) Configuration Models section  
**Acceptance**: Options classes match data-model.md schema, bind from appsettings.json

---

### T006 - [Setup] Add Configuration to appsettings.json
**File**: `lucia.AgentHost/appsettings.json`  
**Description**: Add configuration sections for orchestration components
```json
{
  "RouterExecutor": {
    "ChatClientKey": "ollama-phi3-mini",
    "ConfidenceThreshold": 0.7,
    "SystemPrompt": "You are a routing assistant...",
    "UserPromptTemplate": "Given this request: {0}\n\nSelect agent..."
  },
  "AgentExecutorWrapper": {
    "DefaultTimeoutMs": 30000,
    "MaxRetries": 2,
    "RetryDelayMs": 1000
  },
  "ResultAggregator": {
    "FallbackMessage": "I encountered issues processing your request.",
    "SuccessTemplate": "I've completed {0} action(s): {1}"
  },
  "Redis": {
    "TaskTtlHours": 24,
    "ConnectRetryCount": 3,
    "ConnectTimeout": 5000
  }
}
```
**Acceptance**: Configuration sections load into Options classes via DI

---

### T007 - [Setup] Create OpenTelemetry ActivitySources [P]
**File**: `lucia.Agents/Orchestration/OrchestrationTelemetry.cs`  
**Description**: Create static ActivitySource instances for orchestration telemetry
```csharp
public static class OrchestrationTelemetry
{
    public static readonly ActivitySource Source = new("Lucia.Orchestration", "1.0.0");
    
    public static class Tags
    {
        public const string AgentId = "agent.id";
        public const string Confidence = "agent.confidence";
        public const string TaskId = "task.id";
        public const string SessionId = "session.id";
    }
}
```
**Acceptance**: ActivitySource available for instrumentation, registered with OpenTelemetry

---

### T008 - [Setup] Configure OpenTelemetry in ServiceDefaults
**File**: `lucia.ServiceDefaults/Extensions.cs`  
**Description**: Register orchestration ActivitySource with OpenTelemetry
```csharp
.WithTracing(tracing => tracing
    .AddSource("Lucia.Orchestration")
    .AddAspNetCoreInstrumentation()
    .AddHttpClientInstrumentation())
```
**Acceptance**: Orchestration spans appear in Aspire dashboard telemetry view

---

### T009 - [Setup] Create LoggerMessage Definitions [P]
**File**: `lucia.Agents/Orchestration/OrchestrationLogMessages.cs`  
**Description**: Create compile-time logging with [LoggerMessage] attributes
```csharp
public static partial class OrchestrationLogMessages
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Router selected agent {AgentId} with confidence {Confidence}")]
    public static partial void AgentSelected(this ILogger logger, string agentId, double confidence);
    
    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent execution timeout for {AgentId} after {TimeoutMs}ms")]
    public static partial void AgentTimeout(this ILogger logger, string agentId, int timeoutMs);
}
```
**Acceptance**: Structured logging with compile-time code generation, logs appear in Aspire dashboard

---

### T010 - [Setup] Create Test Data Builders [P]
**File**: `lucia.Tests/TestDoubles/TestDataBuilders.cs`  
**Description**: Create builder pattern for test data (AgentCard, ChatMessage, AgentChoiceResult)
```csharp
public class AgentCardBuilder
{
    public AgentCard Build() => new AgentCard { Id = "test-agent", ... };
    public AgentCardBuilder WithId(string id) { /* ... */ }
}
```
**Acceptance**: Tests can use builders to create test data fluently

---

## Phase 2: Foundational Components

**Goal**: Implement core infrastructure needed by all user stories  
**Completion Criteria**: AgentRegistry integration works, base executor patterns established, Redis connectivity verified

### T011 - [Foundation] Write Tests for AgentChoiceResult Model
**File**: `lucia.Tests/Models/AgentChoiceResultTests.cs`  
**User Story**: Foundation for US1  
**Description**: Test-first creation of AgentChoiceResult model  
**Tests**:
- Serialization to/from JSON
- Required field validation
- Confidence score range (0.0-1.0)
- Additional agents list handling

**Acceptance**: All tests pass, model ready for use

---

### T012 - [Foundation] Implement AgentChoiceResult Model
**File**: `lucia.Agents/Models/AgentChoiceResult.cs`  
**User Story**: Foundation for US1  
**Description**: Create AgentChoiceResult per data-model.md  
**Reference**: [data-model.md](./data-model.md#agentchoiceresult)
```csharp
public class AgentChoiceResult
{
    [JsonPropertyName("agentId")]
    public required string AgentId { get; init; }
    
    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }
    
    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; init; }
    
    [JsonPropertyName("additionalAgents")]
    public List<string>? AdditionalAgents { get; init; }
}
```
**Acceptance**: All tests from T011 pass

---

### T013 - [Foundation] Write Tests for AgentResponse Model [P]
**File**: `lucia.Tests/Models/AgentResponseTests.cs`  
**User Story**: Foundation for US1  
**Description**: Test-first creation of AgentResponse model  
**Tests**:
- Serialization to/from JSON
- Success/failure state handling
- Error message validation
- Execution time tracking

**Acceptance**: All tests pass, model ready for use

---

### T014 - [Foundation] Implement AgentResponse Model [P]
**File**: `lucia.Agents/Models/AgentResponse.cs`  
**User Story**: Foundation for US1  
**Description**: Create AgentResponse per data-model.md  
**Reference**: [data-model.md](./data-model.md#agentresponse)  
**Acceptance**: All tests from T013 pass

---

### T015 - [Foundation] Write Tests for WorkflowState Model [P]
**File**: `lucia.Tests/Models/WorkflowStateTests.cs`  
**User Story**: Foundation for US1  
**Description**: Test workflow execution state tracking  
**Acceptance**: All tests pass

---

### T016 - [Foundation] Implement WorkflowState Model [P]
**File**: `lucia.Agents/Models/WorkflowState.cs`  
**User Story**: Foundation for US1  
**Reference**: [data-model.md](./data-model.md#workflowstate)  
**Acceptance**: All tests from T015 pass

---

### T017 - [Foundation] Create Redis Connection Factory
**File**: `lucia.Agents/Services/RedisConnectionFactory.cs`  
**User Story**: Foundation for US4  
**Description**: Create factory for Redis ConnectionMultiplexer with retry logic
```csharp
public class RedisConnectionFactory
{
    public async Task<IConnectionMultiplexer> CreateAsync(string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.ConnectRetry = 3;
        options.ReconnectRetryPolicy = new ExponentialRetry(1000);
        return await ConnectionMultiplexer.ConnectAsync(options);
    }
}
```
**Reference**: [research.md](./research.md) Redis section  
**Acceptance**: Factory creates connections with proper retry configuration

---

### T018 - [Foundation] Write Integration Tests for Redis Connectivity [P]
**File**: `lucia.Tests/Integration/RedisConnectionTests.cs`  
**User Story**: Foundation for US4  
**Description**: Verify Redis connection and basic operations  
**Tests**:
- Connect to Redis container
- Set/Get string values
- Key expiration (TTL)
- Connection retry on failure

**Acceptance**: Tests pass against local Redis container (docker)

---

### T019 - [Foundation] Register Redis in DI Container
**File**: `lucia.AgentHost/Extensions/ServiceCollectionExtensions.cs`  
**User Story**: Foundation for US4  
**Description**: Register Redis connection as singleton in DI
```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config.GetConnectionString("redis");
    return ConnectionMultiplexer.Connect(connectionString);
});
```
**Acceptance**: IConnectionMultiplexer resolves from DI with active connection

---

### T020 - [Foundation] Create Base Executor Pattern
**File**: `lucia.Agents/Orchestration/BaseExecutor.cs`  
**User Story**: Foundation for US1  
**Description**: Create abstract base class for workflow executors with telemetry
```csharp
public abstract class BaseExecutor<TInput, TOutput>
{
    protected readonly ILogger Logger;
    protected readonly ActivitySource ActivitySource = OrchestrationTelemetry.Source;
    
    public abstract ValueTask<TOutput> HandleAsync(
        TInput input,
        IWorkflowContext context,
        CancellationToken cancellationToken);
}
```
**Acceptance**: Base class provides logging and telemetry infrastructure

---

✅ **Checkpoint**: Foundation complete - Redis connectivity verified, base models created, executor pattern established

---

## Phase 3: US1 - Automatic Agent Routing (P1) - MVP

**Goal**: Implement core routing functionality - users can issue single-domain commands and get responses  
**Independent Test**: Send "Turn on the kitchen lights" → light-agent selected → lights turn on → natural language response  
**Completion Criteria**: RouterExecutor selects agents based on LLM analysis, workflow executes, users receive natural language responses

### T021 - [US1] Write Tests for RouterExecutor
**File**: `lucia.Tests/Orchestration/RouterExecutorTests.cs`  
**User Story**: US1 - Automatic Agent Routing  
**Description**: Test-first RouterExecutor implementation  
**Tests**:
- High confidence selection (>0.7)
- Low confidence fallback (<0.7)
- No agents available fallback
- LLM structured output parsing
- Retry on transient failures
- Agent catalog formatting in prompt

**Acceptance**: All tests pass with mocked IChatClient and AgentRegistry

---

### T022 - [US1] Implement RouterExecutor
**File**: `lucia.Agents/Orchestration/RouterExecutor.cs` (enhance existing)  
**User Story**: US1 - Automatic Agent Routing  
**Description**: Complete RouterExecutor implementation per contract  
**Reference**: [contracts/RouterExecutor.md](./contracts/RouterExecutor.md)  
**Key Features**:
- Inherit from `ReflectingExecutor<RouterExecutor>`
- Implement `IMessageHandler<ChatMessage, AgentChoiceResult>`
- Query AgentRegistry for available agents
- Format agent catalog for LLM prompt
- Invoke keyed IChatClient with structured JSON output
- Parse AgentChoiceResult from LLM response
- Apply confidence threshold logic
- Emit OpenTelemetry spans with tags

**Acceptance**: All tests from T021 pass, FR-001, FR-002, FR-013 satisfied

---

### T023 - [US1] Write Tests for ResultAggregatorExecutor [P]
**File**: `lucia.Tests/Orchestration/ResultAggregatorExecutorTests.cs`  
**User Story**: US1 - Automatic Agent Routing  
**Description**: Test response aggregation logic  
**Tests**:
- All agents succeeded
- All agents failed
- Partial success (mixed results)
- Empty response list handling
- Template formatting

**Acceptance**: All tests pass

---

### T024 - [US1] Implement ResultAggregatorExecutor [P]
**File**: `lucia.Agents/Orchestration/ResultAggregatorExecutor.cs`  
**User Story**: US1 - Automatic Agent Routing  
**Description**: Aggregate agent responses into natural language  
**Reference**: [contracts/ResultAggregatorExecutor.md](./contracts/ResultAggregatorExecutor.md)  
**Acceptance**: All tests from T023 pass, FR-006 satisfied

---

### T025 - [US1] Write Tests for AgentExecutorWrapper [P]
**File**: `lucia.Tests/Orchestration/AgentExecutorWrapperTests.cs`  
**User Story**: US1 - Automatic Agent Routing  
**Description**: Test agent execution wrapper  
**Tests**:
- Local AIAgent invocation
- Remote A2A agent invocation (via TaskManager)
- Timeout handling
- Retry logic on transient failures
- Telemetry span emission
- Error handling and structured AgentResponse

**Acceptance**: All tests pass with mocked AIAgent and TaskManager

---

### T026 - [US1] Implement AgentExecutorWrapper [P]
**File**: `lucia.Agents/Orchestration/AgentExecutorWrapper.cs`  
**User Story**: US1 - Automatic Agent Routing  
**Description**: Wrap AIAgent with telemetry and timeout  
**Reference**: [contracts/AgentExecutorWrapper.md](./contracts/AgentExecutorWrapper.md)  
**Acceptance**: All tests from T025 pass, FR-004, FR-012 satisfied

---

### T027 - [US1] Write Tests for LuciaOrchestrator
**File**: `lucia.Tests/Orchestration/LuciaOrchestratorTests.cs`  
**User Story**: US1 - Automatic Agent Routing  
**Description**: Test workflow orchestration  
**Tests**:
- Workflow creation (RouterExecutor → AgentExecutorWrapper → ResultAggregatorExecutor)
- Conditional edge routing based on agent ID
- End-to-end execution flow
- Error propagation from any executor
- OpenTelemetry workflow span

**Acceptance**: All tests pass with mocked executors

---

### T028 - [US1] Implement LuciaOrchestrator
**File**: `lucia.Agents/Orchestration/LuciaOrchestrator.cs`  
**User Story**: US1 - Automatic Agent Routing  
**Description**: Build and execute workflow graph  
**Reference**: Spec FR-009, FR-014  
**Key Features**:
- Inherit from AIAgent (A2A-compliant)
- Build workflow graph: Start → RouterExecutor → conditional edges → AgentExecutorWrapper(s) → ResultAggregatorExecutor → End
- Conditional edge logic: route to agent wrapper based on RouterExecutor.AgentId
- Integrate with TaskManager for workflow state
- Expose via AgentRegistry as "lucia-orchestrator"

**Acceptance**: All tests from T027 pass, FR-009 satisfied

---

### T029 - [US1] Register Orchestration Components in DI
**File**: `lucia.AgentHost/Extensions/ServiceCollectionExtensions.cs`  
**User Story**: US1 - Automatic Agent Routing  
**Description**: Register all orchestration components in DI container  
**Key Registrations**:
- Keyed IChatClient instances (from connection strings)
- RouterExecutor (with keyed IChatClient)
- AgentExecutorWrapper
- ResultAggregatorExecutor
- LuciaOrchestrator (as AIAgent)
- Register LuciaOrchestrator with AgentRegistry

**Acceptance**: All components resolve from DI, LuciaOrchestrator appears in agent catalog

---

### T030 - [US1] Write Integration Tests for US1 Complete Flow
**File**: `lucia.Tests/Integration/AutomaticAgentRoutingTests.cs`  
**User Story**: US1 - Automatic Agent Routing  
**Description**: End-to-end integration test for US1  
**Test Scenarios** (from spec.md acceptance scenarios):
1. "Turn on the kitchen lights" → light-agent → lights on → confirmation
2. "Play some jazz music" → music-agent → music plays → confirmation
3. "Set temperature to 72 degrees" → climate-agent → thermostat adjusted → confirmation

**Acceptance**: All US1 acceptance scenarios pass, SC-001, SC-003, SC-010 validated

---

### T031 - [US1] Create JSON-RPC Endpoint for Orchestration
**File**: `lucia.AgentHost/APIs/OrchestrationController.cs`  
**User Story**: US1 - Automatic Agent Routing  
**Description**: Create ASP.NET Core endpoint for orchestration requests  
**Endpoint**: `POST /api/orchestrate`  
**Request**:
```json
{
  "message": "Turn on the kitchen lights",
  "sessionId": "user-session-123",
  "taskId": "task-abc-456"
}
```
**Response**:
```json
{
  "response": "I've turned on the kitchen lights",
  "taskId": "task-abc-456",
  "agentId": "light-agent"
}
```
**Acceptance**: Endpoint receives requests, invokes LuciaOrchestrator, returns natural language responses

---

### T032 - [US1] Update quickstart.md with US1 Testing Instructions
**File**: `specs/001-multi-agent-orchestration/quickstart.md`  
**User Story**: US1 - Automatic Agent Routing  
**Description**: Document how to test US1 locally  
**Acceptance**: Developers can follow quickstart to verify US1 functionality

---

✅ **Checkpoint**: US1 Complete - Users can issue single-domain commands and receive natural language responses (MVP ACHIEVED)

---

## Phase 4: US4 - Durable Task Persistence (P2)

**Goal**: Survive process restarts with full conversation context restoration  
**Independent Test**: Start conversation → create taskId → restart host → send follow-up → context restored  
**Completion Criteria**: TaskContext persists to Redis, workflow resumes after restart

### T033 - [US4] Write Tests for TaskContext Model
**File**: `lucia.Tests/Models/TaskContextTests.cs`  
**User Story**: US4 - Durable Task Persistence  
**Description**: Test A2A-compliant TaskContext serialization  
**Tests**:
- JSON serialization with System.Text.Json
- Required fields validation (id, sessionId, status)
- TaskStatus and TaskState enum serialization
- History array handling
- Artifacts array handling
- Metadata dictionary serialization

**Acceptance**: All tests pass, A2A schema compliance validated

---

### T034 - [US4] Implement TaskContext and Related Models [P]
**Files**:
- `lucia.Agents/Models/TaskContext.cs`
- `lucia.Agents/Models/TaskStatus.cs`
- `lucia.Agents/Models/TaskState.cs`

**User Story**: US4 - Durable Task Persistence  
**Description**: Create A2A-compliant models per data-model.md  
**Reference**: [data-model.md](./data-model.md) TaskContext, TaskStatus, TaskState sections  
**Acceptance**: All tests from T033 pass, A2A compliance verified

---

### T035 - [US4] Write Tests for TaskManager
**File**: `lucia.Tests/Services/TaskManagerTests.cs`  
**User Story**: US4 - Durable Task Persistence  
**Description**: Test Redis-based task persistence  
**Tests**:
- Save TaskContext to Redis with TTL
- Load TaskContext from Redis by taskId
- Update existing TaskContext
- Handle non-existent taskId (return null)
- Handle Redis connection failures
- TTL expiration behavior

**Acceptance**: All tests pass with Redis container

---

### T036 - [US4] Implement TaskManager
**File**: `lucia.Agents/Services/TaskManager.cs`  
**User Story**: US4 - Durable Task Persistence  
**Description**: Implement Redis-based task persistence service  
**Reference**: [contracts/TaskManager.md](./contracts/TaskManager.md)  
**Key Features**:
- Save/Load/Update/Delete task operations
- Key pattern: `task:{taskId}`
- TTL: 24 hours (configurable)
- System.Text.Json serialization
- OpenTelemetry spans for Redis operations

**Acceptance**: All tests from T035 pass, FR-007, FR-008 satisfied

---

### T037 - [US4] Register TaskManager in DI [P]
**File**: `lucia.AgentHost/Extensions/ServiceCollectionExtensions.cs`  
**User Story**: US4 - Durable Task Persistence  
**Description**: Register TaskManager as singleton  
**Acceptance**: TaskManager resolves from DI with Redis connection

---

### T038 - [US4] Integrate TaskManager with LuciaOrchestrator
**File**: `lucia.Agents/Orchestration/LuciaOrchestrator.cs` (modify)  
**User Story**: US4 - Durable Task Persistence  
**Description**: Add task persistence to orchestration workflow  
**Changes**:
- Load TaskContext at workflow start (if taskId provided)
- Create new TaskContext if not exists
- Update TaskContext.History with messages
- Update TaskContext.Status on completion
- Save TaskContext after workflow execution

**Acceptance**: TaskContext persists and restores correctly during orchestration

---

### T039 - [US4] Write Integration Tests for US4 Complete Flow
**File**: `lucia.Tests/Integration/DurableTaskPersistenceTests.cs`  
**User Story**: US4 - Durable Task Persistence  
**Description**: End-to-end test for task persistence  
**Test Scenarios** (from spec.md acceptance scenarios):
1. Start conversation → restart host → resume conversation with same taskId
2. Multiple active conversations → restart host → all contexts available
3. Expired TTL → graceful new conversation start

**Acceptance**: All US4 acceptance scenarios pass, SC-005 validated

---

### T040 - [US4] Add Persistence Monitoring Metrics
**File**: `lucia.Agents/Services/TaskManager.cs` (enhance)  
**User Story**: US4 - Durable Task Persistence  
**Description**: Add metrics for task persistence operations  
**Metrics**:
- `task_save_duration` (histogram)
- `task_load_duration` (histogram)
- `task_cache_hits` (counter)
- `task_cache_misses` (counter)

**Acceptance**: Metrics appear in Aspire dashboard, SC-009 partially satisfied

---

✅ **Checkpoint**: US4 Complete - Task persistence survives restarts, conversation context restores

---

## Phase 5: US2 - Context-Preserving Conversation Handoffs (P2)

**Goal**: Multi-turn conversations maintain context when shifting between agents  
**Independent Test**: "Turn on the bedroom lamp" → "Now play classical music" → music starts in bedroom  
**Completion Criteria**: Context (location, previous requests) flows between agents via TaskContext

### T041 - [US2] Enhance TaskContext with Context Metadata
**File**: `lucia.Agents/Models/TaskContext.cs` (modify)  
**User Story**: US2 - Context-Preserving Conversation Handoffs  
**Description**: Add context extraction to Metadata dictionary  
**Metadata Keys**:
- `"location"`: Extracted location (e.g., "bedroom")
- `"previousAgents"`: List of agent IDs in conversation
- `"conversationTopic"`: Current topic/domain

**Acceptance**: Context metadata serializes/deserializes correctly

---

### T042 - [US2] Write Tests for Context Extraction in RouterExecutor
**File**: `lucia.Tests/Orchestration/RouterExecutorTests.cs` (add tests)  
**User Story**: US2 - Context-Preserving Conversation Handoffs  
**Description**: Test context-aware routing  
**Tests**:
- Inject TaskContext history into LLM prompt
- Extract location from previous messages
- Maintain conversation topic across agent switches
- Pass context to selected agent

**Acceptance**: Tests validate context flows through routing decisions

---

### T043 - [US2] Enhance RouterExecutor with Context Awareness
**File**: `lucia.Agents/Orchestration/RouterExecutor.cs` (modify)  
**User Story**: US2 - Context-Preserving Conversation Handoffs  
**Description**: Include TaskContext in routing prompt  
**Prompt Enhancement**:
```
Previous conversation:
- User asked: "Turn on the bedroom lamp"
- Agent: light-agent (location: bedroom)

Current request: "Now play some classical music"
```
**Acceptance**: Tests from T042 pass, FR-005 satisfied

---

### T044 - [US2] Enhance AgentExecutorWrapper with Context Injection
**File**: `lucia.Agents/Orchestration/AgentExecutorWrapper.cs` (modify)  
**User Story**: US2 - Context-Preserving Conversation Handoffs  
**Description**: Inject TaskContext metadata into agent invocation  
**Changes**:
- Extract location from TaskContext.Metadata
- Include previous agent selections in context
- Pass context to AIAgent via ChatMessage metadata

**Acceptance**: Agents receive contextual information for better responses

---

### T045 - [US2] Write Integration Tests for US2 Complete Flow
**File**: `lucia.Tests/Integration/ContextPreservingHandoffsTests.cs`  
**User Story**: US2 - Context-Preserving Conversation Handoffs  
**Description**: End-to-end test for context handoffs  
**Test Scenarios** (from spec.md acceptance scenarios):
1. "Turn on bedroom lamp" → "Now play classical music" → music in bedroom
2. "Dim the lights" → "What's the temperature?" → context preserved
3. Multiple topic shifts → context maintained

**Acceptance**: All US2 acceptance scenarios pass, SC-002 validated

---

### T046 - [US2] Update quickstart.md with Multi-Turn Examples
**File**: `specs/001-multi-agent-orchestration/quickstart.md`  
**User Story**: US2 - Context-Preserving Conversation Handoffs  
**Description**: Document multi-turn testing scenarios  
**Acceptance**: Developers can test context preservation locally

---

### T047 - [US2] Add Context Telemetry
**File**: `lucia.Agents/Orchestration/OrchestrationLogMessages.cs` (add)  
**User Story**: US2 - Context-Preserving Conversation Handoffs  
**Description**: Add structured logging for context operations  
**Log Messages**:
- Context extracted from history
- Location resolved
- Context injected into agent invocation

**Acceptance**: Context flow visible in Aspire dashboard logs

---

✅ **Checkpoint**: US2 Complete - Multi-turn conversations preserve context across agent boundaries

---

## Phase 6: US3 - Multi-Domain Coordination (P3)

**Goal**: Multiple agents work together on complex requests  
**Independent Test**: "Dim living room lights to 30% and play relaxing jazz" → both agents execute → unified response  
**Completion Criteria**: AgentDispatchExecutor handles multi-agent workflows, responses aggregate correctly

### T048 - [US3] Write Tests for AgentDispatchExecutor
**File**: `lucia.Tests/Orchestration/AgentDispatchExecutorTests.cs`  
**User Story**: US3 - Multi-Domain Coordination  
**Description**: Test multi-agent sequential execution  
**Tests**:
- Single agent execution (primary only)
- Multiple agents execution (primary + additional)
- Partial failure handling (one agent fails)
- All agents fail scenario
- Collect all AgentResponse messages

**Acceptance**: All tests pass with mocked AgentExecutorWrappers

---

### T049 - [US3] Implement AgentDispatchExecutor
**File**: `lucia.Agents/Orchestration/AgentDispatchExecutor.cs`  
**User Story**: US3 - Multi-Domain Coordination  
**Description**: Execute multiple agents sequentially  
**Reference**: Spec FR-014  
**Key Features**:
- Inherit from `ReflectingExecutor<AgentDispatchExecutor>`
- Implement `IMessageHandler<AgentChoiceResult, List<AgentResponse>>`
- Execute primary agent first
- Execute additional agents sequentially if specified
- Collect all AgentResponse messages
- Handle partial failures gracefully

**Acceptance**: All tests from T048 pass, FR-014 satisfied

---

### T050 - [US3] Integrate AgentDispatchExecutor into LuciaOrchestrator
**File**: `lucia.Agents/Orchestration/LuciaOrchestrator.cs` (modify)  
**User Story**: US3 - Multi-Domain Coordination  
**Description**: Replace single agent invocation with AgentDispatchExecutor  
**Workflow Change**:
- RouterExecutor → AgentDispatchExecutor (NEW) → ResultAggregatorExecutor
- AgentDispatchExecutor receives AgentChoiceResult
- Dispatches to primary + additional agents
- Returns List<AgentResponse>

**Acceptance**: Multi-agent workflows execute correctly

---

### T051 - [US3] Enhance ResultAggregatorExecutor for Multi-Agent [P]
**File**: `lucia.Agents/Orchestration/ResultAggregatorExecutor.cs` (modify)  
**User Story**: US3 - Multi-Domain Coordination  
**Description**: Handle multiple agent responses in aggregation  
**Template Enhancement**:
- List all completed actions
- Group by success/failure
- Create unified natural language summary

**Acceptance**: Multi-agent responses aggregate into coherent messages

---

### T052 - [US3] Write Integration Tests for US3 Complete Flow
**File**: `lucia.Tests/Integration/MultiDomainCoordinationTests.cs`  
**User Story**: US3 - Multi-Domain Coordination  
**Description**: End-to-end test for multi-agent coordination  
**Test Scenarios** (from spec.md acceptance scenarios):
1. "Dim lights to 30% and play jazz" → both agents execute → unified response
2. "I'm going to bed" → sequential multi-agent workflow
3. One agent fails → partial success message

**Acceptance**: All US3 acceptance scenarios pass, SC-004, SC-008 validated

---

### T053 - [US3] Update quickstart.md with Multi-Agent Examples
**File**: `specs/001-multi-agent-orchestration/quickstart.md`  
**User Story**: US3 - Multi-Domain Coordination  
**Description**: Document multi-agent testing scenarios  
**Acceptance**: Developers can test multi-agent coordination locally

---

✅ **Checkpoint**: US3 Complete - Multi-agent coordination works for complex requests

---

## Phase 7: Polish & Cross-Cutting Concerns

**Goal**: Performance optimization, comprehensive monitoring, documentation finalization  
**Completion Criteria**: All success criteria met, documentation complete, production-ready

### T054 - [Polish] Add Edge Case Handling Tests
**Files**: Multiple test files  
**Description**: Add tests for edge cases from spec.md  
**Tests**:
- Ambiguous requests (RouterExecutor clarification)
- No suitable agent available
- Agent timeout scenarios
- Concurrent requests with same taskId
- Redis unavailable scenarios
- Malformed taskId handling
- Agent removed mid-conversation

**Acceptance**: All edge cases handled gracefully

---

### T055 - [Polish] Performance Benchmarking
**File**: `lucia.Tests/Performance/OrchestrationBenchmarks.cs`  
**Description**: Create performance benchmarks using BenchmarkDotNet  
**Benchmarks**:
- Routing decision latency (SC-010: <500ms p95)
- End-to-end orchestration latency (SC-001: <2s p95)
- Concurrent request throughput (SC-006: 10+ concurrent)
- Redis persistence latency

**Acceptance**: All performance success criteria validated, benchmarks document baseline

---

---

## Task Dependencies

### Dependency Graph (User Story Completion Order)

```
Phase 1 (Setup) → Phase 2 (Foundation)
                      ↓
                  Phase 3 (US1 - P1) ← MVP Milestone
                      ↓
           ┌──────────┴──────────┐
           ↓                     ↓
    Phase 4 (US4 - P2)    Phase 5 (US2 - P2)
           └──────────┬──────────┘
                      ↓
              Phase 6 (US3 - P3)
                      ↓
              Phase 7 (Polish)
```

### Critical Path

1. **Setup → Foundation → US1** (MVP - 22 tasks, ~2 weeks)
2. **US1 → US4** (Persistence - 8 tasks, ~3 days)
3. **US1 + US4 → US2** (Context handoffs - 7 tasks, ~3 days)
4. **US1 → US3** (Multi-agent - 6 tasks, ~2 days)
5. **All → Polish** (Final integration - 2 tasks, ~2 days)

### Parallel Execution Opportunities

**Within US1 (Phase 3)**:
- T023-T024 (ResultAggregator) [P] while T022 (RouterExecutor)
- T025-T026 (AgentExecutorWrapper) [P] while T022 (RouterExecutor)
- T027-T028 (LuciaOrchestrator) after all executors complete

**Within US4 (Phase 4)**:
- T033-T034 (Models) [P] with T035-T036 (TaskManager)

**Within US2 (Phase 5)**:
- T044 (AgentExecutorWrapper changes) [P] with T043 (RouterExecutor changes)

**Cross-Phase**:
- US4 (T033-T039) can start after T020 (Foundation complete), parallel with US1 later tasks
- US2 tests (T042, T045) can be written [P] while implementation proceeds

---

## Implementation Strategy

### MVP Delivery (Phase 3 - US1)

**Scope**: Automatic agent routing with single-agent execution  
**Tasks**: T001-T032 (32 tasks)  
**Duration**: ~2 weeks  
**Value**: Users can issue natural language commands without manual agent selection

### Incremental Delivery

1. **Week 1**: Setup + Foundation (T001-T020)
2. **Week 2**: US1 MVP (T021-T032)
3. **Week 3**: US4 Persistence + US2 Context (T033-T047)
4. **Week 4**: US3 Multi-Agent + Polish (T048-T055)

### Testing Strategy

- **TDD Approach**: Every implementation task preceded by test task (Constitution Principle II)
- **Unit Tests**: Mock external dependencies (IChatClient, AgentRegistry, Redis)
- **Integration Tests**: Real dependencies via Testcontainers (Redis), Aspire.Hosting.Testing
- **Performance Tests**: BenchmarkDotNet for latency and throughput validation

### Risk Mitigation

- **LLM Reliability**: Structured JSON output reduces parsing failures; retry logic handles transients
- **Redis Availability**: Graceful degradation if Redis unavailable (no persistence, warn user)
- **Agent Failures**: Timeout handling + structured error responses prevent silent failures
- **Routing Accuracy**: Confidence threshold prevents incorrect selections; fallback responses for ambiguity

---

## Task Execution Guidelines

### Before Starting Any Task

1. Read task description and reference documents (contracts, data-model)
2. Review related tests (if implementation task)
3. Check Constitution principles (especially One Class Per File, TDD)
4. Verify dependencies completed

### While Executing Task

1. Follow TDD: Write failing test → Minimal implementation → Test passes → Refactor
2. One class per file (Constitution Principle I)
3. Add OpenTelemetry spans/metrics per contract specifications
4. Use [LoggerMessage] for structured logging
5. Follow C# 13 / .NET 10 RC1 patterns (file-scoped namespaces, required properties)

### After Completing Task

1. Run all tests: `dotnet test`
2. Verify OpenTelemetry spans appear in Aspire dashboard
3. Check code coverage (aim for >80% on new code)
4. Update task status in this document
5. Commit with conventional commit message: `feat(orchestration): <task description>`

---

## Success Criteria Traceability

| Success Criterion | Validated By | Phase |
|-------------------|-------------|-------|
| SC-001: <2s response time | T030 (Integration), T055 (Benchmark) | US1, Polish |
| SC-002: 5+ turn context preservation | T045 (Integration) | US2 |
| SC-003: 95% routing accuracy | T030 (Integration), T042 (Tests) | US1 |
| SC-004: Multi-agent unified responses | T052 (Integration) | US3 |
| SC-005: Task persistence across restarts | T039 (Integration) | US4 |
| SC-006: 10+ concurrent requests | T055 (Benchmark) | Polish |
| SC-007: Ambiguous request handling | T054 (Edge Cases) | Polish |
| SC-008: Graceful error messages | T052 (Integration), T054 (Edge Cases) | US3, Polish |
| SC-009: Telemetry for all operations | T040 (Metrics), T047 (Context Logs) | US4, US2 |
| SC-010: <500ms routing latency | T030 (Integration), T055 (Benchmark) | US1, Polish |

---

## Notes

- **Constitution Compliance**: All tasks follow TDD, one class per file, privacy-first (Ollama default), observability built-in
- **Parallel Tasks**: Marked with [P] can execute concurrently (different files)
- **Test Coverage**: Every implementation task has corresponding test task (TDD enforcement)
- **Incremental Value**: Each phase completion delivers testable user value
- **Production Readiness**: Phase 7 ensures all edge cases, performance goals, and monitoring requirements met

**Next Step**: Begin with T001 (Setup - Add NuGet Package References) and follow sequential execution order within each phase.
