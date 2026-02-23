# Implementation Tasks: Activity Dashboard & Live Agent Mesh

**Feature Branch**: `release/solstice`  
**Created**: 2025-02-23  
**Status**: Ready for Implementation  
**Spec**: [spec.md](./spec.md)

## Task Overview

**Total Tasks**: 18  
**Phases**: 5  
**MVP Scope**: Phases 1-3 (Summary cards + Live mesh graph)

### Tasks by User Story

- **Setup & Infrastructure**: 4 tasks (T001-T004)
- **US1 - Activity Overview Dashboard (P1)**: 4 tasks (T005-T008)
- **US2 - Live Agent Mesh Graph (P1)**: 6 tasks (T009-T014)
- **US3 - Usage Reports (P2)**: 2 tasks (T015-T016)
- **Polish & Integration**: 2 tasks (T017-T018)

---

## Phase 1: Setup & Infrastructure

**Goal**: SSE channel, observer wiring, and dashboard route scaffold  
**Completion Criteria**: SSE endpoint streams test events, new page route renders

### T001 - [Backend] Create LiveActivityChannel in-memory event bus
**Files**: `lucia.Agents/Orchestration/LiveActivityChannel.cs`  
**Description**: Create a `Channel<LiveEvent>` wrapper service registered as singleton. Exposes `WriteAsync(LiveEvent)` for the observer and `ReadAllAsync(CancellationToken)` for the SSE endpoint. Use `System.Threading.Channels.Channel.CreateBounded<LiveEvent>(100)` with `BoundedChannelFullMode.DropOldest` to prevent backpressure from slow consumers.

```csharp
public sealed class LiveActivityChannel
{
    private readonly Channel<LiveEvent> _channel = 
        Channel.CreateBounded<LiveEvent>(new BoundedChannelOptions(100)
        { FullMode = BoundedChannelFullMode.DropOldest });
    
    public ValueTask WriteAsync(LiveEvent evt, CancellationToken ct = default) 
        => _channel.Writer.TryWrite(evt) ? default : _channel.Writer.WriteAsync(evt, ct);
    
    public IAsyncEnumerable<LiveEvent> ReadAllAsync(CancellationToken ct) 
        => _channel.Reader.ReadAllAsync(ct);
}
```

**Acceptance**: Service resolves from DI, can write and read events in a unit test

---

### T002 - [Backend] Create LiveActivityObserver implementing IOrchestratorObserver
**Files**: `lucia.Agents/Orchestration/LiveActivityObserver.cs`  
**Description**: Implement `IOrchestratorObserver` that publishes `LiveEvent` objects to `LiveActivityChannel`. Map each callback:

| Observer Callback | LiveEvent Type | State |
|---|---|---|
| `OnRequestStartedAsync` | `requestStart` | Orchestrator → "Processing Prompt..." |
| `OnRoutingCompletedAsync` | `routing` | Selected agent name, confidence |
| `OnAgentExecutionCompletedAsync` | `agentComplete` | Agent → "Generating Response..." then idle |
| `OnResponseAggregatedAsync` | `requestComplete` | Orchestrator → idle |

Add intermediate events for tool calls by wrapping the agent execution — emit `toolCall` when tools are invoked and `toolResult` when they return. This requires hooking into the `AgentExecutionRecord.ToolCalls` data from the existing `TraceCaptureObserver` pattern.

**Agent scope**: Full granular events (Processing Prompt → Calling Tools → Generating Response) apply to **in-process agents only** — OrchestratorAgent, LightAgent, GeneralAgent, ClimateAgent, and DynamicAgent (all in `lucia.Agents.Agents` namespace). Off-process A2A plugin agents (MusicAgent, TimerAgent, future remote agents) emit only a simplified `agentStart` with state "Processing..." when dispatched and `agentComplete` when the A2A call returns. The observer should check whether the dispatched agent is local (resolvable from DI) or remote (A2A URI) to determine which event granularity to use.

Register as a **decorator** around the existing `TraceCaptureObserver` so both observers fire. Use `IEnumerable<IOrchestratorObserver>` or a composite pattern.

