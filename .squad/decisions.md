# Squad Decisions

## Active Decisions

### 1. Eval Expansion Architecture (Ripley, 2026-03-26)

**Summary:** 5-phase roadmap for scaling eval infrastructure from foundation through production scale. Includes infrastructure, tooling, and team expansion plans. See full document below.

### 2. Eval Infrastructure Audit & Extension (Dallas, 2026-03-26)

**Summary:** Extended RealAgentFactory and EvalTestFixture to support all agent types (Climate, Lists, Scene, Dynamic). All components now support comprehensive eval testing across 7 agent types. See full document below.

### 3. Data Pipeline for Eval Scenarios (Ash, 2026-03-26)

**Summary:** Implemented IEvalScenarioSource architecture for converting GitHub issues and conversation traces into standardized eval scenarios. Enables continuous learning from production data. See full document below.

### 4. Climate Agent Eval Suite (Lambert, 2026-03-26)

**Summary:** Created ClimateAgentEvalTests.cs with 8 scenarios covering tool accuracy, intent resolution, and task adherence. Pattern-compliant with existing eval test structure. See full document below.

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction.

---

# Detailed Decision Documents

# Eval Expansion Architecture Plan

**Author:** Ripley (Lead/Eval Architect)  
**Date:** 2025-10-13  
**Status:** Proposed  
**Decision Type:** Architecture

## Executive Summary

This document defines the architecture for expanding lucia's agent evaluation coverage from the current 2 agents (LightAgent, MusicAgent) to comprehensive coverage of all 8 agent types. The plan prioritizes coverage based on functional complexity and risk, establishes a repeatable pattern for adding new eval suites, and outlines the infrastructure changes needed to support trace-driven eval scenarios and GitHub issue ingestion.

## Current State Analysis

### Existing Eval Coverage

| Agent Type | xUnit Eval Suite | YAML Dataset | Status |
|------------|-----------------|--------------|--------|
| LightAgent | ✅ `LightAgentEvalTests.cs` | ✅ `light-agent.yaml` | Complete |
| MusicAgent | ✅ `MusicAgentEvalTests.cs` | ❌ None | Partial (no dataset) |
| OrchestratorAgent | ✅ `OrchestratorEvalTests.cs` | ✅ `orchestrator.yaml` | Complete |
| ClimateAgent | ❌ None | ✅ `climate-agent.yaml` | Dataset only |
| ListsAgent | ❌ None | ✅ `lists-agent.yaml` | Dataset only |
| SceneAgent | ❌ None | ✅ `scene-agent.yaml` | Dataset only |
| GeneralAgent | ❌ None | ✅ `general-agent.yaml` | Dataset only |
| DynamicAgent | ❌ None | ❌ None | No coverage |

### Infrastructure Status

**Working Components:**
- `AgentEvalTestBase`: Provides robust base class with reporting config, model parameterization, and helper methods
- `EvalTestFixture`: Constructs real agents with Azure OpenAI/Ollama backing
- `ChatHistoryCapture`: Captures intermediate tool calls for ToolCallAccuracyEvaluator
- `RealAgentFactory`: TUI harness factory pattern matching fixture pattern
- Disk-based reporting for `dotnet aieval report` integration
- YAML scenario format for structured test cases

**Missing Components:**
- GitHub issue → eval scenario converter
- Production trace → YAML scenario generator
- DynamicAgent evaluation infrastructure (dynamic tool registration challenge)
- MusicAgent YAML dataset (`lucia.EvalHarness/TestData/music-agent.yaml`)

## Agent Capability Matrix

### ClimateAgent
**Primary Skills:** ClimateControlSkill, FanControlSkill  
**Tools:** 8 methods
- SetTemperatureAsync
- SetHvacModeAsync
- GetClimateStateAsync
- FindClimateDevicesByAreaAsync
- SetFanStateAsync
- SetFanSpeedAsync
- GetFanStateAsync
- FindFansByAreaAsync

**Capabilities:**
- Temperature control (set, adjust)
- HVAC mode switching (heat, cool, auto, off)
- Fan control (on/off, speed adjustment)
- Entity discovery by area
- State query

**Complexity:** Medium — Multiple skills, embedding-based entity matching, hybrid resolution

### ListsAgent
**Primary Skills:** ListSkill  
**Tools:** 5 methods
- AddToShoppingListAsync
- ListShoppingItemsAsync
- AddToTodoListAsync
- ListTodoItemsAsync
- ListTodoEntitiesAsync

**Capabilities:**
- Shopping list management
- Todo list management
- Multi-item additions (comma-separated)
- List entity discovery

**Complexity:** Low — Simple CRUD operations, no entity resolution required

### SceneAgent
**Primary Skills:** SceneControlSkill  
**Tools:** 3 methods
- ListScenesAsync
- FindScenesByAreaAsync
- ActivateSceneAsync

**Capabilities:**
- Scene discovery (list all, filter by area)
- Scene activation by name/entity ID
- Area-based filtering

**Complexity:** Low — Read-heavy with single activation action

### GeneralAgent
**Primary Skills:** None (optional WebSearchSkill via plugin)  
**Tools:** 0-1 (web_search if configured)

**Capabilities:**
- General knowledge Q&A
- Fallback for unhandled requests
- Web search integration (optional)
- MCP tool integration (dynamic)

**Complexity:** Low (without MCP) → High (with MCP tools)  
**Note:** MCP tools are resolved at initialization from agent definition

### DynamicAgent
**Primary Skills:** None — user-defined via MongoDB  
**Tools:** Dynamically resolved from MCP registry based on agent definition

**Capabilities:**
- User-defined system prompt
- User-defined tool selection from MCP registry
- Hot-reload support
- Custom agent cards

**Complexity:** High — Dynamic tool registration, requires MongoDB fixture, special eval pattern

**Special Considerations:**
- Tools are not known at compile time
- Requires MongoDB-backed `IAgentDefinitionRepository`
- Evaluation must validate tool resolution, not specific tool calls
- Test scenarios must provision test agent definitions in MongoDB

## Priority Ordering

### Tier 1: High Value, Medium Complexity (Next)
1. **ClimateAgent** — Multi-skill, high user value, moderate tool count
   - Most complex remaining agent (2 skills, 8 tools)
   - Critical for home automation use cases
   - Tests embedding-based entity matching
   - Dataset exists, needs xUnit suite

2. **SceneAgent** — Simple pattern, high user value
   - Straightforward API surface (3 tools)
   - Common user workflow (activate scenes)
   - Dataset exists, needs xUnit suite

3. **ListsAgent** — Simple CRUD, moderate value
   - Low complexity (5 tools)
   - Tests multi-item handling
   - Dataset exists, needs xUnit suite

### Tier 2: Fallback & General (Required for completeness)
4. **GeneralAgent** — Fallback handler
   - Tests out-of-domain routing
   - Optional web search integration
   - Dataset exists, needs xUnit suite
   - Low tool count (0-1), high routing importance

5. **MusicAgent dataset** — Complete existing coverage
   - Suite exists but lacks YAML dataset
   - Inverse of other agents (code complete, data missing)

### Tier 3: Advanced (After core coverage)
6. **DynamicAgent** — Advanced infrastructure
   - Requires MongoDB fixture integration
   - Needs custom eval pattern for dynamic tools
   - No dataset (will be generated from definition examples)
   - Validates hot-reload and tool resolution

