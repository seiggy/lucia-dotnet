# Feature Specification: Activity Dashboard & Live Agent Mesh

**Feature Branch**: `release/solstice`  
**Created**: 2025-02-23  
**Status**: Draft  
**Input**: Activity dashboard with OTEL metrics timeline, usage reports, and live-updating agent mesh graph showing real-time orchestration flow during prompt processing

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Activity Overview Dashboard (Priority: P1)

As a Lucia administrator, I want a single dashboard page that shows platform activity metrics (trace counts, agent usage, task throughput, cache hit rates) over time so that I can monitor system health and usage patterns at a glance.

**Why this priority**: This is the foundation page that aggregates existing scattered stats APIs into a unified view. It provides immediate value by surfacing data that already exists but requires visiting 3+ separate pages to see.

**Independent Test**: Navigate to `/activity` and verify that metric cards display current totals (traces, tasks, errors, cache hits) and a timeline chart shows activity over the selected time range — all sourced from existing `/api/traces/stats`, `/api/tasks/stats`, and `/api/prompt-cache/stats` endpoints.

**Acceptance Scenarios**:

1. **Given** the system has processed requests today, **When** I open `/activity`, **Then** I see summary cards showing total traces, active tasks, error count, and cache hit rate with values matching the individual stats APIs
2. **Given** I select a 7-day time range, **When** the timeline renders, **Then** I see a daily breakdown of request volume by agent, sourced from trace data
3. **Given** no requests have been processed, **When** I open `/activity`, **Then** I see zero-state cards and an empty timeline with a helpful message

**Edge Cases**:
- Stats API returns partial data (one endpoint down) — show available cards, error indicator on failed ones
- Very large trace counts — use abbreviated numbers (e.g., "12.4k")

---

### User Story 2 - Live Agent Mesh Graph (Priority: P1)

As a Lucia administrator, I want to see a live-updating node graph of the agent mesh that animates the orchestration flow in real time while a prompt is being processed, so that I can visualize how agents and tools collaborate and diagnose slow or failing steps.

**Why this priority**: This is the marquee feature that provides unique observability into the multi-agent system. It makes the orchestration tangible and debuggable in a way that logs and traces alone cannot.

**Independent Test**: Open the `/activity` page, submit a prompt through the system (via HA or direct API), and observe the mesh graph animate through the orchestration lifecycle: orchestrator node activates → animated arrow to selected agent(s) → agent nodes show "Processing Prompt..." → animated arrows to tool nodes → tools show activity → arrows reverse → agent shows "Generating Response..." → response flows back to orchestrator.

**Acceptance Scenarios**:

1. **Given** the activity page is open and idle, **When** I view the mesh graph, **Then** I see all registered agents as 🤖 nodes and their associated tools as 🔧 nodes in a static layout, with the orchestrator node centered
2. **Given** a prompt is submitted, **When** the orchestrator starts processing, **Then** the orchestrator node pulses and shows "Processing Prompt..." state
3. **Given** the router has selected an agent, **When** the dispatch begins, **Then** an animated arrow flows from the orchestrator to the selected agent node, the agent node activates with a colored state indicator, and shows "Processing Prompt...", the orchestrator agent node shows "Calling Tools..."
4. **Given** an agent is calling tools, **When** tool execution begins, **Then** animated arrows flow from the agent to each tool node being called, tool nodes pulse, and the agent shows "Calling Tools..."
5. **Given** tool execution completes, **When** the agent generates its response, **Then** animated arrows reverse from tools back to the agent, and the agent shows "Generating Response..."
6. **Given** the orchestrator aggregates the final response, **When** aggregation completes, **Then** an animated arrow flows from the agent back to the orchestrator, and the orchestrator shows "Generating Response..." until the reponse is completed, then show completion state for 3 seconds before returning to idle
7. **Given** multiple agents are dispatched (fan-out), **When** parallel execution occurs, **Then** multiple animated arrow paths are active simultaneously with distinct colors per agent
8. **Given** multiple agents are dispatched (sequential), **When** sequential execution occurs, **Then** multiple arrow paths are active simultaneously with distinct colors per agent, animate only the current agent in sequence, dimming the arrow when finished and leaving incomplete agents that are to be called in the sequence brightly colored, with non-planned agents in a neutral inactive color.

**Edge Cases**:
- Agent execution fails — node shows error state (red) with error indicator
- User navigates away during live animation — SSE connection closes cleanly, no memory leaks
- Multiple concurrent requests — show the most recent active request, queue indicator for others
- No agents registered — show empty mesh with "No agents registered" message
- SSE connection drops — show reconnecting indicator, auto-reconnect with backoff

