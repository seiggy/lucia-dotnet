# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant, built on .NET 10/C# 14 with Microsoft Agent Framework
- **Stack:** .NET 10, C# 14, Aspire 13, Ollama (local LLMs), Azure OpenAI (judge), xUnit, AgentEval, MongoDB/SQLite, Redis/InMemory
- **Created:** 2026-03-26

## Key Architecture

- **Agents:** LightAgent, ClimateAgent, ListsAgent, SceneAgent, GeneralAgent, DynamicAgent, OrchestratorAgent, MusicAgent (separate project)
- **Eval Infrastructure:** lucia.EvalHarness (TUI + reports), lucia.Tests/Orchestration/ (xUnit eval tests)
- **Eval Base:** AgentEvalTestBase provides shared helpers, DiskBasedReportingConfiguration, assertion methods
- **Test Fixture:** EvalTestFixture creates real agents backed by Ollama or Azure OpenAI
- **Trace Pipeline:** TraceCaptureObserver → MongoTraceRepository/SqliteTraceRepository → JsonlConverter for fine-tuning exports
- **YAML Datasets:** light-agent.yaml, climate-agent.yaml, lists-agent.yaml, scene-agent.yaml, general-agent.yaml, orchestrator.yaml

## Current Eval Coverage Gaps

- No eval suites for: ClimateAgent, ListsAgent, SceneAgent, GeneralAgent, DynamicAgent
- No GitHub issue ingestion pipeline
- No trace→eval-scenario conversion
- No eval spec document

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2025-10-13: Eval Expansion Architecture

**Standard Eval Suite Pattern:**
- Each agent needs 3 files: `{AgentName}EvalTests.cs`, factory method in `EvalTestFixture`, factory method in `RealAgentFactory`
- Test methods follow trait-based categorization: `[Trait("Evaluator", "ToolCallAccuracy|IntentResolution|TaskAdherence")]`
- Naming convention: `{ToolName}_{Scenario}_{ExpectedBehavior}` for methods, `{AgentName}.{ToolName}_{Scenario}[{variant}]` for scenario names
- `WithVariants` pattern enables STT robustness testing without duplicating test logic
- `ChatHistoryCapture` wrapper required for `ToolCallAccuracyEvaluator` to see intermediate tool calls consumed by `FunctionInvokingChatClient`

**DynamicAgent Special Considerations:**
- Tools resolved from MCP registry at initialization, not compile-time — requires different eval approach
- Use in-memory `IAgentDefinitionRepository` fake to avoid MongoDB container dependency in tests
- Eval focus: tool resolution correctness, hot-reload, definition loading — NOT specific tool call validation
- Test data is agent definitions (as `[MemberData]`), not user prompts like other agents

**YAML Dataset Formats:**
- Format 1 (structured scenarios): Complex state-dependent tests with `initial_state`, `expected_final_state`, `expected_tool_calls` (light-agent.yaml)
- Format 2 (simple): Simpler `input`/`expected`/`criteria` structure for most agents (climate, scene, lists, general, orchestrator)
- Format 2 recommended for new datasets — easier to generate from traces/issues, TUI harness supports both

**Trace Scoring Heuristics (for eval-worthiness):**
- User feedback (thumbs up/down): 1.0/0.8 — explicit signal of quality
- Execution errors (!success): 0.7 — regression prevention
- Low routing confidence (<0.6): 0.5 — ambiguous cases worth capturing
- Multi-turn corrections (>1 turn): 0.4 — user had to rephrase
- Threshold: 0.5 minimum score for trace → scenario conversion

**GitHub Issue → Scenario Quality Gates:**
- LLM confidence must be ≥ 0.7 for auto-generated scenarios
- Human review required for all generated scenarios before merge (via PR workflow)
- Scenarios should test expected behavior, not just reproduce the bug
- Add `metadata.category: regression` and `metadata.github_issue: "{number}"` for traceability

**Infrastructure Stability Wins:**
- `AgentEvalTestBase` provides all common helpers — no duplication across test suites
- `EvalTestFixture` and `RealAgentFactory` mirror each other (xUnit vs. TUI) — same construction pattern ensures consistency
- `DiskBasedReportingConfiguration` enables `dotnet aieval report` integration without custom tooling
- Stable execution name (`s_executionName`) groups all scenarios in single report run