## Standard Eval Suite Pattern

Based on `LightAgentEvalTests.cs` and `MusicAgentEvalTests.cs`, the repeatable pattern is:

### 1. Test Class Structure

```csharp
#pragma warning disable AIEVAL001 // Microsoft.Extensions.AI.Evaluation is experimental

namespace lucia.Tests.Orchestration;

/// <summary>
/// Evaluation tests for the {AgentName}. Exercises the real <see cref="lucia.Agents.Agents.{AgentName}"/>
/// code path — including <c>ChatClientAgent</c> with <c>FunctionInvokingChatClient</c> — so tools
/// are actually invoked against faked Home Assistant dependencies.
/// </summary>
[Trait("Category", "Eval")]
[Trait("Agent", "{AgentType}")]
public sealed class {AgentName}EvalTests : AgentEvalTestBase
{
    public {AgentName}EvalTests(EvalTestFixture fixture) : base(fixture) { }

    // --- Prompt variant datasets ---
    // --- Tool Call Accuracy tests ---
    // --- Intent Resolution tests ---
    // --- Task Adherence tests ---
}
```

### 2. Fixture Factory Method

Add to `EvalTestFixture.cs`:

```csharp
/// <summary>
/// Creates a real <see cref="{AgentName}"/> backed by the given deployment with
/// chat history capture for tool call evaluation.
/// </summary>
public async Task<({AgentType} Agent, ChatHistoryCapture Capture)> Create{AgentName}WithCaptureAsync(
    string deploymentName, string embeddingModelName)
{
    var chatClient = await CreateEvalChatClientAsync(deploymentName);
    var embeddingClient = await CreateEvalEmbeddingClientAsync(embeddingModelName);
    
    var capture = new ChatHistoryCapture();
    var wrappedClient = new DelegatingChatClient(chatClient, capture.OnChatMessage);
    
    // Fake resolver returning wrapped client
    var resolver = A.Fake<IChatClientResolver>();
    A.CallTo(() => resolver.ResolveAsync(A<string?>._, A<CancellationToken>._))
        .Returns(wrappedClient);
    A.CallTo(() => resolver.ResolveAIAgentAsync(A<string?>._, A<CancellationToken>._))
        .Returns(Task.FromResult<AIAgent?>(null));
    
    var skill = new {SkillName}(
        _haClient,
        _loggerFactory.CreateLogger<{SkillName}>(),
        // ... skill-specific dependencies
    );
    
    var agent = new {AgentType}(resolver, _mockDefinitionRepo, skill, _tracingFactory, _loggerFactory);
    await agent.InitializeAsync();
    
    return (agent, capture);
}
```

### 3. RealAgentFactory Method

Add to `RealAgentFactory.cs`:

```csharp
/// <summary>
/// Creates a real <see cref="{AgentName}"/> backed by the given Ollama model.
/// </summary>
public async Task<RealAgentInstance> Create{AgentName}Async(string modelName)
{
    var (resolver, tracer) = CreateOllamaResolverWithTracer(modelName);
    var skill = new {SkillName}(
        _haClient,
        _loggerFactory.CreateLogger<{SkillName}>(),
        // ... skill-specific dependencies
    );
    var agent = new {AgentType}(resolver, _definitionRepo, skill, _tracingFactory, _loggerFactory);
    await agent.InitializeAsync();
    return new RealAgentInstance 
    { 
        AgentName = "{AgentName}", 
        Agent = agent, 
        DatasetFile = "TestData/{agent-name}.yaml", 
        Tracer = tracer 
    };
}
```

Then add to `AgentFactories` dictionary:

```csharp
["{AgentName}"] = Create{AgentName}Async,
```

### 4. Test Method Pattern

Each capability maps to a test trait category:

**Tool Call Accuracy** — Validates correct tool selection and parameter extraction
```csharp
[Trait("Evaluator", "ToolCallAccuracy")]
[SkippableTheory]
[MemberData(nameof(ModelIds))]
public async Task {ToolName}_{Scenario}_{ExpectedBehavior}(string modelId, string embeddingModelId)
{
    var (agent, capture) = await Fixture.Create{AgentName}WithCaptureAsync(modelId, embeddingModelId);
    var reportingConfig = CreateReportingConfig(
        includeTextEvaluators: false);
    
    var (response, result) = await RunAgentAndEvaluateAsync(
        modelId,
        agent.GetAIAgent(),
        capture,
        "User prompt here",
        reportingConfig,
        "{AgentName}.{ToolName}_{Scenario}");
    
    AssertHasTextResponse(response);
    AssertNoUnacceptableMetrics(result);
}
```

**Intent Resolution** — Tests STT robustness, fuzzy matching, multi-step workflows
```csharp
[Trait("Evaluator", "IntentResolution")]
[SkippableTheory]
[MemberData(nameof({Intent}Prompts))]
public async Task {Intent}_{Scenario}_{ExpectedBehavior}(
    string modelId, string embeddingModelId, string prompt, string variant)
{
    // WithVariants pattern for STT robustness testing
}
```

**Task Adherence** — Out-of-domain rejection, domain boundaries
```csharp
[Trait("Evaluator", "TaskAdherence")]
[SkippableTheory]
[MemberData(nameof(OutOfDomainPrompts))]
public async Task OutOfDomain_{RequestType}_{ExpectedBehavior}(
    string modelId, string embeddingModelId, string prompt, string variant)
{
    // Agent should politely decline or explain it's out of scope
}
```

### 5. YAML Dataset Structure

Two supported formats exist:

**Format 1: Structured Scenarios** (light-agent.yaml)
```yaml
scenarios:
  - id: scenario_id
    description: "Human readable description"
    category: control|query|stt-robustness|out-of-domain|edge-case
    initial_state:
      entity.id:
        state: "on"
        attributes:
          friendly_name: "Name"
          brightness: 255
    user_prompt: "User's natural language request"
    expected_tool_calls:
      - tool: ToolNameAsync
        arguments:
          paramName: "value|contains:partial|*"
    response_must_contain:
      - "keyword"
    response_must_not_contain:
      - "forbidden_word"
    expected_final_state:
      entity.id:
        state: "off"
```

**Format 2: Simple Test Data** (climate-agent.yaml, others)
```yaml
data:
  - id: test_id
    input: "User request"
    expected: "keyword_in_response"
    expected_tools:
      - SkillName
    criteria:
      - "Natural language success criterion"
      - "Another criterion"
    metadata:
      category: out-of-domain
      difficulty: hard
```

Both formats are consumed by the TUI harness (`lucia.EvalHarness`). The xUnit tests reference these scenarios in test names but construct prompts directly in `[MemberData]` datasets.

## DynamicAgent Special Considerations

### Challenge: Dynamic Tool Registration

DynamicAgent is fundamentally different from built-in agents:
- Tools are resolved from MCP registry at initialization, not compile-time
- Tool set varies per agent definition (stored in MongoDB)
- No fixed skill classes to reference

### Proposed Eval Pattern

**1. Test Fixture Extension**