**Acceptance**: Observer emits full events for in-process agents, simplified events for remote agents

---

### T003 - [Backend] Create SSE endpoint at GET /api/activity/live
**Files**: `lucia.AgentHost/Extensions/ActivityApi.cs`  
**Description**: Map a minimal API endpoint that streams `LiveEvent` objects as SSE:

```csharp
group.MapGet("/live", async (LiveActivityChannel channel, HttpContext ctx, CancellationToken ct) =>
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";
    
    await foreach (var evt in channel.ReadAllAsync(ct))
    {
        await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(evt)}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
});
```

Also add `GET /api/activity/mesh` returning the current agent topology (agents + their tools) from `IAgentRegistry` and agent definitions for the initial graph layout.

Also add `GET /api/activity/summary` aggregating stats from trace, task, and cache services into a single response.

**Acceptance**: `curl` to `/api/activity/live` holds connection open and receives events when prompts are processed

---

### T004 - [Dashboard] Scaffold ActivityPage route and navigation
**Files**: `lucia-dashboard/src/pages/ActivityPage.tsx`, `lucia-dashboard/src/App.tsx`, `lucia-dashboard/src/api.ts`, `lucia-dashboard/src/types.ts`  
**Description**: 
- Add route `<Route path="/activity" element={<ActivityPage />} />`
- Add sidebar nav link (use 📊 or activity icon)
- Add API functions: `fetchActivitySummary()`, `fetchAgentMesh()`, `connectActivityStream()` (SSE via `EventSource`)
- Add types: `LiveEvent`, `AgentMeshNode`, `AgentMeshEdge`, `ActivitySummary`, `AgentActivityStats`
- Scaffold page with three placeholder sections: summary cards, mesh graph, reports

**Acceptance**: `/activity` route renders with placeholder sections, nav link works

---

**✓ Phase 1 Checkpoint**: SSE endpoint streams events during orchestration, dashboard page route exists with placeholders

---

## Phase 2: Activity Overview (US1)

**Goal**: Summary cards with real metrics  
**Completion Criteria**: Cards display live data from aggregated stats

### T005 - [Backend] Implement /api/activity/summary endpoint
**Files**: `lucia.AgentHost/Extensions/ActivityApi.cs`  
**Description**: Aggregate data from `ITraceRepository`, `ITaskStore`, and `IPromptCacheService` into a single `ActivitySummary` response:

```typescript
{
  traces: { total, errored, byAgent },
  tasks: { activeCount, completedToday },
  cache: { totalEntries, hitRate },
  uptime: "2h 34m"
}
```

**Acceptance**: Endpoint returns combined stats matching individual API values

---

### T006 - [Dashboard] Summary cards component
**Files**: `lucia-dashboard/src/pages/ActivityPage.tsx`  
**Description**: Render 4-6 metric cards at the top of the page:
- Total Requests (from traces.total)
- Active Tasks (from tasks.activeCount)
- Error Rate (from traces.errored / traces.total)
- Cache Hit Rate (from cache.hitRate)
- Agents Active (count of agents with recent traces)

Cards should use the Observatory theme, be responsive (2-col on mobile, 4-col on desktop), and auto-refresh every 30 seconds.

**Acceptance**: Cards display correct values, responsive layout works on mobile

---

### T007 - [Dashboard] Activity timeline chart
**Files**: `lucia-dashboard/src/pages/ActivityPage.tsx`  
**Description**: Simple SVG-based bar/line chart showing request volume over time. Use trace timestamps grouped by hour/day. No external charting library — use inline SVG with the Observatory color palette. Show bars colored by agent (using `traces.byAgent` data). Time range selector: 1h / 6h / 24h / 7d.

**Acceptance**: Timeline renders with real trace data, time range selector works

---

### T008 - [Dashboard] Auto-refresh and loading states
**Files**: `lucia-dashboard/src/pages/ActivityPage.tsx`  
**Description**: Add 30-second polling interval for summary data. Show skeleton loaders on initial load. Show "Last updated: X seconds ago" indicator. Error states for failed fetches with retry button.