---

### User Story 3 - Usage Reports (Priority: P2)

As a Lucia administrator, I want usage breakdown reports showing per-agent request counts, average response times, error rates, and tool call frequency so that I can identify bottlenecks and optimize my agent configurations.

**Why this priority**: Builds on the stats foundation from US1 to provide actionable insights. Secondary to the live view because it's analytical rather than operational.

**Independent Test**: Open the `/activity` page, scroll to the reports section, and verify per-agent breakdown tables showing request counts, avg duration, error rates, and top tools used — all derived from existing trace data.

**Acceptance Scenarios**:

1. **Given** traces exist for multiple agents, **When** I view the reports section, **Then** I see a table with one row per agent showing request count, avg duration (ms), error rate (%), and most-used tools
2. **Given** I click on an agent row, **When** the detail expands, **Then** I see a mini-timeline of that agent's activity and a breakdown of tool call frequency

---

## Requirements

### Functional Requirements

**FR-001**: New `/activity` route in the dashboard with three sections: summary cards, live mesh graph, and usage reports

**FR-002**: Summary cards aggregate data from existing stats endpoints (`/api/traces/stats`, `/api/tasks/stats`, `/api/prompt-cache/stats`) into a unified view

**FR-003**: Server-Sent Events (SSE) endpoint at `GET /api/activity/live` that streams orchestration lifecycle events in real time, powered by `IOrchestratorObserver` hooks

**FR-004**: Live mesh graph renders registered agents as 🤖 nodes and their tools as 🔧 nodes using React Flow (lightweight, MIT-licensed, React-native graph library)

**FR-005**: Agent node states displayed with distinct colors:
- **Idle** — muted/dim (dust color)
- **Processing Prompt...** — amber pulse
- **Calling Tools...** — blue pulse  
- **Generating Response...** — sage/green pulse
- **Error** — red/ember pulse

**FR-006**: Animated arrows (edges) between nodes:
- Orchestrator → Agent: shows dispatch in progress
- Agent → Tool: shows tool call in progress
- Reverse direction on completion
- Animated dash pattern indicating data flow direction

**FR-007**: New `LiveActivityObserver` implementing `IOrchestratorObserver` that publishes events to an in-memory channel consumed by the SSE endpoint

**FR-008**: **In-process agent observability scope**: Only agents in the `lucia.Agents.Agents` namespace (OrchestratorAgent, LightAgent, GeneralAgent, ClimateAgent, DynamicAgent) emit full granular lifecycle events (Processing Prompt → Calling Tools → Generating Response). Off-process A2A plugin agents (MusicAgent, TimerAgent, and any future remote agents) show a simplified "Processing..." state when the orchestrator dispatches to them, since they run in separate processes without access to the in-process observer channel. Full remote agent observability (pushing events back to the host observer) is deferred to a future release. DynamicAgent **must** support the full observer event pattern since it runs in-process and represents user-defined agents.

**FR-009**: Usage reports section derives per-agent statistics from the existing `/api/traces/stats` `byAgent` data, enhanced with a new `/api/activity/agent-stats` endpoint that returns per-agent avg duration and error rates

**FR-010**: Mobile-responsive layout — summary cards stack, mesh graph is scrollable/zoomable, reports table is horizontally scrollable

### Key Entities

- **LiveEvent**: SSE event payload with `type` (routing | agentStart | toolCall | toolResult | agentComplete | requestComplete | error), `agentName`, `toolName?`, `state`, `timestamp`
- **AgentMeshNode**: Graph node representing an agent (🤖) or tool (🔧) with id, label, type, state, position
- **AgentMeshEdge**: Graph edge with source, target, animated flag, color, label
- **AgentActivityStats**: Per-agent stats with requestCount, avgDurationMs, errorRate, topTools

## Success Criteria

**SC-001**: Activity page loads within 2 seconds showing summary cards with data from all three stats endpoints

**SC-002**: Live mesh graph establishes SSE connection and renders initial agent topology within 1 second of page load

**SC-003**: Orchestration events appear in the mesh graph within 200ms of the observer callback firing (perceived real-time)

**SC-004**: SSE connection auto-reconnects within 5 seconds of disconnection

**SC-005**: Page is fully functional on mobile viewports (≥375px width)

**SC-006**: Zero memory leaks — SSE connections and React Flow instances clean up on unmount