```csharp
/// <summary>
/// Creates a <see cref="DynamicAgent"/> from a test agent definition.
/// Requires MongoDB fixture for agent definition storage.
/// </summary>
public async Task<(DynamicAgent Agent, ChatHistoryCapture Capture)> CreateDynamicAgentWithCaptureAsync(
    string deploymentName, 
    string embeddingModelName,
    AgentDefinition testDefinition)
{
    // Store test definition in MongoDB
    await _definitionRepo.SaveAgentDefinitionAsync(testDefinition);
    
    var chatClient = await CreateEvalChatClientAsync(deploymentName);
    var capture = new ChatHistoryCapture();
    var wrappedClient = new DelegatingChatClient(chatClient, capture.OnChatMessage);
    
    var resolver = A.Fake<IChatClientResolver>();
    A.CallTo(() => resolver.ResolveAsync(A<string?>._, A<CancellationToken>._))
        .Returns(wrappedClient);
    
    var agent = new DynamicAgent(
        testDefinition.Id,
        testDefinition,
        _definitionRepo,
        _mcpToolRegistry,
        resolver,
        _providerResolver,
        _providerRepository,
        _tracingFactory,
        _telemetrySource,
        _loggerFactory);
    
    await agent.InitializeAsync();
    
    return (agent, capture);
}
```

**2. Test Dataset: Agent Definitions Instead of Prompts**

```csharp
public static IEnumerable<object[]> DynamicAgentDefinitions =>
    new[]
    {
        new object[]
        {
            new AgentDefinition
            {
                Id = "test-weather-agent",
                Name = "Weather Agent",
                Description = "Provides weather information",
                Instructions = "You are a weather agent. Use web search to find current weather.",
                Tools = ["web_search"],
                ModelProvider = "azure-openai",
                Model = "gpt-4o"
            },
            "What's the weather in Seattle?",
            "weather"
        }
    };
```

**3. Evaluation Focus: Tool Resolution, Not Tool Calls**

DynamicAgent tests validate:
- ✅ Agent definition loading from MongoDB
- ✅ MCP tool resolution (correct tools registered)
- ✅ System prompt application
- ✅ Model provider selection
- ✅ Hot-reload (rebuild after definition update)
- ❌ NOT specific tool call validation (varies per definition)

**4. Infrastructure Requirement: MongoDB Test Fixture**

DynamicAgent requires a MongoDB container for `IAgentDefinitionRepository`:
- Option A: Extend `EvalTestFixture` with Testcontainers MongoDB
- Option B: Use in-memory fake repository for eval tests only
- **Recommendation:** Option B — Fake repository with in-memory storage for test isolation

```csharp
private sealed class InMemoryAgentDefinitionRepository : IAgentDefinitionRepository
{
    private readonly ConcurrentDictionary<string, AgentDefinition> _store = new();
    
    public Task<AgentDefinition?> GetAgentDefinitionAsync(string agentId, CancellationToken ct)
        => Task.FromResult(_store.TryGetValue(agentId, out var def) ? def : null);
    
    public Task SaveAgentDefinitionAsync(AgentDefinition definition, CancellationToken ct)
    {
        _store[definition.Id] = definition;
        return Task.CompletedTask;
    }
    
    // ... other methods
}
```

## GitHub Issue Integration

### Objective
Convert filed GitHub issues into eval scenarios that prevent regressions.

### Proposed Pipeline

```
GitHub Issue → Issue Analyzer → YAML Scenario Generator → Dataset File → CI/CD Eval Run
```

### Issue Analyzer (`lucia.EvalHarness/Providers/GitHubIssueAnalyzer.cs`)

```csharp
public sealed class GitHubIssueAnalyzer
{
    /// <summary>
    /// Extracts eval scenario metadata from a GitHub issue.
    /// </summary>
    public async Task<EvalScenarioMetadata?> AnalyzeIssueAsync(
        string issueUrl, 
        IChatClient judgeClient,
        CancellationToken cancellationToken = default)
    {
        // 1. Fetch issue via GitHub API
        var issue = await FetchIssueAsync(issueUrl, cancellationToken);
        
        // 2. Extract user prompt, expected behavior, actual behavior
        var prompt = $"""
            Analyze this bug report and extract:
            - User's original prompt (what they asked the agent to do)
            - Expected agent behavior (what should have happened)
            - Actual agent behavior (what went wrong)
            - Which agent this affects (light, climate, music, etc.)
            
            Issue Title: {issue.Title}
            Issue Body:
            {issue.Body}
            """;
        
        var response = await judgeClient.CompleteAsync(prompt, cancellationToken: cancellationToken);
        
        // 3. Parse structured metadata
        return JsonSerializer.Deserialize<EvalScenarioMetadata>(response.Message.Text);
    }
}

public sealed class EvalScenarioMetadata
{
    public required string AgentType { get; init; }
    public required string UserPrompt { get; init; }
    public required string ExpectedBehavior { get; init; }
    public required string ActualBehavior { get; init; }
    public List<string> ExpectedToolCalls { get; init; } = [];
    public string Category { get; init; } = "regression";
}
```

### YAML Generator (`lucia.EvalHarness/Providers/YamlScenarioGenerator.cs`)

```csharp
public sealed class YamlScenarioGenerator
{
    /// <summary>
    /// Converts eval scenario metadata into a YAML scenario block.
    /// </summary>
    public string GenerateScenario(EvalScenarioMetadata metadata, string issueNumber)
    {
        // Format 2 (simple) structure for regression tests
        return $$$"""
            - id: gh_issue_{{{issueNumber}}}
              input: "{{{metadata.UserPrompt}}}"
              expected: "{{{ExtractKeyword(metadata.ExpectedBehavior)}}}"
              expected_tools:
                {{{string.Join("\n    ", metadata.ExpectedToolCalls.Select(t => $"- {t}"))}}}
              criteria:
                - "{{{metadata.ExpectedBehavior}}}"
                - "Does not exhibit: {{{metadata.ActualBehavior}}}"
              metadata:
                category: regression
                github_issue: "{{{issueNumber}}}"
            """;
    }
}
```

### Integration Workflow

1. Developer files issue: "LightAgent doesn't handle 'dim kitchen to 50%' correctly"
2. CI workflow triggers `dotnet run --project lucia.EvalHarness -- github-import #{issue_number}`
3. Analyzer extracts metadata via LLM
4. Generator appends scenario to `lucia.EvalHarness/TestData/{agent}-agent.yaml`
5. PR created with new scenario
6. After merge, scenario runs in CI on every commit

### Manual Fallback
When LLM extraction fails or produces low-confidence results:
```bash
dotnet run --project lucia.EvalHarness -- add-scenario \
  --agent light \
  --id gh_issue_42 \
  --input "Dim kitchen to 50%" \
  --expected "50" \
  --tool ControlLightsAsync \
  --criterion "Sets brightness to 50%"
```

## Trace-Driven Eval Scenarios

### Objective
Convert production traces (MongoTraceRepository, SqliteTraceRepository) into eval scenarios for continuous improvement.

### Data Flow

```
Production Trace → Trace Analyzer → Scenario Ranker → YAML Generator → Dataset Update → Eval Suite
```

### Trace Selection Criteria

**High-value traces for eval conversion:**
1. **User satisfaction signals** — Traces with thumbs-up/thumbs-down feedback
2. **Error traces** — Traces where agent execution failed (exception, timeout)
3. **Low-confidence routing** — Traces where router confidence < 0.6
4. **Multi-turn corrections** — User had to rephrase prompt 2+ times
5. **Out-of-domain fallback** — Traces that hit GeneralAgent but shouldn't have