**Acceptance**: Data refreshes automatically, loading/error states display correctly

---

**✓ Phase 2 Checkpoint**: Summary cards show real metrics, timeline chart renders trace data

---

## Phase 3: Live Agent Mesh Graph (US2)

**Goal**: Real-time animated node graph showing orchestration flow  
**Completion Criteria**: Mesh animates through full orchestration lifecycle during prompt processing

### T009 - [Dashboard] Install React Flow and create MeshGraph component
**Files**: `lucia-dashboard/package.json`, `lucia-dashboard/src/components/MeshGraph.tsx`  
**Description**: Install `@xyflow/react` (React Flow v12, MIT license). Create `MeshGraph` component that accepts `nodes: AgentMeshNode[]` and `edges: AgentMeshEdge[]` props. Custom node components:
- **AgentNode**: 🤖 icon, agent name, state badge with color (idle=dust, processing=amber, tools=blue, generating=sage, error=ember)
- **ToolNode**: 🔧 icon, tool name, smaller than agent nodes
- **OrchestratorNode**: 🧠 icon, "Orchestrator" label, larger central node

Use Observatory theme colors for all styling. Enable pan/zoom, disable node dragging.

**Acceptance**: Static graph renders with agent and tool nodes in correct layout

---

### T010 - [Dashboard] Build initial mesh topology from /api/activity/mesh
**Files**: `lucia-dashboard/src/pages/ActivityPage.tsx`, `lucia-dashboard/src/components/MeshGraph.tsx`  
**Description**: Fetch agent mesh data on page load. Build node/edge graph:
- Orchestrator node at center
- Agent nodes in a ring around orchestrator
- Tool nodes clustered below/beside their parent agent
- Edges from orchestrator → each agent (hidden until active)
- Edges from each agent → its tools (hidden until active)

Use auto-layout: orchestrator center, agents in a semi-circle, tools fanning out from each agent.

**Acceptance**: Graph shows all registered agents and tools in readable layout

---

### T011 - [Dashboard] SSE connection and event handling
**Files**: `lucia-dashboard/src/hooks/useActivityStream.ts`  
**Description**: Create `useActivityStream()` hook using `EventSource` API:
- Connect to `/api/activity/live`
- Parse incoming `LiveEvent` JSON
- Expose `lastEvent` and `connectionState` (connected/reconnecting/disconnected)
- Auto-reconnect with exponential backoff (1s, 2s, 4s, max 30s)
- Clean up on unmount (close EventSource)

**Acceptance**: Hook connects, receives events, reconnects on disconnect, cleans up

---

### T012 - [Dashboard] Animate mesh graph from live events
**Files**: `lucia-dashboard/src/components/MeshGraph.tsx`  
**Description**: Consume `LiveEvent` stream and update graph state:

| Event | Graph Update |
|---|---|
| `requestStart` | Orchestrator node → amber "Processing Prompt..." |
| `routing` | Animated dashed edge: orchestrator → selected agent |
| `agentStart` | Agent node → amber "Processing Prompt..." (in-process) or "Processing..." (remote) |
| `toolCall` | Agent → blue "Calling Tools...", animated edge to tool node, tool pulses *(in-process only)* |
| `toolResult` | Reverse animated edge from tool → agent *(in-process only)* |
| `agentComplete` | Agent → sage "Generating Response...", then animated edge back to orchestrator |
| `requestComplete` | All nodes → idle, edges hide |
| `error` | Affected node → ember with error indicator |

**Remote agent handling**: Off-process agents (MusicAgent, TimerAgent, etc.) will only transition between idle → "Processing..." → complete/error. They won't show tool-level animations since their internal execution is opaque to the host observer. The node should display a subtle "remote" badge (e.g., 🌐) to visually distinguish them from in-process agents.

Animated edges use CSS `stroke-dasharray` + `stroke-dashoffset` animation. Each agent gets a unique accent color from a palette for its edges during multi-agent dispatch.

**Acceptance**: In-process agents animate full lifecycle; remote agents show simplified Processing → Complete flow

---