**Coverage Gaps Identified:**
- 2/8 agents with full coverage (Light, Music), 5/8 with datasets only (Climate, Scene, Lists, General, Orchestrator), 1/8 with no coverage (Dynamic)
- No systematic GitHub issue → eval scenario pipeline (bugs likely to recur)
- No production trace → eval scenario conversion (missing real-world usage patterns)
- MusicAgent has xUnit suite but no YAML dataset (inverse of other agents)

### 2026-03-26: LightAgentEvalTests Deep Audit

**Key Finding: xUnit eval tests are smoke tests, not real evals.**

Every test in `LightAgentEvalTests.cs` only asserts `AssertHasTextResponse()` + `AssertNoUnacceptableMetrics()`. No test uses the available `AssertToolCalled()` helper. No test verifies entity resolution, parameter extraction, or state changes. The LLM judge (SmartHomeToolCallEvaluator) provides a 1-5 score but `AssertNoUnacceptableMetrics` only catches scores ≤1, meaning "Poor" (2/5) passes.

**Contrast with EvalHarness:** The TUI harness (`lucia.EvalHarness`) tests ARE specific — they check exact tool names, parameters, and expected state. The xUnit suite is dramatically less useful for model debugging.

**Infrastructure issues found:**
- Azure API key in appsettings.json is placeholder — all Azure-backed tests fail with 401
- `ControlLightsAsync` vs `ControlLights` name mismatch appears in harness failures (test infra bug, not model bug)
- Judge model requires Azure OpenAI — Ollama-only environments can't score

**EvalHarness results (most recent):** granite4:350m scored 37.1/100 on LightAgent (18% pass), gemma3:270m scored 18.2/100. GeneralAgent (no tools) scores 84-87 across both models.

**Full audit written to:** `.squad/decisions/inbox/ripley-light-eval-audit.md`

### 2025-10-13: Orchestration Simplification Investigation

**Hypothesis Investigated:** Remove A2A setup and simplify to agents-as-tools where orchestrator receives dynamic collection of agents as its tools.

**Key Findings:**

1. **Current Architecture Complexity Source:**
   - A2A infrastructure (RemoteAgentInvoker, A2AHost, mesh deployment) adds ~2,000 lines of code
   - **Standalone mode (default)** uses LocalAgentInvoker exclusively — A2A is not used in production
   - Real complexity is in unused deployment flexibility, not orchestration pattern itself

2. **Agents-as-Tools Feasibility:**
   - **Not viable** with Microsoft Agent Framework — agents are not composable as tools
   - Framework expects `AIFunction` instances (typed parameters, synchronous/async methods)
   - Agents take unstructured text and return chat responses (incompatible signatures)
   - Critical losses: parallel execution (sequential tool calls), multi-agent coordination, explicit routing cache, streaming observability

3. **Dynamic Agents + MCP Compatibility:**
   - DynamicAgent uses MCP tools internally (via IMcpToolRegistry)
   - Works perfectly as orchestration target via LocalAgentInvoker
   - No benefit from agents-as-tools pattern — would risk breaking MCP integration

4. **Incremental Simplification Path (Recommended):**
   - Remove A2A infrastructure entirely (RemoteAgentInvoker, A2AHost, mesh mode config)
   - Keep LuciaEngine workflow orchestration (RouterExecutor → AgentDispatchExecutor → LocalAgentInvoker)
   - Preserves: parallel execution, multi-agent coordination, routing cache, streaming traces, dynamic agents + MCP
   - Benefits: -2,000 lines code, single-process deployment, zero network latency, reduced operational complexity
   - Effort: 1-2 weeks vs. 4-6 weeks for full rewrite

5. **Architecture Insight:**
   - Workflow pattern (Router → Dispatch → Aggregate) is not the complexity source
   - Pattern enables parallel agent execution, tailored instructions per agent, explicit routing decisions
   - LocalAgentInvoker already eliminates HTTP overhead for in-process agents
   - Removing workflow layer would lose critical capabilities for marginal simplification

**Recommendation:** Incremental simplification — delete A2A/mesh infrastructure, keep workflow orchestration. POC scope: Phase 1 (A2A removal), Phase 2 (registry simplification), Phase 3 (WorkflowFactory cleanup). Total effort: 1-2 weeks.