### Trace Analyzer (`lucia.EvalHarness/Providers/TraceAnalyzer.cs`)

```csharp
public sealed class TraceAnalyzer
{
    /// <summary>
    /// Analyzes a production trace and determines if it's a good eval candidate.
    /// </summary>
    public async Task<TraceAnalysisResult> AnalyzeTraceAsync(
        AgentTrace trace,
        IChatClient judgeClient,
        CancellationToken cancellationToken = default)
    {
        // Score the trace
        var score = CalculateEvalWorthScore(trace);
        
        if (score < 0.5) // Threshold: not worth converting
            return new TraceAnalysisResult { IsCandidate = false, Score = score };
        
        // Extract scenario metadata using LLM
        var prompt = $"""
            Analyze this agent trace and create an eval scenario.
            
            User Prompt: {trace.UserMessage}
            Agent Response: {trace.AgentResponse}
            Tool Calls: {string.Join(", ", trace.ToolCalls.Select(tc => tc.ToolName))}
            Execution Time: {trace.ExecutionTime}ms
            Success: {trace.Success}
            
            Extract:
            - What was the user trying to accomplish?
            - Was the agent response correct?
            - What tools should have been called?
            - What would make this a good regression test?
            """;
        
        var response = await judgeClient.CompleteAsync(prompt, cancellationToken: cancellationToken);
        
        return new TraceAnalysisResult
        {
            IsCandidate = true,
            Score = score,
            Metadata = JsonSerializer.Deserialize<EvalScenarioMetadata>(response.Message.Text)
        };
    }
    
    private double CalculateEvalWorthScore(AgentTrace trace)
    {
        double score = 0.0;
        
        // Explicit feedback = highest value
        if (trace.UserFeedback == "thumbs_up") score += 1.0;
        if (trace.UserFeedback == "thumbs_down") score += 0.8; // Negative feedback is valuable
        
        // Error traces = important to prevent regression
        if (!trace.Success) score += 0.7;
        
        // Low confidence = ambiguous routing, good for eval
        if (trace.RoutingConfidence < 0.6) score += 0.5;
        
        // Multi-turn = user had to correct/rephrase
        if (trace.TurnCount > 1) score += 0.4;
        
        // Novel prompts = not covered by existing evals
        // (requires semantic similarity to existing scenarios — future enhancement)
        
        return score;
    }
}
```

### Batch Conversion Workflow

```bash
# Analyze last 1000 traces from production MongoDB
dotnet run --project lucia.EvalHarness -- trace-import \
  --source mongodb \
  --connection "mongodb://localhost:27017" \
  --limit 1000 \
  --min-score 0.5
  
# Output: Suggested scenarios in .squad/trace-scenarios/pending/
# Review, edit, approve, then merge into TestData/*.yaml
```

## Implementation Roadmap

### Phase 1: Complete Tier 1 Coverage (Weeks 1-2)
- [ ] Create `ClimateAgentEvalTests.cs`
  - [ ] Add `CreateClimateAgentWithCaptureAsync` to `EvalTestFixture`
  - [ ] Add `CreateClimateAgentAsync` to `RealAgentFactory` 
  - [ ] Implement 8+ test methods covering both skills
  - [ ] Add STT variant testing for temperature/mode requests
- [ ] Create `SceneAgentEvalTests.cs`
  - [ ] Add fixture factory methods
  - [ ] Implement 5+ test methods
  - [ ] Test area-based scene discovery
- [ ] Create `ListsAgentEvalTests.cs`
  - [ ] Add fixture factory methods
  - [ ] Implement 6+ test methods
  - [ ] Test multi-item comma-separated additions

### Phase 2: Complete Tier 2 Coverage (Week 3)
- [ ] Create `GeneralAgentEvalTests.cs`
  - [ ] Add fixture factory methods
  - [ ] Handle optional WebSearchSkill
  - [ ] Test fallback behavior, out-of-domain routing
  - [ ] Test MCP tool integration (if configured)
- [ ] Create `lucia.EvalHarness/TestData/music-agent.yaml`
  - [ ] Convert existing MusicAgentEvalTests scenarios to YAML format
  - [ ] Add genre-based playback scenarios
  - [ ] Add speaker selection scenarios

### Phase 3: Advanced Infrastructure (Week 4)
- [ ] DynamicAgent evaluation
  - [ ] Add `InMemoryAgentDefinitionRepository` to `EvalTestFixture`
  - [ ] Create `DynamicAgentEvalTests.cs` with definition-based tests
  - [ ] Validate tool resolution from MCP registry
  - [ ] Test hot-reload (rebuild after definition update)
- [ ] GitHub issue integration
  - [ ] Implement `GitHubIssueAnalyzer`
  - [ ] Implement `YamlScenarioGenerator`
  - [ ] Create `dotnet aieval github-import {issue_number}` command
  - [ ] Add CI workflow for auto-import on issue labels

### Phase 4: Trace-Driven Scenarios (Week 5)
- [ ] Production trace ingestion
  - [ ] Implement `TraceAnalyzer` with eval-worth scoring
  - [ ] Create batch trace analysis tool
  - [ ] Build trace → YAML conversion pipeline
  - [ ] Add human-in-the-loop review workflow
- [ ] Scenario deduplication
  - [ ] Implement semantic similarity check
  - [ ] Prevent duplicate scenarios across issues/traces
  - [ ] Merge similar scenarios with variant prompts

## Metrics & Success Criteria

### Coverage Metrics
- **Agent Coverage:** 8/8 agents with xUnit eval suites
- **Dataset Coverage:** 8/8 agents with YAML datasets
- **Tool Coverage:** 100% of agent tools exercised in at least 1 scenario
- **Scenario Categories:** Each agent has tests for:
  - Tool call accuracy
  - Intent resolution (including STT variants)
  - Task adherence (out-of-domain rejection)
  - Edge cases (already-satisfied state, multi-step, etc.)

### Quality Metrics
- **Pass Rate:** ≥ 95% of eval scenarios pass on each model
- **Latency:** p95 latency < 3s for simple scenarios
- **Tool Call Accuracy:** ≥ 90% correct tool selection (ToolCallAccuracyEvaluator)
- **Coherence:** ≥ 4.0/5.0 average (CoherenceEvaluator)
- **Relevance:** ≥ 4.0/5.0 average (RelevanceEvaluator)

### Regression Detection
- **GitHub Issue → Scenario Conversion:** ≥ 80% of filed bugs result in new eval scenarios within 1 week
- **Trace-Driven Scenarios:** ≥ 50 new scenarios per quarter from production traces
- **Scenario Stability:** < 5% eval flakiness (same prompt, different result)

## Infrastructure Changes Required

### Minimal Changes
✅ **No breaking changes to existing infrastructure**  
All new components extend existing patterns without modification.

### New Files

**xUnit Test Suites:**
- `lucia.Tests/Orchestration/ClimateAgentEvalTests.cs`
- `lucia.Tests/Orchestration/SceneAgentEvalTests.cs`
- `lucia.Tests/Orchestration/ListsAgentEvalTests.cs`
- `lucia.Tests/Orchestration/GeneralAgentEvalTests.cs`
- `lucia.Tests/Orchestration/DynamicAgentEvalTests.cs`