### T013 - [Dashboard] Connection status indicator
**Files**: `lucia-dashboard/src/pages/ActivityPage.tsx`  
**Description**: Show SSE connection status above the mesh graph:
- 🟢 "Live" — connected and receiving
- 🟡 "Reconnecting..." — connection lost, retrying
- 🔴 "Disconnected" — failed after max retries, with manual reconnect button

**Acceptance**: Indicator reflects actual connection state, reconnect button works

---

### T014 - [Backend] Emit tool-level events from observer
**Files**: `lucia.Agents/Orchestration/LiveActivityObserver.cs`  
**Description**: Enhance the observer to emit granular tool-call events for **in-process agents only** (OrchestratorAgent, LightAgent, GeneralAgent, ClimateAgent, DynamicAgent). Since `IOrchestratorObserver.OnAgentExecutionCompletedAsync` provides `AgentExecutionRecord` which includes `ToolCalls`, emit `toolCall` and `toolResult` events by inspecting the execution record. For true real-time tool events, add an `agentStart` event emitted at the beginning of `OnRoutingCompletedAsync` (when we know which agent is being dispatched) and reconstruct tool activity from the completed execution record's timestamps.

For off-process A2A agents, emit only `agentStart` (state: "Processing...") on dispatch and `agentComplete` when the A2A HTTP call returns. Check `AgentCard.Url` — if it resolves to a remote URI, use simplified events.

**DynamicAgent**: Must support full observer events since it runs in-process. The observer should treat DynamicAgent identically to the built-in agents — it has access to the same `AgentExecutionRecord` data including tool calls.

**Acceptance**: SSE stream includes tool-level events for in-process agents, simplified events for remote agents

---

**✓ Phase 3 Checkpoint**: Live mesh graph animates through complete orchestration flow in real-time

---

## Phase 4: Usage Reports (US3)

**Goal**: Per-agent usage breakdown  
**Completion Criteria**: Reports section shows actionable per-agent statistics

### T015 - [Backend] Add /api/activity/agent-stats endpoint
**Files**: `lucia.AgentHost/Extensions/ActivityApi.cs`  
**Description**: Query trace repository to compute per-agent stats:
- Request count
- Average duration (ms)
- Error rate (%)
- Top 5 tools by call frequency

Return as `AgentActivityStats[]`. Use trace data grouped by `routing.selectedAgent`.

**Acceptance**: Endpoint returns accurate per-agent stats matching trace data

---

### T016 - [Dashboard] Usage reports table
**Files**: `lucia-dashboard/src/pages/ActivityPage.tsx`  
**Description**: Render a table below the mesh graph with per-agent rows. Columns: Agent Name, Requests, Avg Duration, Error Rate, Top Tools (as pill tags). Sortable by any column. Mobile: horizontally scrollable table.

**Acceptance**: Table renders with real data, sorting works, responsive on mobile

---

**✓ Phase 4 Checkpoint**: Reports show per-agent breakdown with sortable columns

---

## Phase 5: Polish & Cross-Cutting

### T017 - [Testing] Integration tests for SSE and activity endpoints
**Files**: `lucia.Tests/ActivityDashboard/`  
**Description**: 
- Test `LiveActivityChannel` write/read cycle
- Test SSE endpoint returns correct content-type and streams events
- Test `/api/activity/summary` aggregates correctly
- Test `/api/activity/mesh` returns agent topology

**Acceptance**: All tests pass with `dotnet test --filter 'Category!=Eval'`

---

### T018 - [Dashboard] Mobile responsiveness and polish
**Files**: `lucia-dashboard/src/pages/ActivityPage.tsx`, `lucia-dashboard/src/components/MeshGraph.tsx`  
**Description**: 
- Summary cards: 1-col on mobile, 2-col on sm, 4-col on lg
- Mesh graph: scrollable container with pinch-to-zoom on mobile, minimum height 300px
- Reports table: horizontal scroll wrapper on mobile
- Verify Observatory theme consistency across all new components

**Acceptance**: Page is fully usable on 375px viewport width

---

**✓ Phase 5 Checkpoint**: Tests pass, mobile layout verified, ready for release
