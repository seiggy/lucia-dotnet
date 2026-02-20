# Documentation Modernization Execution Plan

Date: 2026-02-20
Status: Ready for execution
Approach: Parallel multi-agent delivery with a runtime-code baseline

## Objective
Ship a synchronized documentation refresh that is anchored to runtime truth in `lucia.AppHost`, `lucia.AgentHost`, `lucia.A2AHost`, `lucia.Agents`, and `lucia.HomeAssistant`, while removing highest-impact drift first.

## Workstream Matrix
| Workstream | Owner Agent | Depends On | Primary Outputs |
|---|---|---|---|
| WS1 Runtime Baseline | Architecture Analyst | None | Canonical architecture facts, service topology notes, drift evidence pack |
| WS2 Core Product Docs | Product Docs Editor | WS1 | Updated `README.md`, `.docs/product/tech-stack.md`, `.docs/product/roadmap.md` architecture alignment |
| WS3 Architecture Docs & Diagrams | Diagram Author | WS1 | Updated diagram index/integration docs and terminology consistency across architecture pages |
| WS4 Integration & Ops Docs | Integration Docs Editor | WS1 | Updated `infra/README.md`, `custom_components/lucia/README.md`, `custom_components/lucia/CHANGES.md` |
| WS5 Agent Guidance Docs | Maintainer Docs Editor | WS1, WS2 | Updated `AGENTS.md`, `CLAUDE.md`, `.github/copilot-instructions.md` for current runtime and commands |
| WS6 Validation & Release Notes | QA Validator | WS2, WS3, WS4, WS5 | Drift closure report, link check report, doc handoff summary |

## Wave Schedule
### Wave 1 (Day 0-1): Baseline + Highest Drift
- Execute WS1 end-to-end and freeze baseline facts.
- Start WS2 immediately after WS1 fact freeze.
- Patch top-priority drift statements affecting onboarding/build/runtime understanding.
- Exit criteria: WS1 approved; top 8 backlog items fixed or explicitly waived.

### Wave 2 (Day 2-3): Parallel Domain Updates
- Run WS3, WS4, and WS5 in parallel against WS1 facts.
- Enforce shared terminology set: `AgentHost`, `A2AHost`, `AppHost`, `Redis`, `MongoDB`, `LuciaEngine`.
- Exit criteria: all changed docs pass internal link checks and owner self-review.

### Wave 3 (Day 4): Validation + Handoff
- WS6 validates drift closures and performs final consistency sweep.
- Publish concise release-style summary of doc changes and remaining debt.
- Exit criteria: definition-of-done met and handoff protocol completed.

## WS1 Canonical Architecture Facts (Runtime-Derived)
- `lucia.AppHost/AppHost.cs` provisions Azure AI Foundry as an existing resource using parameters for name and resource group.
- AppHost defines model deployments: `chat` (GPT-4o), `embeddings` (TextEmbedding3Large), plus `chat-mini`, `phi4`, and `gpt-5-nano`.
- AppHost provisions Redis with persistent lifetime, data volume, RedisInsight, and explicit container name `redis`.
- AppHost provisions MongoDB with persistent lifetime, data volume, Mongo Express, and explicit container name `mongodb`.
- Mongo logical databases are `luciatraces`, `luciaconfig`, and `luciatasks`.
- AppHost runs `lucia-agenthost` plus two `lucia.A2AHost` instances: `music-agent` and `timer-agent`.
- A2AHost instances receive `PluginDirectory` via environment variables pointing to `plugins/music-agent` and `plugins/timer-agent`.
- AppHost also runs `lucia-dashboard` as a Vite app with references to `lucia-agenthost` and external HTTP endpoints.
- `lucia.AgentHost/Program.cs` registers Redis plus Mongo clients for traces/config/tasks and applies Mongo-backed configuration override via `AddMongoConfiguration("luciaconfig")`.
- AgentHost registers chat + embeddings clients and keyed chat clients for `phi4` and `gpt-5-nano`.
- AgentHost exposes APIs via `MapAgentRegistryApiV1`, `MapAgentProxyApi`, `MapAgentDiscovery`, `MapTraceManagementApi`, `MapDatasetExportApi`, `MapConfigurationApi`, `MapPromptCacheApi`, and `MapTaskManagementApi`.
- `lucia.A2AHost/Program.cs` loads plugins dynamically (`PluginLoader.LoadAgentPlugins`) and wraps the music agent chat client with tracing.
- `lucia.Agents/Orchestration/LuciaEngine.cs` orchestrates a workflow chain: `RouterExecutor -> AgentDispatchExecutor -> ResultAggregatorExecutor`.
- `lucia.Agents/Agents/OrchestratorAgent.cs` is an `AIAgent` + `ILuciaAgent` adapter around `LuciaEngine` with session-aware routing.
- `lucia.HomeAssistant/Services/HomeAssistantClient.cs` is a hand-written typed REST client using `HomeAssistantOptions` (base URL, bearer token, timeout), not source-generated code.