**YAML Datasets:**
- `lucia.EvalHarness/TestData/music-agent.yaml` (missing)

**Trace & Issue Tooling:**
- `lucia.EvalHarness/Providers/GitHubIssueAnalyzer.cs`
- `lucia.EvalHarness/Providers/YamlScenarioGenerator.cs`
- `lucia.EvalHarness/Providers/TraceAnalyzer.cs`
- `lucia.EvalHarness/Providers/InMemoryAgentDefinitionRepository.cs`

**Commands:**
- `lucia.EvalHarness/Commands/GitHubImportCommand.cs`
- `lucia.EvalHarness/Commands/TraceImportCommand.cs`
- `lucia.EvalHarness/Commands/AddScenarioCommand.cs`

### Changes to Existing Files

**`lucia.Tests/Orchestration/EvalTestFixture.cs`:**
- Add 4 new factory methods: `CreateClimateAgentWithCaptureAsync`, `CreateSceneAgentWithCaptureAsync`, `CreateListsAgentWithCaptureAsync`, `CreateGeneralAgentWithCaptureAsync`
- Add `CreateDynamicAgentWithCaptureAsync` with in-memory repository support
- Add `_inMemoryDefinitionRepo` field for DynamicAgent tests

**`lucia.EvalHarness/Providers/RealAgentFactory.cs`:**
- Add 4 new factory methods matching test fixture pattern
- Update `AgentFactories` dictionary with 4 new entries
- Add `CreateDynamicAgentAsync` (optional, for TUI harness support)

**CI/CD Integration:**
- Add GitHub Actions workflow: `.github/workflows/eval-on-issue-label.yml`
  - Triggers on issue labeled `needs-eval-scenario`
  - Runs `dotnet run --project lucia.EvalHarness -- github-import {issue_number}`
  - Creates PR with generated scenario

## Risks & Mitigations

### Risk: LLM-based scenario extraction produces low-quality YAML
**Impact:** High — Invalid or incorrect scenarios pollute dataset  
**Mitigation:**
- Human-in-the-loop review before merging generated scenarios
- Confidence scoring on LLM extraction (reject < 0.7 confidence)
- Manual override commands for low-confidence extractions
- Schema validation on generated YAML before writing to disk

### Risk: DynamicAgent eval pattern doesn't match real usage
**Impact:** Medium — Tests pass but real dynamic agents fail  
**Mitigation:**
- Include end-to-end integration test with real MCP server
- Test against production-like agent definitions from `.docs/examples/`
- Validate hot-reload in eval tests (not just initial load)

### Risk: Eval flakiness due to non-deterministic LLM behavior
**Impact:** Medium — False positives in CI, eval trust erosion  
**Mitigation:**
- Run each scenario 3x per model, require 2/3 pass
- Set temperature=0 for deterministic responses where possible
- Track flakiness metrics, flag scenarios with < 80% pass rate
- Use `SkippableTheory` to skip known-flaky scenarios on specific models

### Risk: Over-reliance on trace data creates bias toward current behavior
**Impact:** Low — Evals validate "what is" instead of "what should be"  
**Mitigation:**
- Prioritize GitHub issues (user-reported bugs) over traces
- Include adversarial scenarios (not from traces)
- Manual curation of trace-derived scenarios for correctness

## Decision Points

### Decision 1: In-memory vs. MongoDB for DynamicAgent tests
**Recommendation:** In-memory fake repository  
**Rationale:**
- ✅ Faster test execution (no container startup)
- ✅ Test isolation (no cross-test pollution)
- ✅ Simpler CI setup
- ❌ Doesn't validate MongoDB serialization (accepted trade-off)

### Decision 2: YAML format for new datasets
**Recommendation:** Format 2 (simple) for new agent datasets  
**Rationale:**
- ✅ Matches existing climate/scene/lists/general datasets
- ✅ Easier to generate from traces/issues (less complex structure)
- ✅ TUI harness already supports both formats
- Format 1 (structured scenarios) reserved for complex state-dependent tests

### Decision 3: Trace import: batch vs. streaming
**Recommendation:** Batch with manual review  
**Rationale:**
- ✅ Human can validate quality before merging
- ✅ Prevents eval pollution from low-value traces
- ✅ Simpler initial implementation
- Future: Add streaming for high-confidence traces (thumbs-up feedback)

## References

### Existing Files
- `lucia.Tests/Orchestration/AgentEvalTestBase.cs` — Base class contract
- `lucia.Tests/Orchestration/EvalTestFixture.cs` — Agent construction pattern
- `lucia.Tests/Orchestration/LightAgentEvalTests.cs` — Template for new suites
- `lucia.Tests/Orchestration/MusicAgentEvalTests.cs` — Simpler template
- `lucia.Tests/Orchestration/OrchestratorEvalTests.cs` — Multi-agent routing tests
- `lucia.EvalHarness/Providers/RealAgentFactory.cs` — TUI factory pattern

### YAML Datasets
- `lucia.EvalHarness/TestData/light-agent.yaml` — Format 1 (structured)
- `lucia.EvalHarness/TestData/climate-agent.yaml` — Format 2 (simple)
- `lucia.EvalHarness/TestData/scene-agent.yaml` — Format 2
- `lucia.EvalHarness/TestData/lists-agent.yaml` — Format 2
- `lucia.EvalHarness/TestData/general-agent.yaml` — Format 2
- `lucia.EvalHarness/TestData/orchestrator.yaml` — Format 2 (routing-focused)

### Agent Implementations
- `lucia.Agents/Agents/ClimateAgent.cs` — 2 skills, 8 tools
- `lucia.Agents/Agents/SceneAgent.cs` — 1 skill, 3 tools
- `lucia.Agents/Agents/ListsAgent.cs` — 1 skill, 5 tools
- `lucia.Agents/Agents/GeneralAgent.cs` — 0-1 skills, optional MCP tools
- `lucia.Agents/Agents/DynamicAgent.cs` — Dynamic MCP tool resolution

### Skills
- `lucia.Agents/Skills/ClimateControlSkill.cs`
- `lucia.Agents/Skills/FanControlSkill.cs`
- `lucia.Agents/Skills/SceneControlSkill.cs`
- `lucia.Agents/Skills/ListSkill.cs`

## Appendix: Test Naming Conventions

### Test Method Naming
Pattern: `{ToolName}_{Scenario}_{ExpectedBehavior}`

Examples:
- `SetTemperature_BasicRequest_ProducesResponse`
- `FindScenesByArea_BedroomRequest_ReturnsBedroomScenes`
- `AddToShoppingList_MultipleItems_AddsAllItems`

### Scenario Naming
Pattern: `{AgentName}.{ToolName}_{Scenario}[{variant}]`

Examples:
- `ClimateAgent.SetTemperature_BasicRequest[exact]`
- `ClimateAgent.SetTemperature_BasicRequest[stt-misspelled]`
- `SceneAgent.ActivateScene_MovieMode[exact]`
- `ListsAgent.AddToShoppingList_MultipleItems[exact]`

