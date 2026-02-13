# Architecture Diagram Index

> Created: 2025-10-02  
> Source folder: ./diagrams/

## Diagrams

### 1) System Context
- Purpose: Show the primary actors, system boundaries, and external dependencies for the Lucia platform.
- Source: ./diagrams/context.mmd
```mermaid
---
config:
  theme: dark
---
flowchart TB
  classDef actor fill:#1f77b4,color:#ffffff,stroke:#85c0f9,stroke-width:2px;
  classDef platform fill:#2ca02c,color:#0a0a0a,stroke:#98df8a,stroke-width:2px;
  classDef external fill:#ff7f0e,color:#0a0a0a,stroke:#ffbb78,stroke-width:2px;
  classDef service fill:#9467bd,color:#ffffff,stroke:#c5b0d5,stroke-width:2px;

  subgraph Home_Assistant["Home Assistant Deployment"]
    haPlugin["Lucia Conversation Plugin\n(custom component)"]:::service
    haAPIs["Home Assistant APIs\n(Conversation, REST, LLM)"]:::service
    haDevices["Automations & Smart Devices"]:::external
  end

  subgraph Lucia_Platform["Lucia Agent Platform (.NET Aspire)"]
    luciaAPI["Lucia API Service\n(lucia-dotnet)"]:::platform
    registry["Agent Registry & A2A Gateway"]:::platform
    orchestration["Semantic Kernel Orchestration\n(lucia.Agents)"]:::platform
  end

  user["Homeowner / Operator"]:::actor
  haCore["Home Assistant Core"]:::service
  llmProviders["LLM Providers\n(OpenAI, Gemini, Claude, Local)"]:::external

  user -->|"Voice/Text Request"| haPlugin
  haPlugin -->|"Conversation intents"| haAPIs
  haAPIs -->|"Agent registry lookup"| luciaAPI
  luciaAPI -->|"A2A coordination"| orchestration
  orchestration -->|"Action directives"| haAPIs
  haAPIs -->|"Device updates"| haDevices
  orchestration -.->|"Optional prompts"| llmProviders
  haPlugin -->|"Skill metadata"| registry
  haCore -->|"Executes automations"| haDevices
```

### 2) Container View
- Purpose: Break down the Lucia solution into containers, libraries, and key integrations.
- Source: ./diagrams/container.mmd
```mermaid
---
config:
  theme: dark
---
flowchart LR
  classDef ha fill:#1f77b4,color:#ffffff,stroke:#85c0f9,stroke-width:2px;
  classDef lucia fill:#2ca02c,color:#0a0a0a,stroke:#98df8a,stroke-width:2px;
  classDef runtime fill:#9467bd,color:#ffffff,stroke:#c5b0d5,stroke-width:2px;
  classDef infra fill:#17becf,color:#0a0a0a,stroke:#9edae5,stroke-width:2px;
  classDef ai fill:#ff7f0e,color:#0a0a0a,stroke:#ffbb78,stroke-width:2px;
  classDef optional stroke-dasharray: 6 3,stroke:#f7f7f7,color:#f7f7f7;

  subgraph HA_ENV["Home Assistant Environment"]
    haPlugin["Lucia Conversation Plugin\n(custom component)"]:::ha
    haApis["Home Assistant Core APIs"]:::ha
  end

  subgraph LUCIA_SERVICES["Lucia .NET Services"]
    luciaApi["lucia-dotnet API Service"]:::lucia
    agentGateway["Agent Registry API\n& A2A JSON-RPC"]:::lucia
    appHost["lucia.AppHost (.NET Aspire)"]:::lucia
    homeAssistantSdk["lucia.HomeAssistant SDK"]:::lucia
  end

  subgraph AGENT_RUNTIME["Agent Runtime Libraries"]
    agentsLib["lucia.Agents\n(Semantic Kernel)"]:::runtime
    agentHost["lucia.AgentHost Worker"]:::runtime
  end

  subgraph SHARED_INFRA["Shared Infrastructure & Tooling"]
    serviceDefaults["lucia.ServiceDefaults"]:::infra
    sourceGenerator["Home Assistant\nSource Generator"]:::infra
    futureDb["PostgreSQL 17+ (planned)"]:::infra
    futureCache["Redis Cache (planned)"]:::infra
    class futureDb,futureCache optional
  end

  subgraph AI_PROVIDERS["External AI Providers"]
    openAi["OpenAI GPT-4o"]:::ai
    gemini["Google Gemini"]:::ai
    claude["Anthropic Claude"]:::ai
  end

  haPlugin --> haApis
  haApis --> luciaApi
  luciaApi --> agentGateway
  luciaApi --> homeAssistantSdk
  agentGateway --> agentsLib
  agentsLib --> agentHost
  appHost --> luciaApi
  appHost --> agentHost
  serviceDefaults --> luciaApi
  serviceDefaults --> agentHost
  sourceGenerator --> homeAssistantSdk
  luciaApi -.-> futureDb
  agentHost -.-> futureCache
  agentsLib -.-> openAi
  agentsLib -.-> gemini
  agentsLib -.-> claude
```

