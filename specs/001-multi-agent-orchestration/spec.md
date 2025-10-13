# Feature Specification: Multi-Agent Orchestration

**Feature Branch**: `001-multi-agent-orchestration`  
**Created**: 2025-10-13  
**Status**: Draft  
**Input**: Multi-agent orchestration with Microsoft Agent Framework workflows for automatic agent routing, multi-domain coordination, and durable task persistence using Redis

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Automatic Agent Routing (Priority: P1)

As a privacy-focused homeowner, I want Lucia to automatically determine which agent should handle my request so that I can use natural language commands without manually configuring which agent to use.

**Why this priority**: This is the core value proposition that differentiates Lucia from simple keyword-based systems. Without automatic routing, users must manually select agents in the Home Assistant UI, creating a poor user experience that blocks basic functionality.

**Independent Test**: Send a single-domain voice/text command (e.g., "Turn on the kitchen lights") through the JSON-RPC endpoint and verify that the correct agent is selected, executes the command, and returns a natural language response - all without manual agent configuration.

**Acceptance Scenarios**:

1. **Given** a user with multiple agents available (light, music, climate), **When** they say "Turn on the kitchen lights", **Then** the RouterExecutor selects the light-agent, the lights turn on, and the user receives confirmation "I've turned on the kitchen lights"
2. **Given** a user requests "Play some jazz music", **When** the request is processed, **Then** the music-agent is selected and jazz music begins playing with appropriate confirmation
3. **Given** a user asks "Set temperature to 72 degrees", **When** processed, **Then** the climate-agent is selected and the thermostat is adjusted

---

### User Story 2 - Context-Preserving Conversation Handoffs (Priority: P2)

As an automation enthusiast, I want conversations to maintain context when I shift between topics so that I don't have to repeat myself or manually switch agents.

**Why this priority**: This enables natural multi-turn conversations that feel intelligent and fluid. It's what makes Lucia feel like an assistant rather than a command interface, though it's secondary to basic routing functionality.

**Independent Test**: Conduct a multi-turn conversation where the first request targets one agent (e.g., "Turn on the bedroom lamp" → light-agent) and the second request shifts to another agent while preserving location context (e.g., "Now play some classical music" → music-agent with bedroom context).

**Acceptance Scenarios**:

1. **Given** a user has just asked "Turn on the bedroom lamp", **When** they follow up with "Now play some classical music", **Then** the music starts playing in the bedroom and context (location) is preserved through the taskId
2. **Given** a user asks "Dim the lights", **When** they follow up with "That's good, now what's the temperature?", **Then** the system switches from light-agent to climate-agent while maintaining conversation thread
3. **Given** a multi-turn conversation, **When** the topic shifts multiple times, **Then** each agent receives appropriate context from previous turns

---

### User Story 3 - Multi-Domain Coordination (Priority: P3)

As a power user, I want to issue commands that require multiple agents working together so that I can create complex home automation scenarios with a single natural language command.

**Why this priority**: This enables advanced automation scenarios but is not required for basic functionality. It represents the full vision of multi-agent orchestration but can be delivered after core routing works.

**Independent Test**: Send a request that explicitly requires two agents (e.g., "Dim the living room lights to 30% and play relaxing jazz") and verify that both agents execute in coordination and return a unified response.

**Acceptance Scenarios**:

1. **Given** a user wants to set up a relaxation environment, **When** they say "Dim the living room lights to 30% and play relaxing jazz", **Then** both light-agent and music-agent execute, both actions complete, and a unified response is returned
2. **Given** a user says "I'm going to bed", **When** processed, **Then** multiple agents coordinate to turn off downstairs lights, arm security, adjust bedroom temperature, and dim bedroom lights (sequential workflow)
3. **Given** a multi-agent request where one agent fails, **When** processed, **Then** successful agent actions complete and the user is informed of what worked and what failed

---

### User Story 4 - Durable Task Persistence (Priority: P2)

As the operator of Lucia, I need long-running orchestrated conversations to survive process restarts so that multi-turn workflows can resume without losing user context or requiring the user to start over.

**Why this priority**: Essential for production reliability and user trust. Without persistence, any restart loses conversation context, forcing users to restart their interactions. This is frustrating and breaks the assistant experience.

**Independent Test**: Start a multi-turn conversation that creates a taskId, restart the agent host process, then send a follow-up message with the same taskId and verify that the conversation context is restored and the request is handled appropriately.

**Acceptance Scenarios**:

1. **Given** a user has started a conversation with taskId "abc-123", **When** the host process restarts and a follow-up request arrives with taskId "abc-123", **Then** the conversation context is restored from Redis and the request is processed with full context
2. **Given** multiple active conversations with different taskIds, **When** the system restarts, **Then** all task contexts are available for restoration when subsequent messages arrive
3. **Given** a task context that has exceeded its TTL, **When** a follow-up request arrives, **Then** the system gracefully handles the expired context and starts a fresh conversation

---

### Edge Cases

