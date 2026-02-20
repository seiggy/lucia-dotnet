# Architecture Diagram Index

> Created: 2025-10-02  
> Updated: 2025-02-20  
> Source folder: ./diagrams/

All diagrams are maintained as **Excalidraw** files (`.excalidraw`). Open them with the [VS Code Excalidraw extension](https://marketplace.visualstudio.com/items?itemName=pomdtr.excalidraw-editor) for interactive editing and PNG export.

## Diagrams

### 1) System Context

- **Purpose**: Show the primary actors, system boundaries, and external dependencies for the Lucia platform.
- **Source**: [01-system-context.excalidraw](./diagrams/01-system-context.excalidraw)
- **Covers**: Homeowner actor, Home Assistant deployment (Conversation Plugin, HA APIs, Smart Devices), Lucia Platform Runtime (AgentHost, A2AHosts for music-agent & timer-agent, Dashboard, Redis, MongoDB), and external LLM providers.

### 2) Runtime Containers

- **Purpose**: Break down the Lucia solution into containers, libraries, and key integrations.
- **Source**: [02-runtime-containers.excalidraw](./diagrams/02-runtime-containers.excalidraw)
- **Covers**: Home Assistant environment, Lucia .NET Services (AppHost, AgentHost, A2AHosts, Dashboard, HomeAssistant SDK), Agent Runtime Libraries (lucia.Agents, lucia.MusicAgent, lucia.TimerAgent), Shared Infrastructure (ServiceDefaults, Redis, MongoDB), and External AI Providers (OpenAI, Gemini, Claude).

### 3) Orchestration Components

- **Purpose**: Document the Router → Agent Dispatch → Result Aggregator pipeline inside LuciaOrchestrator.
- **Source**: [03-orchestration-components.excalidraw](./diagrams/03-orchestration-components.excalidraw)
- **Covers**: Incoming request flow through RouterExecutor (intent classification), AgentDispatchExecutor (fan-out), AgentExecutorWrapper (per-agent invoke), ResultAggregatorExecutor (merge), and the ILuciaAgent pool (OrchestratorAgent, MusicAgent, TimerAgent, HomeAssistantAgent). Shows Redis session state, LLM routing, and HA API connections.

### 4) Conversation Sequence Flow

- **Purpose**: Document the step-by-step flow for handling a Home Assistant voice/text request.
- **Source**: [04-conversation-sequence.excalidraw](./diagrams/04-conversation-sequence.excalidraw)
- **Covers**: 12-step sequence — Homeowner → HA Plugin → AgentHost → LLM (intent classification) → A2AHost → LLM (optional reasoning) → HA Devices → state confirmation → response back through Dashboard and Plugin to Homeowner.

### 5) Deployment Topology

- **Purpose**: Illustrate how components are deployed across the home network and cloud services.
- **Source**: [05-deployment-topology.excalidraw](./diagrams/05-deployment-topology.excalidraw)
- **Covers**: Home Assistant Host (RPi/VM with HA Core and Lucia Plugin), Lucia Platform Runtime (.NET Aspire orchestrating AppHost, AgentHost on ports 5211/7000, A2AHosts for music-agent & timer-agent, Dashboard, Redis, MongoDB), Observability stack (OTel Collector, Grafana/Loki), and optional Cloud AI providers (OpenAI GPT-4o, Gemini/Claude).

## Notes

- All diagrams use the Excalidraw element format with a consistent color palette.
- Excalidraw files are the **canonical editable source** for architecture diagrams.
- Open any `.excalidraw` file in VS Code (with the Excalidraw extension) to edit interactively or export as `.excalidraw.png`.
- Dashed connections indicate optional external LLM provider use.