### 3) Conversation Flow (Sequence)
- Purpose: Document the steps for handling a Home Assistant voice/text request.
- Source: ./diagrams/sequence-conversation-flow.mmd
```mermaid
---
config:
  theme: dark
---
sequenceDiagram
  autonumber
  actor Homeowner as Homeowner
  participant Plugin as Lucia Conversation Plugin
  participant HAAPI as Home Assistant API Layer
  participant LuciaAPI as Lucia API Service
  participant Orchestrator as Semantic Kernel Orchestrator
  participant Agent as Specialized Agent (e.g., LightAgent)
  participant LLM as Optional LLM Provider
  participant Devices as Home Assistant Devices & Automations

  Homeowner->>Plugin: Issue natural language request
  Plugin->>HAAPI: Submit conversation payload
  HAAPI->>LuciaAPI: HTTP POST /agents/execute
  LuciaAPI->>Orchestrator: Build plan & select agent(s)
  Orchestrator->>Agent: Invoke capability via A2A JSON-RPC
  Agent-->>Orchestrator: Proposed action & rationale
  Agent->>LLM: (Optional) Ask for reasoning / summarization
  LLM-->>Agent: Resulting intent refinement
  Orchestrator->>HAAPI: Send concrete device command(s)
  HAAPI->>Devices: Apply state changes / execute automation
  Devices-->>HAAPI: Report updated state
  HAAPI-->>Plugin: Structured response payload
  Plugin-->>Homeowner: Confirm outcome via TTS/UI
```

### 4) Deployment Topology
- Purpose: Illustrate how components are deployed across the home lab and cloud services.
- Source: ./diagrams/deployment.mmd
```mermaid
---
config:
  theme: dark
---
flowchart TB
  classDef host fill:#1f77b4,color:#ffffff,stroke:#85c0f9,stroke-width:2px;
  classDef cluster fill:#2ca02c,color:#0a0a0a,stroke:#98df8a,stroke-width:2px;
  classDef observ fill:#9467bd,color:#ffffff,stroke:#c5b0d5,stroke-width:2px;
  classDef infra fill:#17becf,color:#0a0a0a,stroke:#9edae5,stroke-width:2px;
  classDef ai fill:#ff7f0e,color:#0a0a0a,stroke:#ffbb78,stroke-width:2px;
  classDef optional stroke-dasharray: 6 3,stroke:#f7f7f7,color:#f7f7f7;

  subgraph HOME_HOST["Home Assistant Host (Raspberry Pi / VM)"]
    haCore["Home Assistant Core"]:::host
    luciaComponent["Lucia Conversation Plugin"]:::host
  end

  subgraph LUCIA_CLUSTER["Lucia Platform Cluster"]
    appHost["lucia.AppHost (.NET Aspire)"]:::cluster
    luciaApi["Lucia API Service (HTTPS)"]:::cluster
    agentHost["Lucia Agent Host Worker"]:::cluster
  end

  subgraph OBSERVABILITY["Observability"]
    otelCollector["OpenTelemetry Collector"]:::observ
    logsStack["Grafana / Loki Stack"]:::observ
  end

  subgraph DATA_SERVICES["Optional Data Services"]
    postgres["PostgreSQL 17+"]:::infra
    redis["Redis Cache"]:::infra
    class postgres,redis optional
  end

  subgraph AI_PROVIDERS["External AI Providers"]
    openai["OpenAI GPT-4o"]:::ai
    gemini["Google Gemini"]:::ai
    claude["Anthropic Claude"]:::ai
    class openai,gemini,claude optional
  end

  haCore --> luciaComponent
  luciaComponent --> luciaApi
  appHost --> luciaApi
  appHost --> agentHost
  luciaApi --> agentHost
  luciaApi --> otelCollector
  agentHost --> otelCollector
  otelCollector --> logsStack
  luciaApi -.-> postgres
  agentHost -.-> redis
  agentHost -.-> openai
  agentHost -.-> gemini
  agentHost -.-> claude
```

## Notes

- Dashed connections (`..`) indicate optional or planned integrations.
- All diagrams use accessible, high-contrast palettes for readability on dark backgrounds.
- Update these diagrams when new deployment targets or agents are introduced.
