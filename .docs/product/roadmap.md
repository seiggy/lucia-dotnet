# Product Roadmap

> Last Updated: 2026-02-20
> Version: 2.0.0
> Status: Active Expansion

## Phase 0: Already Completed

The following features have been implemented and are working:

- [x] Core project structure with .NET 10 Aspire - Solution architecture with proper separation of concerns `M`
- [x] Agent Registry API - REST endpoints for agent registration and discovery `L`
- [x] A2A Protocol v0.3.0 implementation - JSON-RPC 2.0 agent communication protocol `M`
- [x] LightAgent - Semantic search-enabled light control agent with Home Assistant integration `XL`
- [x] MusicAgent - Music Assistant playback control agent `XL`
- [x] Home Assistant HTTP client - Hand-written strongly-typed API client `L`
- [x] Microsoft Agent Framework integration - Core AI orchestration with OpenAI `M`
- [x] Chat history and context management - Token management with conversation threading `S`
- [x] Home Assistant plugin base - Python custom component structure `M`
- [x] Plugin configuration flow - UI for API endpoint, authentication, and agent selection `M`
- [x] Agent registry validation - Plugin pulls and validates agent list from catalog `S`
- [x] Conversation endpoint - Natural language chat API with JSON-RPC `M`
- [x] Intent processing - Convert natural language to actions via Agent Framework `M`
- [x] Agent selection UI - Dynamic dropdown with agent switching without reload `M`
- [x] Context preservation - Conversation threading maintains context across agent switches `M`
- [x] HACS integration - Easy installation via Home Assistant Community Store `S`
- [x] IntentResponse integration - Proper Home Assistant conversation platform support `S`

## Phase 1: Foundation & Integration (Completed)

**Goal:** Complete core Home Assistant integration and agent registry functionality  
**Success Criteria:** Home Assistant plugin can discover and validate agents via API

### Completed Features

- [x] Home Assistant plugin base - Python custom component structure `M`
- [x] Plugin configuration flow - UI for API endpoint and authentication `M`
- [x] Agent registry validation - Plugin pulls and validates agent list `S`
- [x] Authentication layer - Optional API key support with SSL `M`
- [x] Agent health checks - Monitor agent availability via catalog endpoint `S`
- [x] Error recovery - Graceful handling of agent failures with fallbacks `S`

### Phase 1 Achievements
- ? Full Home Assistant integration working
- ? Dynamic agent discovery and selection
- ? JSON-RPC 2.0 communication established
- ? HACS marketplace integration

## Phase 2: Core Agent Capabilities (Delivered Baseline, Expansion In Progress)

**Goal:** Implement essential home automation agents  
**Success Criteria:** Natural language control of major home systems

### Completed Features

- [x] LightAgent - Light and lighting scene control with semantic search `XL`
- [x] MusicAgent - Music Assistant playback control across Satellite1 endpoints `XL`
- [x] Conversation endpoint - Natural language chat API with A2A protocol `M`
- [x] Intent processing - Convert natural language to actions `M`
- [x] Context awareness - Time, location, and state-based responses `M`
- [x] Multi-agent orchestration - Coordinate between multiple agents `M`
- [x] ClimateAgent - Temperature and HVAC control `L`
- [x] SceneAgent - Activate and manage scenes `M`
- [x] TimerAgent - Create and trigger timers and events `S`

### In Progress Features

- [ ] SecurityAgent - Alarm, locks, and camera management `L`
- [ ] NotificationAgent - Send alerts and notifications `S`
- [ ] Improved WebSocket integration - Real-time event streaming from Home Assistant `L`

### Dependencies

- Agent Framework improvements for task coordination
- Additional Home Assistant service integrations

## Phase 3: Intelligence & Learning (Planned - 3-4 weeks)

**Goal:** Add adaptive and predictive capabilities  
**Success Criteria:** System learns patterns and suggests automations

### Must-Have Features

- [x] Multi-LLM support - OpenAI, Azure OpenAI/Foundry, Gemini, Claude providers available `M`
- [x] Local LLM integration - Ollama/local model support available `L`
- [ ] Pattern recognition - Identify user behavior patterns `XL`
- [ ] Automation suggestions - Recommend automations based on usage `L`
- [x] Persistent runtime storage - Redis + MongoDB for sessions, traces, config, and task records `M`

### Should-Have Features

- [ ] Cost optimization - Route requests to appropriate LLM tier `S`
- [ ] Fallback strategies - Handle LLM failures gracefully `M`
- [ ] Performance monitoring - Track response times and accuracy `S`
- [ ] Agent memory - Long-term context preservation across sessions `M`

### Dependencies

- Multiple LLM API provider integrations
- Agent Framework memory capabilities

## Phase 4: Advanced Features (Future - 4-6 weeks)

**Goal:** Full-speech pipeline
**Success Criteria:** Full-speech pipeline with GPU acceleration support

### Must-Have Features

- [ ] Backup and restore - Agent state persistence `M`
- [ ] Multi-home support - Manage multiple Home Assistant instances `L`
- [ ] Voice integration - Local speech-to-text and text-to-speech `L`
- [ ] Diarization for full-speech pipeline `XL`
- [ ] Satellite1 direct integration `L`

### Dependencies

- Custom Satellite1 firmware build