**Full analysis written to:** `.squad/decisions/inbox/ripley-orchestration-simplification.md`

### 2025-10-13: MAF Workflows Deep-Dive — Can We Replace the Orchestration Engine?

**Context:** Zack corrected previous assumption that MAF doesn't support nested agents. MAF DOES support nested agents through Workflows. Question: Can we replace the orchestration engine with a dynamic workflow service?

**Key Discovery: We're Already Using MAF Workflows!**

The current orchestration IS a MAF Workflow:
- `RouterExecutor`, `AgentDispatchExecutor`, `ResultAggregatorExecutor` all extend `Microsoft.Agents.AI.Workflows.Executor`
- `WorkflowFactory.BuildAndExecuteAsync` uses `WorkflowBuilder` to connect them: Router → Dispatch → Aggregator
- `InProcessExecution.RunAsync` handles workflow execution with type-safe data flow

**Architecture:**
- 18 files, ~3,000 lines in `lucia.Agents/Orchestration/`
- Core pattern: LuciaEngine (session lifecycle) → WorkflowFactory (workflow construction) → Custom Executors (business logic)
- RouterExecutor (432 lines): LLM-based routing + prompt cache for exact match + semantic similarity
- AgentDispatchExecutor (300 lines): Parallel agent execution via `Task.WhenAll`, clarification handling
- ResultAggregatorExecutor (270 lines): Response aggregation + optional personality rewriting via LLM
- LocalAgentInvoker (187 lines): In-process agent execution via `AIHostAgent` with session persistence