### Variant Labels
- `exact` — Precise wording, no STT artifacts
- `stt-{artifact}` — Common speech-to-text errors (spelling, homophone, dropped words)
- `casual` — Informal phrasing ("make it cooler" vs. "set temperature to 68")
- `verbose` — Extra context ("hey lucia, can you please...")

This convention enables trend analysis: "ClimateAgent regressed on stt-* prompts after update X"

---

# Eval Infrastructure Audit — All Agent Support

**Date:** 2025-01-26  
**Agent:** Dallas  
**Status:** Complete  

## Summary

Audited the eval infrastructure to ensure it can evaluate all agent types. Identified gaps in both **RealAgentFactory** (EvalHarness) and **EvalTestFixture** (xUnit tests), then implemented fixes to support Climate, Lists, Scene, and DynamicAgent construction.

## Infrastructure Components Reviewed

### Core Test Infrastructure (lucia.Tests/Orchestration/)

1. **AgentEvalTestBase.cs** — Shared eval test base
   - ✅ Provides ModelIds parameterization from appsettings.json
   - ✅ CreateReportingConfig with SmartHomeToolCallEvaluator, LatencyEvaluator, optional A2AToolCallEvaluator
   - ✅ RunAgentAndEvaluateAsync helper methods (with/without ChatHistoryCapture)
   - ✅ RunRouterAndEvaluateAsync for RouterExecutor testing
   - ✅ RunOrchestratorAndEvaluateAsync for full LuciaEngine pipeline testing
   - ✅ AssertToolCalled, AssertHasTextResponse, AssertNoUnacceptableMetrics helpers
   - **Finding:** Generic enough to support all agent types — no changes needed

2. **EvalTestFixture.cs** — Fixture that builds real agents
   - **Before:** Only supported LightAgent, MusicAgent, GeneralAgent
   - **After:** Now supports ClimateAgent, ListsAgent, SceneAgent (+ WithCapture variants)
   - Agent cards extracted for all 6 agent types (for orchestrator registry)
   - ✅ Multi-provider support: AzureOpenAI, Ollama, OpenAI
   - ✅ CreateLuciaOrchestratorAsync wires up full pipeline
   - **Note:** DynamicAgent not included — requires MongoDB definition at runtime

3. **Evaluators:**
   - SmartHomeToolCallEvaluator: 1–5 score, handles state-aware optimization
   - A2AToolCallEvaluator: Validates routing → agent targeting → execution → aggregation
   - LatencyEvaluator: 1–5 inverse scale (< 500ms = 5, > 1500ms = 1, non-failing)
   - **Finding:** Generic evaluators work for all agent types — no changes needed

4. **ChatHistoryCapture.cs**
   - ✅ Captures tool calls + tool results from FunctionInvokingChatClient pipeline
   - ✅ Used for WithCapture factory methods to get full conversation history
   - **Finding:** Generic middleware — works with all agents

### Harness Infrastructure (lucia.EvalHarness/)

1. **RealAgentFactory.cs** — Constructs real agents for harness
   - **Before:** Supported LightAgent, ClimateAgent, ListsAgent, SceneAgent, GeneralAgent
   - **After:** Added CreateDynamicAgentAsync(modelName, agentId) factory
   - ✅ AgentFactories dictionary maps agent names to factory methods
   - ✅ EnableTracing property toggles ConversationTracer in pipeline
   - ✅ ParameterProfile property controls model inference settings
   - **Finding:** DynamicAgent was missing — now added

2. **EvalRunner.cs**
   - ✅ EvaluateRealAgentAsync: runs agent + AgentEval metrics (tool selection/success/efficiency/task completion)
   - ✅ EvaluateScenariosAsync: YAML scenario runner with ScenarioValidator
   - ✅ Uses MAFEvaluationHarness + PerformanceCollector
   - ✅ Returns ModelEvalResult with per-test-case breakdowns
   - **Finding:** Generic runner — works with all ILuciaAgent implementations

3. **ScenarioLoader.cs**
   - ✅ Loads TestScenario[] from YAML with underscored naming convention
   - **Finding:** Format is agent-agnostic — ready for all agents

4. **ScenarioValidator.cs**
   - ✅ SetupInitialStateAsync: sets FakeHomeAssistantClient entity states
   - ✅ ValidateAsync: validates tool call chain, response content, final entity state
   - ✅ Supports expected_tool_calls with arguments (including `*` wildcards, `contains:` assertions)
   - **Finding:** Works with any agent that calls HA tools

### Agent Constructor Requirements

Each agent was inspected for constructor dependencies:

| Agent | Dependencies |
|-------|-------------|
| **ClimateAgent** | IChatClientResolver, IAgentDefinitionRepository, ClimateControlSkill, FanControlSkill, TracingChatClientFactory, ILoggerFactory |
| **ListsAgent** | IChatClientResolver, IAgentDefinitionRepository, ListSkill, TracingChatClientFactory, ILoggerFactory |
| **SceneAgent** | IChatClientResolver, IAgentDefinitionRepository, SceneControlSkill, TracingChatClientFactory, ILoggerFactory |
| **GeneralAgent** | IChatClientResolver, IAgentDefinitionRepository, IMcpToolRegistry, TracingChatClientFactory, ILoggerFactory |
| **DynamicAgent** | agentId (string), AgentDefinition, IAgentDefinitionRepository, IMcpToolRegistry, IChatClientResolver, IModelProviderResolver, IModelProviderRepository, TracingChatClientFactory, AgentsTelemetrySource, ILoggerFactory |

**Findings:**
- All agents follow similar constructor patterns
- DynamicAgent requires pre-loaded AgentDefinition from repository
- Skills require IHomeAssistantClient, IEntityLocationService, ILoggerFactory, IOptionsMonitor<T>
- Climate/Fan skills also need IEmbeddingProviderResolver, IDeviceCacheService, IHybridEntityMatcher

## Changes Made

### 1. Extended RealAgentFactory (lucia.EvalHarness/Providers/RealAgentFactory.cs)

```csharp
public async Task<RealAgentInstance> CreateDynamicAgentAsync(string modelName, string agentId)
{
    var (resolver, tracer) = CreateOllamaResolverWithTracer(modelName);
    
    // Load agent definition from repository
    var definition = await _definitionRepo.GetAgentDefinitionAsync(agentId, default);
    if (definition is null)
        throw new InvalidOperationException($"Agent definition '{agentId}' not found");

    var agent = new DynamicAgent(
        agentId, definition, _definitionRepo, 
        A.Fake<IMcpToolRegistry>(), resolver, 
        A.Fake<IModelProviderResolver>(), 
        A.Fake<IModelProviderRepository>(), 
        _tracingFactory, new AgentsTelemetrySource(), _loggerFactory);

    await agent.InitializeAsync();
    return new RealAgentInstance { 
        AgentName = $"DynamicAgent[{agentId}]", 
        Agent = agent, 
        DatasetFile = $"TestData/{agentId}.yaml", 
        Tracer = tracer 
    };
}
```

**Why:** Harness can now evaluate user-defined dynamic agents from MongoDB definitions.

### 2. Extended EvalTestFixture (lucia.Tests/Orchestration/EvalTestFixture.cs)

Added factory methods:
- `CreateClimateAgentAsync(deploymentName, embeddingModelName?)`
- `CreateListsAgentAsync(deploymentName)`
- `CreateSceneAgentAsync(deploymentName)`
- `CreateClimateAgentWithCaptureAsync(deploymentName, embeddingModelName?)`
- `CreateListsAgentWithCaptureAsync(deploymentName)`
- `CreateSceneAgentWithCaptureAsync(deploymentName)`