## Prioritized Drift Backlog (Top 15)
| Priority | Location | Stale Statement (Concise) | Runtime Reality / Fix Direction |
|---|---|---|---|
| P1 | `README.md` | Project includes `lucia.HomeAssistant.SourceGenerator` | Remove; HA client is hand-written in `lucia.HomeAssistant/Services/HomeAssistantClient.cs` |
| P1 | `RELEASE_NOTES.md` | Mentions SourceGenerator as active project | Replace with hand-written client architecture note |
| P1 | `AGENTS.md` | Lists `lucia.HomeAssistant.SourceGenerator` | Remove from project snapshot |
| P1 | `.docs/product/tech-stack.md` | Primary DB is in-memory/PostgreSQL planned | Update to active Redis + MongoDB runtime usage |
| P1 | `.docs/product/tech-stack.md` | HA REST uses Roslyn source generators | Update to hand-written typed client |
| P1 | `CLAUDE.md` | Main web API has empty controllers folder | Replace with current minimal-API route map in `lucia.AgentHost/Program.cs` |
| P1 | `CLAUDE.md` | Integration tests are commented out | Replace with current test reality and command guidance |
| P2 | `README.md` | Build command uses `lucia-dotnet.sln` | Align to active solution usage (`.slnx` / workspace tasks) |
| P2 | `AGENTS.md` | Build command uses `lucia-dotnet.sln` | Align with current workspace command convention |
| P2 | `.github/copilot-instructions.md` | Build command uses `lucia-dotnet.sln` | Align with current workspace command convention |
| P2 | `.docs/product/tech-stack.md` | Embeddings model listed as `text-embedding-3-small` | Update to `TextEmbedding3Large` deployment in AppHost |
| P2 | `.docs/product/tech-stack.md` | Management UI marked planned | Update to existing `lucia-dashboard` Vite app |
| P3 | `README.md` | Static endpoint examples imply single-host-only topology | Add current AppHost topology context (AgentHost + A2A hosts + dashboard) |
| P3 | `.docs/architecture/.../diagram-index.md` | Includes Source Generator node | Replace with current Home Assistant client/service representation |
| P3 | `custom_components/lucia/README.md` | “Coming soon” install/status notes remain | Replace with current supported path and known constraints |

## Definition of Done
- WS1 facts are approved and referenced by all downstream workstreams.
- Top 15 backlog items are each marked: Fixed, Waived (with rationale), or Deferred (with owner/date).
- No changed doc reintroduces removed components (`lucia.HomeAssistant.SourceGenerator`).
- Build/run/test instructions in touched docs match current repository conventions.
- Architecture terminology is consistent across product, architecture, and guidance docs.
- Internal markdown links in touched files resolve.
- Final report from WS6 includes changed files list and unresolved drift list.

## Parallel Edit Handoff Protocol
### Branching and ownership
- One owner agent per workstream; no shared-file concurrent edits.
- If overlap is unavoidable, assign a primary owner and create explicit edit windows.

### Baseline contract
- WS1 publishes `Fact Freeze v1` as immutable reference for Wave 2.
- Any post-freeze runtime discrepancy is logged as a change request before edits continue.

### Change packaging
- Each workstream submits: scope summary, files touched, backlog item IDs closed, and open risks.
- Submit smallest coherent PR-sized batch to reduce merge drift.

### Merge order
- Merge WS2 first (core product truth), then WS3/WS4, then WS5.
- WS6 performs final reconciliation after all merges.

### Conflict resolution
- Runtime file evidence wins over legacy docs.
- If two docs conflict and runtime is ambiguous, defer statement and add explicit TODO with owner/date.

### Final handoff checklist
- All wave exit criteria met.
- Drift table status updated.
- Reviewer sign-off captured from WS6 owner.
- Publish final modernization summary in `.docs/reports/`.

## Execution Notes
- Keep edits concise and factual; avoid speculative roadmap claims in runtime sections.
- Preserve historical sections only when clearly labeled as historical.
- Prefer explicit file references to reduce future drift during audits.