- What happens when the RouterExecutor cannot confidently determine which agent to use (ambiguous request)?
- How does the system handle requests when no suitable specialized agent is available?
- What happens when an agent times out or fails during execution?
- How does the system handle concurrent requests for the same taskId?
- What happens when Redis is unavailable and task persistence fails?
- How does the system handle malformed or invalid taskId values?
- What happens when an agent is removed from the registry mid-conversation?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST implement a RouterExecutor that analyzes user requests using an IChatClient (configurable LLM/SLM) and returns a structured AgentChoiceResult containing agent ID, confidence score, and reasoning
- **FR-002**: System MUST query the AgentRegistry for available agents and their capabilities when making routing decisions
- **FR-003**: System MUST route requests to the selected agent using workflow conditional edges based on the agent ID returned from RouterExecutor
- **FR-004**: System MUST wrap all agents in AgentExecutorWrapper components that handle context propagation, telemetry, and error handling
- **FR-005**: System MUST preserve conversation context across agent boundaries using taskId as the conversation identifier
- **FR-006**: System MUST aggregate responses from one or more agents and format them into natural language confirmations via ResultAggregatorExecutor
- **FR-007**: System MUST persist task context to Redis with a configurable TTL for durable conversation state
- **FR-008**: System MUST restore task context from Redis when processing follow-up requests with an existing taskId
- **FR-009**: System MUST implement LuciaOrchestrator as an A2A-compliant agent that exposes itself through the AgentRegistry and integrates A2A message delivery with the workflow TaskManager
- **FR-010**: System MUST provide an AgentCard resolver that checks the local AgentCatalog before creating remote A2A clients
- **FR-011**: System MUST emit OpenTelemetry spans, metrics, and structured logs for all orchestration operations
- **FR-012**: System MUST handle agent execution timeouts gracefully with configurable timeout thresholds
- **FR-013**: System MUST provide graceful fallback responses when no suitable agent is available (RouterExecutor returns fallback AgentChoiceResult when confidence is below threshold or no agents match)
- **FR-014**: System MUST support both single-agent and multi-agent execution patterns through the workflow engine (AgentDispatchExecutor handles sequential execution of primary agent plus optional additional agents)
- **FR-015**: System MUST expose configuration for enabling/disabling orchestration features via feature flags

### Key Entities

- **AgentChoiceResult**: Router output containing selected agent ID, confidence score, reasoning explanation, and optional additional agents for multi-agent coordination
- **RouterExecutor**: Workflow executor that invokes IChatClient to analyze user requests and produce AgentChoiceResult using structured JSON output
- **LuciaOrchestrator**: Main orchestrator class that builds and executes the workflow (RouterExecutor → AgentDispatchExecutor → ResultAggregatorExecutor) and exposes itself as an A2A-compliant agent
- **AgentDispatchExecutor**: Workflow executor that receives AgentChoiceResult, resolves agent wrappers, and invokes agents sequentially (primary agent first, then additional agents if specified)
- **AgentExecutorWrapper**: Wraps AIAgent instances to handle A2A message delivery, telemetry, timeout handling, and error management
- **ResultAggregatorExecutor**: Workflow executor that collects AgentResponse messages and formats them into natural language confirmation strings
- **TaskContext**: Serializable conversation state including message history, agent selections, and metadata (stored in Redis with configurable TTL)
- **WorkflowState**: Execution state for the orchestration workflow including current executor and pending operations
- **AgentResponse**: Structured response from an agent execution containing success status, content, error details, and execution time

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can issue single-domain commands and receive appropriate responses in under 2 seconds (95th percentile)
- **SC-002**: Multi-turn conversations successfully maintain context across at least 5 conversation turns with topic shifts
- **SC-003**: Routing decisions achieve at least 95% accuracy (correct agent selected) for unambiguous requests
- **SC-004**: Multi-agent coordination requests complete successfully with unified responses when both agents support the requested domains
- **SC-005**: Task context successfully persists across host restarts with full conversation restoration for active taskIds
- **SC-006**: System handles at least 10 concurrent orchestration requests without performance degradation
- **SC-007**: Ambiguous requests receive appropriate clarification prompts or fallback responses rather than incorrect agent selection
- **SC-008**: Agent execution failures result in graceful error messages rather than system crashes or silent failures
- **SC-009**: All orchestration operations emit telemetry data enabling monitoring of routing confidence, agent latency, and failure rates
- **SC-010**: Routing decisions complete in under 500 milliseconds (95th percentile) to maintain responsive user experience

## Clarifications *(optional)*

### Session 2025-10-13

**Q1: Terminology Alignment** - The spec referenced "RouterExecutor" throughout but research.md and plan.md described a "CoordinatorAgent" architecture. Which terminology is correct for the dynamic agent registry pattern?

**A1**: RouterExecutor is the correct terminology. It is already partially implemented in `lucia.Agents/Orchestration/RouterExecutor.cs` as a workflow executor that:
- Queries AgentRegistry for available agents dynamically at runtime
- Invokes an IChatClient (configurable LLM/SLM) to analyze user requests
- Returns structured AgentChoiceResult with agent ID, confidence score, reasoning, and optional additional agents
- Handles fallback/clarification logic when confidence is below threshold or no agents match

**Clarification Impact**:
- Updated FR-001 to specify RouterExecutor uses IChatClient (not just "an LLM")
- Updated FR-003 to clarify routing uses workflow conditional edges based on RouterExecutor output
- Updated FR-006 to specify ResultAggregatorExecutor for response aggregation
- Updated FR-009 to clarify LuciaOrchestrator (not generic "task-aware host service") must expose itself as A2A-compliant agent
- Updated FR-013 to specify RouterExecutor handles fallback logic via confidence threshold
- Updated FR-014 to clarify AgentDispatchExecutor handles sequential execution pattern
- Expanded Key Entities section to document actual implementation components: RouterExecutor, LuciaOrchestrator, AgentDispatchExecutor, AgentExecutorWrapper, ResultAggregatorExecutor

---