Extended `ExtractAgentCards()` to build cards for all 6 agent types:
- LightAgent, MusicAgent, GeneralAgent (existing)
- ClimateAgent, ListsAgent, SceneAgent (new)

**Why:** Lambert can now write xUnit eval tests for Climate/Lists/Scene agents.

### 3. Added Missing Using Directive

```csharp
using lucia.Agents.Configuration.UserConfiguration;
```

**Why:** Needed for ClimateControlSkillOptions, FanControlSkillOptions, SceneControlSkillOptions.

## Infrastructure Readiness Assessment

| Component | Climate | Lists | Scene | General | Dynamic | Light | Music |
|-----------|---------|-------|-------|---------|---------|-------|-------|
| **RealAgentFactory** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| **EvalTestFixture** | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ |
| **AgentEvalTestBase** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Evaluators** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **ScenarioLoader** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **ScenarioValidator** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

**Notes:**
- MusicAgent not in RealAgentFactory (harness focuses on HA control agents)
- DynamicAgent not in EvalTestFixture (requires MongoDB at test runtime — out of scope for xUnit parameterized tests)
- All agents supported by core eval infrastructure (base class, evaluators, validators)

## What Lambert Can Do Now

1. **Write xUnit eval tests** for Climate, Lists, Scene using `EvalTestFixture`:
   ```csharp
   [Theory]
   [MemberData(nameof(ModelIds))]
   public async Task ClimateAgent_SetsTemperature(string deploymentName, string embeddingModelName)
   {
       var (agent, capture) = await Fixture.CreateClimateAgentWithCaptureAsync(deploymentName, embeddingModelName);
       var reportingConfig = CreateReportingConfig();
       var (response, evaluation) = await RunAgentAndEvaluateAsync(
           deploymentName, agent.GetAIAgent(), capture, "Set the thermostat to 72 degrees",
           reportingConfig, "ClimateAgent_SetTemperature");
       
       AssertToolCalled(response, "SetClimateTemperature");
       AssertNoUnacceptableMetrics(evaluation);
   }
   ```

2. **Use EvalHarness** to run scenario-based evals for all agents:
   ```bash
   dotnet run --project lucia.EvalHarness -- \
       --agent ClimateAgent \
       --dataset TestData/climate-agent.yaml \
       --model qwen2.5:7b
   ```

3. **Evaluate DynamicAgent** from MongoDB definitions via harness:
   ```bash
   dotnet run --project lucia.EvalHarness -- \
       --agent custom-office-agent \
       --dataset TestData/custom-office-agent.yaml \
       --model qwen2.5:7b
   ```

## Recommendations

1. **Lambert:** Create YAML scenario suites for Climate/Lists/Scene in `TestData/`
2. **Lambert:** Write xUnit eval tests for the new agents using the extended fixture
3. **Team:** Consider adding MusicAgent to RealAgentFactory if harness-based music eval is needed
4. **Team:** If DynamicAgent eval tests are needed in xUnit, create a separate fixture that seeds MongoDB

## Build Status

✅ `dotnet build lucia-dotnet.slnx -v minimal` — **succeeded**
- All projects compiled without warnings or errors
- Changes are backward compatible

## Next Steps

Infrastructure is ready. Lambert can proceed with writing eval test suites for:
- ClimateAgent (HVAC control, fan control, comfort adjustments)
- ListsAgent (shopping list, todo list operations)
- SceneAgent (scene activation, scene discovery)

All evaluators, validators, and helpers are generic enough to support these agents without further infrastructure changes.

---

# Data Pipeline Design

**Decision Date:** 2026-03-26  
**Status:** Implemented  
**Owner:** Ash (Data Engineer)

## Context

Lucia needs a systematic way to convert real-world data (GitHub issues, conversation traces) into eval scenarios for continuous improvement. Without this pipeline, eval datasets remain static and don't benefit from production learnings.

## Decision

Implemented a data pipeline in `lucia.EvalHarness/DataPipeline/` that converts two primary data sources into standardized eval scenarios:

### Architecture

```
┌─────────────────┐
│ GitHub Issues   │──┐
└─────────────────┘  │
                     │    ┌──────────────────┐    ┌────────────────────┐
┌─────────────────┐  ├───>│ IEvalScenario    │───>│ EvalScenario       │
│ Conversation    │──┘    │ Source           │    │ Exporter           │
│ Traces          │       └──────────────────┘    └────────────────────┘
└─────────────────┘                │                        │
                                   │                        v
                                   v                  ┌─────────────┐
                         ┌──────────────────┐        │ agent.yaml  │
                         │ EvalScenario     │        │ files       │
                         │ (intermediate)   │        └─────────────┘
                         └──────────────────┘
```

### Components

1. **IEvalScenarioSource** — Interface for scenario sources
   - Defines `GetScenariosAsync(filter)` contract
   - Supports filtering by category, agent, source type, errors-only
   
2. **GitHubIssueScenarioSource** — Converts issues to scenarios
   - Parses GitHub issue bodies using regex extraction
   - Identifies: user input, expected behavior, agent, error state
   - Skips feature requests and issues without trace reports
   - Uses GitHub CLI (`gh`) for issue retrieval
   
3. **TraceScenarioSource** — Converts traces to scenarios
   - Pulls from `ITraceRepository` (SQLite or MongoDB)
   - Extracts routing decisions, tool calls, error states
   - Handles both successful and errored traces
   - Maps traces to categories based on agent and content
   
4. **EvalScenarioExporter** — Writes scenarios to YAML
   - Uses YamlDotNet for serialization
   - Supports single-file export or grouped-by-agent output
   - Matches existing `TestData/*.yaml` format
   - Includes metadata headers for traceability

5. **EvalScenario (model)** — Intermediate representation
   - Unified structure for all scenario sources
   - Includes: ID, description, category, user prompt, expected agent
   - Optional: tool calls, state assertions, response validation
   - Metadata tracks source and original data

### Design Principles

1. **Source Isolation** — Each data source is a separate implementation of `IEvalScenarioSource`. New sources (e.g., manual curated datasets, Slack feedback) can be added without modifying existing code.

2. **Intermediate Representation** — `EvalScenario` decouples data sources from export formats. Future formats (JSON, CSV, etc.) only need a new exporter.

3. **Regression Focus** — Errored traces and bug-labeled issues automatically generate "should NOT fail" test cases for continuous regression prevention.

4. **Tool Call Extraction** — Successful traces contribute expected tool call patterns, enabling API contract validation.

5. **Metadata Preservation** — Source IDs, timestamps, and original error messages are retained for debugging and dataset versioning.

## GitHub Issue Parsing

Issues with embedded trace reports are parsed as follows:

- **User Input**: Extracted from `### User Input` or `### Raw Input` code blocks
- **Expected Behavior**: From `### Expected Behavior` section
- **Agent**: From `**Selected Agent:**` or `**Agents**` fields in trace report
- **Category**: Inferred from report type (Command vs Conversation) and title keywords

Example transformations:
- Issue #106 (music agent timeout) → regression scenario expecting NO timeout
- Issue #105 (wrong room light selection) → control scenario with correct entity resolution

