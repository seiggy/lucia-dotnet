# Project Context

- **Owner:** Zack Way
- **Project:** lucia-dotnet — Privacy-first multi-agent AI assistant for Home Assistant
- **Stack:** .NET 10, C# 14, MongoDB/SQLite (trace storage), GitHub API, OpenTelemetry
- **Created:** 2026-03-26

## Key Files I Build On

- `lucia.Agents/Training/TraceCaptureObserver.cs` — captures conversation traces
- `lucia.Agents/Training/ConversationTrace.cs` — trace data model (includes RoutingDecision, TracedToolCall, TraceLabel)
- `lucia.Agents/Training/JsonlConverter.cs` — trace→JSONL export
- `lucia.Agents/Training/TraceRetentionService.cs` — trace cleanup
- `lucia.Data/Sqlite/SqliteTraceRepository.cs` — SQLite trace storage
- `lucia.Agents/Training/MongoTraceRepository.cs` — MongoDB trace storage
- `lucia.AgentHost/Apis/DatasetExportApi.cs` — dataset export REST API

## Current State

- Trace capture works end-to-end: observer → repository → JSONL export
- Traces have labels, routing decisions, tool call history, performance data
- No GitHub issue ingestion exists yet
- No trace→eval-scenario conversion exists yet
- Dataset export API exists but only for JSONL fine-tuning format

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### 2026-03-26: Data Pipeline Implementation

**What I Built:**
- Complete data pipeline architecture in `lucia.EvalHarness/DataPipeline/`
- `IEvalScenarioSource` — source abstraction for extensibility
- `GitHubIssueScenarioSource` — parses GitHub issues with trace reports into eval scenarios
- `TraceScenarioSource` — converts conversation traces from repository into scenarios
- `EvalScenarioExporter` — exports scenarios to YAML format matching existing test structure
- `EvalScenario` model — intermediate representation decoupling sources from export formats

**Key Learnings:**
1. GitHub issues with embedded trace reports follow consistent markdown structure
   - Trace reports contain: user input, agent selection, tool calls, errors
   - Regex extraction works for current template format
   - 10+ actionable issues found with trace data (issues #103-107, others)

2. Conversation traces have rich data for eval generation:
   - Routing decisions provide expected agent mapping
   - Tool calls from successful executions become expected API interactions
   - Errored traces automatically become regression test cases
   - Metadata (timestamp, duration, confidence) preserved for debugging

3. YAML export format matches existing TestData structure:
   - Scenarios have: id, description, category, user_prompt, expected_agent
   - Optional fields: tool calls, response assertions, state expectations
   - YamlDotNet handles serialization with underscore naming convention

4. Design allows for easy extension:
   - New sources implement `IEvalScenarioSource` interface
   - New export formats only need new exporter class
   - Filter criteria support category, agent, source type, errors-only

**Build Status:** ✅ Clean build, zero warnings

**Next Steps for Pipeline:**
- Add automation: scheduled dataset generation from new data
- Implement deduplication to avoid redundant scenarios
- Add confidence thresholds for trace inclusion
- Create human-in-the-loop approval workflow

### 2026-03-26: Light Agent Pain Map Analysis

**What I Analyzed:**
- 100+ GitHub issues (6 directly light-agent-related: #105, #103, #84, #83, #38, #71)
- 8 eval trace files across 2 models (granite4:350m and gemma3:270m), 77 test executions
- 11 YAML scenarios in light-agent.yaml
- xUnit tests in LightAgentEvalTests.cs and FindLightSkillEvalTests.cs
- LightAgent.cs system prompt and LightControlSkill.cs tool definitions

**Key Findings:**
1. granite4:350m consistently picks GetLightsState instead of ControlLights for "turn on" commands — wrong tool selection is the #1 failure mode
2. Color parameter extraction is 100% broken — models put color name in `state` field instead of `color` field
3. gemma3:270m is too small for tool-calling — 0 tool calls on 9/11 scenarios
4. Real users report entity resolution failures: "front room" and "dining room" don't match despite areas existing (#105, #103)
5. Non-English users are blocked — orchestrator translates entity names, breaking entity resolution (#84)
6. Eval infrastructure has a tool-name mismatch bug: YAML expects `ControlLightsAsync` but models emit `ControlLights`, inflating failure counts by ~50%
7. Major testing gaps: no multi-room, relative brightness, color temperature, toggle, non-English, bulk operation, or error recovery scenarios

**Deliverable:** `.squad/decisions/inbox/ash-light-agent-pain-map.md`