**MAF Workflow Capabilities:**
- Graph execution: DAG with automatic data flow between executors
- Type safety: WorkflowBuilder validates input/output type compatibility
- Event system: ExecutorInvokedEvent, WorkflowOutputEvent, WorkflowErrorEvent for telemetry
- Conditional routing: `AddSwitch` for branching based on data
- Fan-out/fan-in: Multiple edges for parallel execution (we use internal parallelism in AgentDispatchExecutor instead)
- Nested workflows: `workflow.BindAsExecutor()` for composition
- Durable workflows: Azure Functions + Durable Task Scheduler integration (not used, we're in-process only)

**Answer: YES, we can replace the orchestration with a dynamic workflow service, with caveats.**

**Simplification Opportunity:**
- Replace `WorkflowFactory` (349 lines) with `DynamicOrchestrationWorkflow` service (~250 lines)
- Consolidate agent discovery logic (ResolveAgentsAsync, CreateAgentInvokers) into one place
- Cache stateless executors (Router, Aggregator) for reuse across requests
- **Complexity reduction: ~10-15% (100-150 lines), not 100%**

**What Must Stay:**
- All three custom Executors (Router, Dispatch, Aggregator) — they contain the orchestration business logic
- LocalAgentInvoker — in-process agent execution with session persistence + NeedsInput detection
- SessionManager — Redis-backed session/task lifecycle
- LuciaEngine — session management, observer coordination, error handling

**What the Workflow Gives Us for FREE:**
- Type-safe data flow between executors
- Event system for telemetry
- Error isolation (ExecutorFailedEvent doesn't crash workflow)
- Execution lifecycle management
- Future extensibility (add pre/post-processing executors without refactoring)

**What We're Still Building:**
- Routing logic (LLM call + prompt cache)
- Agent execution (parallel invocation + clarification)
- Response aggregation (combining + personality rewriting)
- Session management (multi-turn context, Redis persistence)
- Agent resolution (ILuciaAgent, IDynamicAgentProvider, remote agents)

**Dynamic Agent + MCP Compatibility:**
- **No impact.** DynamicAgent.GetAIAgent() returns AIAgent with MCP tools resolved at initialization
- LocalAgentInvoker wraps AIAgent in AIHostAgent and executes it
- Workflow is oblivious to static vs dynamic agents

**Parallel Execution:**
- **Preserved.** AgentDispatchExecutor uses `Task.WhenAll` for internal parallelism
- Alternative (future): Use MAF fan-out pattern (Router → [Agent1, Agent2, Agent3] → Aggregator)
- Recommendation: Keep internal parallelism — simpler and more efficient (avoids per-request workflow rebuilding)

**Streaming + Multi-Turn:**
- **Preserved.** Streaming happens at agent level (AIHostAgent) and orchestrator level (OrchestratorAgent)
- Multi-turn context managed via LuciaEngine (SessionData from Redis) + LocalAgentInvoker (a2a.contextId propagation)
- Orthogonal to workflow structure

**Migration Path:**
1. Phase 1: Refactor WorkflowFactory → DynamicOrchestrationWorkflow (Week 1) — consolidate logic, no behavior change
2. Phase 2 (optional): Optimize executor caching (Week 2) — singleton Router/Aggregator, per-request Dispatch
3. Phase 3 (separate): Remove A2A infrastructure (per previous decision) — saves ~2,000 lines

**POC Scope:**
- Create DynamicOrchestrationWorkflow service
- Update LuciaEngine to use it
- Run all existing tests (xUnit eval tests, TUI harness, integration tests)
- Verify: routing, prompt cache, multi-agent, clarification, DynamicAgent + MCP, personality, streaming traces
- Success criteria: All tests pass, no performance regression, 10-15% code reduction
- Estimated effort: 3-5 days

**Risk Assessment:**
- Low risk: Already using MAF Workflows, this is refactoring not rewriting, strong test coverage
- Medium risk: DI lifecycle changes (executor caching), session state interactions, observer integration
- Mitigation: Test concurrency, keep session management outside workflow, ensure events still captured

**Recommendation:**
- **Proceed with Phase 1** — DynamicOrchestrationWorkflow refactor
- Why: 10-15% complexity reduction, no risk, foundation for future optimization, aligns with Zack's vision
- Why not full rewrite: Custom executors contain business logic (must stay), A2A infrastructure is the real complexity source (separate decision), incremental value meaningful but not transformative

**Full proposal written to:** `.squad/decisions/inbox/ripley-maf-workflow-proposal.md`

### 2025-10-13: MAF Workflows v2 Research — Dynamic Workflow Service Feasibility

**Context:** Zack corrected previous assumption — MAF DOES support nested agents through Workflows. Question: Can we simplify the orchestration by replacing the engine with a dynamic workflow service?

**Critical Discovery: We're ALREADY Using MAF Workflows Correctly!**

The current orchestration (WorkflowFactory + Custom Executors) is the proper MAF Workflows pattern:
- `RouterExecutor`, `AgentDispatchExecutor`, `ResultAggregatorExecutor` all extend `Microsoft.Agents.AI.Workflows.Executor`
- `WorkflowFactory.BuildAndExecuteAsync` uses `WorkflowBuilder` to connect them: Router → Dispatch → Aggregator
- `InProcessExecution.RunAsync` handles workflow execution with type-safe data flow
- Event system (`ExecutorInvokedEvent`, `WorkflowOutputEvent`, `WorkflowErrorEvent`) provides telemetry for free

**MAF Workflows Capabilities (via Context7 research):**
- Graph execution: DAG with automatic data flow between executors
- Type safety: WorkflowBuilder validates input/output type compatibility
- Event system: Built-in telemetry hooks
- Conditional routing: `AddSwitch` for branching based on data
- Fan-out/fan-in: Multiple edges for parallel execution
- Nested workflows: `workflow.BindAsExecutor()` for composition
- Durable workflows: Azure Functions + Durable Task Scheduler integration (not used, we're in-process only)
- Streaming support: Works with streaming agents transparently

**What MAF Workflows Doesn't Provide (we must build):**
- Routing logic (LLM call + prompt cache + semantic similarity)
- Agent execution (parallel invocation + clarification handling)
- Response aggregation (combining + personality rewriting)
- Session management (multi-turn context, Redis persistence)
- Agent resolution (ILuciaAgent, IDynamicAgentProvider, remote agents)

**Complexity Analysis (18 files, 2,753 total lines):**

| Component | Lines | Purpose | Can Delete? |
|-----------|-------|---------|-------------|
| RouterExecutor | 431 | LLM routing + prompt cache | ❌ Business logic |
| WorkflowFactory | 348 | Agent resolution + workflow construction | ✅ Refactor to ~250 lines |
| AgentDispatchExecutor | 299 | Parallel execution + clarification | ❌ Business logic |
| LuciaEngine | 277 | Session lifecycle + observer coordination | ❌ Core orchestration |
| ResultAggregatorExecutor | 269 | Response aggregation + personality | ❌ Business logic |
| LocalAgentInvoker | 186 | In-process agent execution | ❌ Core invocation |
| RemoteAgentInvoker | 154 | A2A HTTP invocation | ✅ Phase 3 (A2A removal) |
| SessionManager | 145 | Redis-backed session/task lifecycle | ❌ State management |
| Other | 644 | Options, models, observers, telemetry | ❌ Required support |

**Answer: YES, we can simplify with DynamicOrchestrationWorkflow service, with caveats.**

**Simplification Opportunity:**
- Replace `WorkflowFactory` (348 lines) with `DynamicOrchestrationWorkflow` service (~250 lines)
- Consolidate agent discovery logic (ResolveAgentsAsync, CreateAgentInvokers) into one place
- Cache stateless executors (Router, Aggregator) for reuse across requests
- **Complexity reduction: ~10-15% (100-150 lines), not 100%**

**What Must Stay:**
- All three custom Executors (Router, Dispatch, Aggregator) — they contain the orchestration business logic
- LocalAgentInvoker — in-process agent execution with session persistence + NeedsInput detection
- SessionManager — Redis-backed session/task lifecycle
- LuciaEngine — session management, observer coordination, error handling

**What the Workflow Gives Us for FREE:**
- Type-safe data flow: `ChatMessage → AgentChoiceResult → List<OrchestratorAgentResponse> → OrchestratorResult`
- Event system for telemetry (no manual event emitting needed)
- Error isolation (`ExecutorFailedEvent` doesn't crash workflow)
- Execution lifecycle management
- Future extensibility (add pre/post-processing executors without refactoring)

**Dynamic Agent + MCP Compatibility:**
- **No impact.** DynamicAgent.GetAIAgent() returns AIAgent with MCP tools resolved at initialization
- LocalAgentInvoker wraps AIAgent in AIHostAgent and executes it
- Workflow is oblivious to static vs dynamic agents
- MCP tool registry, tool resolution, hot-reload — all orthogonal to workflow structure

**Parallel Execution:**
- **Preserved.** AgentDispatchExecutor uses `Task.WhenAll` for internal parallelism
- Alternative (future): Use MAF fan-out pattern (Router → [Agent1, Agent2, Agent3] → Aggregator)
- **Recommendation:** Keep internal parallelism — simpler and more efficient (avoids per-request workflow rebuilding)

**Streaming + Multi-Turn:**
- **Preserved.** Streaming happens at agent level (AIHostAgent) and orchestrator level (OrchestratorAgent)
- Multi-turn context managed via LuciaEngine (SessionData from Redis) + LocalAgentInvoker (a2a.contextId propagation)
- Orthogonal to workflow structure

**Migration Path:**
1. Phase 1: Refactor WorkflowFactory → DynamicOrchestrationWorkflow (Week 1) — consolidate logic, executor caching, no behavior change
2. Phase 2 (optional): Optimize executor caching (Week 2) — thread-safety analysis, DI lifecycle optimization
3. Phase 3 (separate): Remove A2A infrastructure (per previous decision) — saves ~154 lines

**POC Scope (3-5 days):**
- Create DynamicOrchestrationWorkflow service
- Consolidate agent resolution logic
- Implement executor caching (Router/Aggregator singletons)
- Update LuciaEngine to use new service
- Run all tests (xUnit eval tests, TUI harness, integration tests)
- Verify: routing, prompt cache, multi-agent, clarification, DynamicAgent + MCP, personality, streaming traces
- Success criteria: All tests pass, no performance regression, 100-150 line reduction

**Risk Assessment:**
- Low risk: Already using MAF Workflows, this is refactoring not rewriting, strong test coverage
- Medium risk: DI lifecycle changes (executor caching), session state interactions, observer integration
- Mitigation: Test concurrency, keep session management outside workflow, ensure events still captured

**Recommendation:**
- **Proceed with Phase 1** — DynamicOrchestrationWorkflow refactor
- Why: 10-15% complexity reduction, no risk, foundation for future optimization, aligns with Zack's vision, single-responsibility principle
- Why not full rewrite: Custom executors contain business logic (must stay), A2A infrastructure is the real complexity source (separate decision), incremental value meaningful but not transformative
- **The real win is architectural clarity, not line count**

**Full proposal written to:** `.squad/decisions/inbox/ripley-maf-workflow-v2.md`

### 2025-01-13: Entity Resolution Architecture — Cascading Elimination Pipeline

**Current State Analysis:**
- **EntityLocationService** uses global hybrid scoring (HybridEntityMatcher) that combines embedding cosine similarity + string-level similarity (Levenshtein + token-core + phonetic Metaphone)
- **SearchHierarchyAsync** searches floors/areas/entities in parallel, compares best scores across levels to decide resolution path (entity vs area vs floor)
- **DirectSkillExecutor** uses fast-path **ExactMatchEntities** which does: exact entity_id lookup, exact area name match, exact friendly_name match
- **Limitation:** No fuzzy matching in fast-path, no STT artifact handling, no multi-match disambiguation
- **Context Available:** ConversationContext.DeviceArea from Wyoming voice platform (caller location), but not fully leveraged

**Cache Structure:**
- **LocationSnapshot** (immutable, swapped atomically via Volatile): ImmutableArray of Floors/Areas/Entities + ImmutableDictionary by ID
- **FloorInfo:** FloorId, Name, Aliases, Level, Icon, NameEmbedding, PhoneticKeys, AliasPhoneticKeys
- **AreaInfo:** AreaId, Name, FloorId, Aliases, EntityIds, Icon, Labels, NameEmbedding, PhoneticKeys, AliasPhoneticKeys
- **HomeAssistantEntity:** EntityId, FriendlyName, Domain, Aliases, AreaId, Platform, SupportedFeatures, IncludeForAgent (visibility), PhoneticKeys
- **Key Constraint:** No STT confidence score — HA strips it from Wyoming before reaching us

**Cascading Entity Resolver Design:**
1. **Step 1: Query Decomposition (deterministic NLP)** — Extract action, explicit location, device type, detect complexity (temporal, conditional, color)
2. **Step 2: Location Grounding** — Explicit location in query → use it; else use callerArea from context; else no location context
3. **Step 3: Domain Filtering** — Filter cached entities: domain ∈ [light, switch] based on detected device type + skill filter
4. **Step 4: Entity Matching** — Exact/normalized friendly_name, phonetic keys (STT artifacts), partial token match
5. **Decision Tree:** 1 match → resolve; N matches same area → resolve all; 0 matches → bail to LLM (NoMatch); N matches multiple areas → bail to LLM (Ambiguous)

**Key Architecture Decisions:**
- **Zero matches from cascade = uncertainty signal** → hand off to LLM orchestrator (handles garbled STT, unusual requests)
- **Complex commands (temporal, multi-domain, compound) → always LLM** — detected in Step 1 and bail immediately
- **Fast-path must be <50ms** — pure string/dictionary operations, no embeddings
- **Keep embeddings for LLM agent path only** — SearchHierarchyAsync still used when cascade bails
- **DeviceArea context is primary grounding signal** — overridden only by explicit location in query

**Migration Strategy:**
- Phase 1: Parallel execution with feature flag for telemetry comparison
- Phase 2: Switch default after validating <5% LLM fallback rate regression
- Phase 3: Remove feature flag, keep SearchHierarchyAsync for LLM agents
- **Do NOT remove HybridEntityMatcher** — still needed for LLM agent fuzzy search

**Performance Target:** <50ms cache-hit resolution (Step 1: <5ms, Step 2: <5ms, Step 3: <10ms, Step 4: <30ms)

**Integration Points:**
- DirectSkillExecutor: Replace ResolveSearchTermsToEntityIds/ResolveEntityIdFromCache with ICascadingEntityResolver.Resolve()
- ConversationCommandProcessor: Already has HandleLlmFallbackAsync() for bail path
- LuciaEngine: Receives full context when cascade bails (original query, caller area, full entity/area lists)

**Test Coverage:**
- Unit tests: QueryDecomposer, Location Grounding, Domain Filtering, Entity Matching (exact/phonetic/token)
- Integration tests: End-to-end cascade scenarios, bail conditions, performance benchmarks (<50ms p99)
- Telemetry: cascade.resolution.duration_ms, cascade.bail.count (by BailReason), llm.fallback.rate

**Full specification written to:** `.squad/decisions/inbox/ripley-cascading-entity-spec.md`