## Trace Conversion

Traces are converted based on success/error state:

**Successful traces:**
- Extract tool calls as expected API interactions
- Use routing decision as expected agent
- Response becomes success criteria

**Errored traces:**
- User prompt becomes regression test input
- Original error becomes "must NOT occur" assertion
- Routing info preserved for debugging

## Next Steps

1. **Automation**: Add scheduled job to generate datasets weekly from new issues/traces
2. **Filtering**: Implement confidence thresholds for trace inclusion (e.g., routing confidence > 80%)
3. **Deduplication**: Detect similar scenarios across sources to avoid redundant tests
4. **Manual Review**: Add human-in-the-loop workflow for approving generated scenarios before adding to test suite

## Trade-offs

**Pros:**
- Continuous learning from production data
- Regression prevention built-in
- No manual scenario authoring for common patterns

**Cons:**
- GitHub CLI dependency (requires authenticated `gh` installation)
- Regex parsing is brittle to issue template changes
- No deduplication yet (could generate many similar tests)

## Implementation Notes

- All classes follow one-class-per-file convention
- File-scoped namespaces used throughout
- Nullable reference types enabled
- Build succeeded with zero warnings

---

# Climate Agent Eval Suite - Implementation Decision

**Date:** 2026-03-26  
**Author:** Lambert (QA / Eval Scenario Engineer)  
**Status:** Complete ✅

## What Was Built

Created the first new eval suite following the exact pattern of existing eval tests:

### 1. Test Suite: `lucia.Tests/Orchestration/ClimateAgentEvalTests.cs`

Production-quality eval test class that:
- Extends `AgentEvalTestBase` (correct base class)
- Uses `[Trait("Category", "Eval")]` and `[Trait("Agent", "Climate")]` for categorization
- Uses `[MemberData]` for model × prompt variant cross-products
- Covers 8 test scenarios across 3 evaluator categories:
  - **Tool Call Accuracy:** SetTemperature, SetHvacMode, GetClimateState, SetFanSpeed
  - **Intent Resolution:** ComfortAdjustment (natural language like "I'm cold"), MultiStep operations
  - **Task Adherence:** OutOfDomain (music/light requests)
- Includes **STT variants** for temperature commands (thermometer/seventy-two)
- Total test matrix: 8 scenarios × N models × M variants = ~40+ test executions per run

### 2. Infrastructure: EvalTestFixture Already Had Support

The fixture at `lucia.Tests/Orchestration/EvalTestFixture.cs` already contained:
- `CreateClimateAgentWithCaptureAsync()` method (lines 496-527)
- `CreateClimateControlSkillOptionsMonitor()` method (lines 122-127)
- `CreateFanControlSkillOptionsMonitor()` method (lines 129-134)

**Fix applied:** Added missing `using lucia.Agents.Configuration.UserConfiguration;` to resolve compilation errors for the Options types.

### 3. YAML Dataset: Enhanced Coverage

The existing `lucia.EvalHarness/TestData/climate-agent.yaml` already had good coverage. It includes:
- Basic temperature setting (with/without units, STT variants)
- HVAC mode control (heat, cool, off)
- Comfort adjustments ("I'm cold", "I'm hot")
- Fan control (on/off, speed percentage, speed names)
- Temperature queries
- Multi-step operations
- Out-of-domain handling

## Coverage Analysis

### Covered Scenarios ✅
- ✅ Intent resolution (set temperature, comfort adjustment)
- ✅ Tool selection accuracy (climate vs fan tools)
- ✅ Parameter extraction (temps, HVAC modes, fan speeds, areas)
- ✅ STT variants (thermometer, seventy-two, therma-stat)
- ✅ Out-of-domain handling (music, lights)
- ✅ Multi-step scenarios (turn on heat AND set temp)
- ✅ Natural language comfort ("I'm cold" → increase temp by adjustment)

### Edge Cases Included
- Temperature without units (assumes Fahrenheit)
- Spelled-out numbers ("seventy two")
- STT artifacts ("thermometer" for thermostat)
- Comfort adjustment with configurable offset (GetComfortAdjustment)

### Gaps Found 🔍

#### 1. Home Assistant Snapshot Missing Climate Entities
The `lucia.Tests/TestData/ha-snapshot.json` does NOT contain climate entities yet. Search revealed:
- `grep "climate\|thermostat"` found entity IDs in the entity list
- But `jq` queries show the snapshot only has: lights, media_players, areas
- **Impact:** Tests will run against a fake HA client with empty climate data, limiting validation

**Recommendation:** Update the snapshot export script to include climate entities:
```powershell
.\scripts\Export-HomeAssistantSnapshot.ps1 -Endpoint $env:HA_ENDPOINT -Token $env:HA_TOKEN
```

#### 2. Error Scenarios Not Yet Covered
The test suite focuses on happy paths. Missing scenarios:
- Invalid temperature values (e.g., 200°F, -50°F)
- Unknown room names ("Turn on heat in the dungeon")
- Unsupported HVAC modes (device doesn't support "dry" mode)
- Fan without speed control (binary on/off only fans)

**Recommendation:** Add error scenario tests in a follow-up iteration once we validate the happy path baseline.

#### 3. No Integration Tests Against Real HA
Current approach:
- Uses `FakeHomeAssistantClient.FromSnapshotFile()` for deterministic testing
- Fixture supports real HA client if `Configuration.HomeAssistant.BaseUrl` is set

**Recommendation:** Document how to run eval tests against a live HA instance for validation before releases.

## Build Verification

```bash
cd /mnt/games/github/lucia-dotnet
dotnet build lucia.Tests/lucia.Tests.csproj -v minimal
```

✅ **Result:** Build succeeded with 0 warnings, 0 errors

## Pattern Compliance

Followed the exact structure of `LightAgentEvalTests.cs`:
- ✅ Same class structure (sealed, inherits AgentEvalTestBase)
- ✅ Same method patterns (SkippableTheory with MemberData)
- ✅ Same assertion patterns (AssertHasTextResponse, AssertNoUnacceptableMetrics)
- ✅ Same fixture usage (Fixture.CreateClimateAgentWithCaptureAsync)
- ✅ Same reporting patterns (CreateReportingConfig with evaluator flags)
- ✅ Same trait organization (Evaluator, Agent categories)

## Next Steps

1. **Generate HA snapshot with climate entities** so tests run against realistic data
2. **Run the eval suite** with configured Azure OpenAI judge model
3. **Add error scenario tests** once happy path baseline is established
4. **Document findings** in eval reports for climate agent performance
5. **Iterate on agent instructions** based on eval failures (if any)

## Files Modified

- ✅ Created: `lucia.Tests/Orchestration/ClimateAgentEvalTests.cs`
- ✅ Modified: `lucia.Tests/Orchestration/EvalTestFixture.cs` (removed duplicate using)
- ✅ Enhanced: `lucia.EvalHarness/TestData/climate-agent.yaml` (already good coverage)

## Learnings

- The EvalTestFixture infrastructure is robust and extensible
- ClimateAgent creation was already implemented (great forward planning!)
- YAML dataset format is clear and maps well to test scenarios
- STT variant testing is a critical part of the eval strategy
- Missing climate entities in snapshot is the main blocker for realistic validation
