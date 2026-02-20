
# Release Notes - 2026.02.20

**Release Date:** February 20, 2026  
**Code Name:** "Galaxy"

---

## 🌌 Overview

"Galaxy" is the largest release in Lucia's history — a sweeping upgrade that touches nearly every layer of the stack. At its core, this release migrates the entire platform to **Microsoft Agent Framework 1.0.0-preview.260212.1** and **.NET 10**, introduces an **LLM fine-tuning data pipeline** backed by **MongoDB**, adds a brand-new **Timer Agent** for timed announcements, and delivers a comprehensive **evaluation testing framework** for measuring agent quality at scale. With 535 files changed across 86k+ lines, "Galaxy" transforms Lucia from a conversational assistant into a self-improving, observable, and extensible agentic platform.

## 🚀 Highlights

- **Microsoft Agent Framework 1.0.0-preview.260212.1** — Full migration to the latest MAF preview with breaking API changes including `AgentThread` → `AgentSession`, `ChatMessageStore` → `ChatHistoryProvider`, and consolidated `AsAIAgent()` creation. Session management is now fully async, and source-generated executors replace reflection-based patterns.
- **LLM Fine-Tuning Data Pipeline** — A production-grade training system that automatically captures orchestrator and agent conversation traces, stores them in MongoDB, and exports them as OpenAI-compatible JSONL datasets. Includes sensitive data redaction, labeling workflows, configurable retention, and per-agent filtering.
- **MongoDB Integration** — Dual-purpose MongoDB backend for trace/training data storage (`luciatraces`) and hot-reloadable application configuration (`luciaconfig`). Configuration changes poll every 5 seconds and override appsettings.json without restarts.
- **Timer Agent** — New agent for creating timed announcements and reminders on Home Assistant assist satellite devices. Supports natural-language duration parsing, concurrent timer management, and TTS announcements via `assist_satellite.announce`.
- **Evaluation Testing Framework** — Comprehensive eval tests using `Microsoft.Extensions.AI.Evaluation` with LLM-based evaluators (Relevance, Coherence, ToolCallAccuracy, TaskAdherence, Latency). Cross-products models × prompt variants with disk-based reporting via `dotnet aieval report`.

## ✨ What's New

### 🤖 Timer Agent & Skill

- **SetTimer**: Create timers with natural-language durations ("5 minutes", "1 hour 30 minutes") targeting specific satellite devices
- **CancelTimer**: Cancel active timers by ID with graceful cleanup
- **ListTimers**: View all active timers with remaining time and messages
- Thread-safe concurrent timer tracking via `ConcurrentDictionary`
- Fire-and-forget execution model with `CancellationTokenSource` support
- OpenTelemetry instrumentation for timer operations
- Testable time handling via `TimeProvider`

### 🧠 LLM Fine-Tuning Pipeline

- **TraceCaptureObserver**: Hooks into orchestrator lifecycle to capture full interaction traces without impacting latency (fire-and-forget)
- **AgentTracingChatClient**: Wrapping `DelegatingChatClient` that captures system prompts, user messages, tool calls, tool results, and assistant responses per-agent
- **AsyncLocal session correlation**: Links orchestrator and per-agent traces together across async boundaries
- **JsonlConverter**: Exports traces to OpenAI fine-tuning JSONL format with human correction labels, per-agent filtering, and tool call inclusion
- **TraceRetentionService**: Auto-cleans unlabeled traces after configurable retention period (default 30 days)
- **Sensitive data redaction**: Configurable regex patterns strip API keys, tokens, and JWTs before persistence

### 🗄️ MongoDB Backend

- **Trace Storage** (`MongoTraceRepository`): CRUD operations with automatic indexing on Timestamp, Label.Status, and AgentId fields
- **Configuration Storage** (`MongoConfigurationProvider`): ASP.NET Core `IConfigurationProvider` backed by MongoDB with 5-second polling for hot-reload
- **ConfigSeeder**: Bootstraps MongoDB config from appsettings.json on first run
- **Stats aggregation**: Total, labeled, unlabeled, and errored trace counts by agent
- **Filtering**: Query traces by date range, agent, model, label status, and keyword

### 📊 Evaluation Testing

- **EvalTestFixture** & **AgentEvalTestBase**: Reusable infrastructure for agent quality testing
- **Real agent instances** backed by Azure OpenAI evaluation models with `FunctionInvokingChatClient` for actual tool execution
- **LLM-based evaluators**: Relevance, Coherence, ToolCallAccuracy, TaskAdherence, and Latency scoring
- **Cross-product parameterization**: `[MemberData]` drives model × prompt variant test matrices
- **Eval test suites**: LightAgentEvalTests, MusicAgentEvalTests, OrchestratorEvalTests with STT artifact variants and edge cases
- **ChatHistoryCapture** for recording intermediate tool calls and responses

## 🔧 Under the Hood

### MAF Migration (Breaking Changes)

| Before | After |
|--------|-------|
| `AgentThread` | `AgentSession` |
| `ChatMessageStore` | `ChatHistoryProvider` |
| `GetNewThread()` | `await CreateSessionAsync()` |
| `DeserializeThread()` | `await DeserializeSessionAsync()` |
| `session.Serialize()` | `agent.SerializeSession()` |
| `AgentRunResponse` | `AgentResponse` |
| `CreateAIAgent()` / `GetAIAgent()` | `AsAIAgent()` |
| `ReflectingExecutor` | Source-generated `[AgentTools]` |

- `DelegatingAIAgent` is now abstract — implementations must override core methods
- `AIAgent.Id` is now non-nullable (required)
- Provider signatures changed: `ChatHistoryProvider` and `AIContextProvider` now receive `(AIAgent agent, AgentSession session)`
- Custom `IAgentRegistry` replaces removed `AgentCatalog` for agent discovery

### Dependency Upgrades

- **Microsoft.Agents.AI** packages → **1.0.0-preview.260212.1**
- **Microsoft.Extensions.AI** → **10.2.0 / 10.3.0**
- Target framework → **.NET 10**

### DI Registration Fix

- Fixed `AddKeyedAzureOpenAIClient` poisoning the non-keyed `AzureOpenAIClient` registration with a null factory
- Keyed chat clients now reuse the shared non-keyed `OpenAIClient` to prevent double-registration conflicts

### APM & Observability

- **Custom meters**: `Lucia.TraceCapture`, `Lucia.Skills.LightControl`, `Lucia.Skills.MusicPlayback`
- **Activity sources**: Full tracing for Lucia orchestration, agents, skills, and all MAF/A2A namespaces
- **Health check filtering**: Excludes `/health` and `/alive` from trace recording
- **HTTP enrichment**: Request/response headers and bodies captured in OpenTelemetry spans

### Agent Registration & Discovery

- Enhanced agent registration with `IAgentRegistry` interface and `LocalAgentRegistry`
- Agent Cards (A2A protocol) now required for registration with full capability metadata
- `OrchestratorServiceKeys` manages agent-specific model keys for bulk registration
- Async initialization support added to MusicAgent

### Housekeeping

- Removed obsolete multi-agent orchestration specification files
- Refactored `ContextExtractorTests` to use async mock registry creation
- Removed unused test doubles and updated package references

### Home Assistant Custom Component

- **Migrated to `_async_handle_message`**: Replaced deprecated `async_process` method with the new `_async_handle_message` API, adopting Home Assistant's built-in `ChatLog` for multi-turn conversation support
- **Chat log integration**: All agent responses (success and error) are now recorded in the HA `ChatLog` via `async_add_assistant_content_without_tools` with `AssistantContent`
- **`continue_conversation` support**: `ConversationResult` now includes the `continue_conversation` flag for future multi-turn flows
- Bumped component version to `2026.02.20`

## ✅ Upgrade Notes

- **Breaking**: This release requires **.NET 10** — upgrade your SDK before building.
- **Breaking**: MAF API changes require updating all agent session management code (see migration table above).
- **MongoDB required**: New trace capture and configuration features require MongoDB. Add MongoDB to your Aspire AppHost or provide connection strings for `luciatraces` and `luciaconfig` databases.
- **New environment variables**: `HA_ENDPOINT` and `HA_TOKEN` provide Home Assistant API access for testing and snapshot export.
- Existing installations should reload the integration after updating to register the new Timer Agent card.
- Eval tests require Azure OpenAI credentials configured for evaluation model access.
- **Home Assistant plugin**: The conversation entity now uses `_async_handle_message` instead of `async_process`. This is backwards-compatible per the HA team, but requires a recent Home Assistant Core version with `ChatLog` support.

## 🔮 What's Next

See our [Roadmap](https://github.com/seiggy/lucia-dotnet/blob/master/.docs/product/roadmap.md) for upcoming features:

- **Climate Agent** — HVAC and temperature control
- **Security Agent** — Alarms, locks, and camera integration
- **Scene Agent** — Scene management and automation
- **Training UI** — Web interface for labeling conversation traces and managing fine-tuning datasets
- **Local LLM fine-tuning** — Use captured training data with local models for privacy-first deployment
- **WebSocket streaming** — Real-time Home Assistant event monitoring

---

# Release Notes - 2025.11.09

**Release Date:** November 9, 2025  
**Code Name:** "Constellation"

---

## 🌌 Overview

"Constellation" delivers the feature we've been building toward all year: multi-agent orchestration working end-to-end inside Lucia. Requests can now fan out to the most relevant specialists, combine their output, and respond with a natural narrative backed by contextual awareness. Alongside the orchestration milestone, we introduce a new general-knowledge agent that fills in the gaps when a domain specialist is unavailable, plus targeted refinements to the lighting and music skills that make everyday interactions smoother.

## 🚀 Highlights

- **Multi-Agent Orchestration (GA)** — Router, dispatch, and aggregator executors now coordinate multiple agents in a single workflow, with task persistence and telemetry baked in. Complex requests like "Dim the kitchen lights and play relaxing jazz" are handled as one coherent conversation.
- **General Knowledge Agent** — A new catalog entry that handles open-ended queries, status questions, and conversation handoffs when no specialist is a clean match. It plugs directly into the orchestrator so fallbacks feel intentional instead of abrupt.
- **Smarter Light Selection** — Improved semantic matching, room disambiguation, and capability detection make it far easier to target the right fixture on the first try—even when users describe locations conversationally.
- **Music Skill Enhancements** — Faster player discovery, richer queue summaries, and better error messaging tighten the loop between Music Assistant and Lucia’s orchestration pipeline.

## 🔧 Under the Hood

- Expanded orchestration telemetry with detailed WorkflowErrorEvent parsing and OpenTelemetry spans for traceability.
- Options flow updated to align with Home Assistant 2025.12 requirements (no more manual `self.config_entry`).
- HTTP client instrumentation now captures request/response headers and payloads when traces are recorded, aiding diagnostics of A2A traffic.

## ✅ Upgrade Notes

- No breaking schema changes, but existing installations should reload the integration after updating to register the new general agent card.
- Home Assistant users will no longer see the 2025.12 config-flow deprecation warning.

---

# Release Notes - v2025.10.07

**Release Date:** October 7, 2025  
**Code Name:** "Illumination"

---

## 🌟 Overview

This release represents a major milestone for Lucia, bringing the autonomous home assistant from concept to working reality. Named after the Nordic sun goddess who brings light through darkness, this release illuminates your smart home with AI-powered agent automation.

> Correction (2026-02-20): references in this historical section to `lucia.HomeAssistant.SourceGenerator` reflect the 2025.10 state. Current runtime uses the hand-written `lucia.HomeAssistant/Services/HomeAssistantClient.cs` implementation.

## ✨ What's New

### 🏠 Home Assistant Integration (Complete)

#### Custom Component
- **Full Integration**: Native Home Assistant custom component with conversation platform
- **HACS Support**: Easy installation via Home Assistant Community Store
  - Add as custom repository: `https://github.com/seiggy/lucia-dotnet`
  - One-click download and installation
  - Automatic updates through HACS
- **Agent Selection UI**: Dynamic dropdown to choose between specialized agents
  - Automatically discovers available agents from catalog
  - Live agent switching without integration reload
  - Descriptive agent names with capability information
- **Configuration Flow**: Complete setup wizard with validation
  - Repository URL configuration
  - API key authentication (optional)
  - System prompt customization with Home Assistant template syntax
  - Max token configuration (10-4000 tokens)
  
#### Conversation Platform
- **Natural Language Processing**: Full integration with Home Assistant's conversation system
- **Intent Response**: Proper `IntentResponse` implementation for speech output
- **Context Threading**: Conversation continuity using `contextId` for multi-turn dialogues
- **Error Handling**: Graceful error messages with proper fallback behavior

#### Communication Protocol
- **JSON-RPC 2.0**: Standardized communication with agents
- **A2A Protocol v0.3.0**: Agent-to-Agent protocol implementation
- **TaskId Management**: Correct handling of `taskId: null` (required by Agent Framework)
- **Message Threading**: UUID-based message and context tracking
- **HTTP Client**: Async HTTP communication using `httpx` library

### 🤖 Agent System

#### Agent Framework (Microsoft Public Preview)
- **LightAgent**: Fully functional light and switch control
  - Semantic search for finding lights by natural language
  - Device capability detection (brightness, color temp, color modes)
  - State queries and control operations
  - Switch entity support (light switches)
  - Embedding-based similarity matching
  
- **MusicAgent**: Music Assistant integration
  - Playback control (play, pause, stop, skip)
  - Volume management
  - Queue management
  - Player discovery and selection
  - Music Assistant API integration

#### Agent Registry
- **Dynamic Discovery**: Agents register and expose capabilities
- **Catalog Endpoint**: `/agents` returns all available agents with metadata
- **Agent Cards**: Complete agent information including:
  - Name, description, version
  - Supported skills and capabilities
  - Protocol version
  - Example queries
  - Input/output modes

#### Skills System
- **LightControlSkill**: Comprehensive light control
  - `find_light`: Natural language light discovery using embeddings
  - `get_light_state`: Query current light status
  - `set_light_state`: Control on/off, brightness, color
  - Entity caching with 30-minute refresh
  - Cosine similarity matching for semantic search
  
- **MusicPlaybackSkill**: Music Assistant control
  - Playback operations
  - Volume control
  - Queue management
  - Player management

### 🏗️ Technical Infrastructure

#### .NET 10 RTM
- **Latest Framework**: Running on .NET 10 RTM
- **Modern C#**: Using C# 13 features and nullable reference types
- **Performance**: Optimized async/await patterns throughout

#### Agent Framework Integration
- **Microsoft Agent Framework**: Public Preview integration
- **ChatClientAgent**: Modern agent architecture
- **AIFunctionFactory**: Tool creation and registration
- **IChatClient**: LLM provider abstraction
- **IEmbeddingGenerator**: Embedding generation for semantic search

#### .NET Aspire
- **Cloud-Native**: .NET Aspire orchestration for development
- **Service Discovery**: Automatic service registration
- **Health Checks**: Built-in health monitoring
- **OpenTelemetry**: Distributed tracing and metrics

#### Multi-LLM Support
- **OpenAI**: Full GPT-4o and embedding support
- **Azure OpenAI**: Enterprise-grade Azure integration
- **Azure AI Inference**: Azure AI Studio models
- **Ollama**: Local LLM support for privacy-focused deployments
- **Connection Strings**: Standardized configuration format
- **Keyed Clients**: Multiple LLM configurations per application

### 🔧 Developer Experience

#### Project Structure
- **lucia.AgentHost**: Main agent hosting API (ASP.NET Core)
- **lucia.Agents**: Agent implementations and skills
- **lucia.HomeAssistant**: Home Assistant API client library
- **Home Assistant client generation**: Historical at this release point; now replaced by hand-written client implementation
- **lucia.AppHost**: .NET Aspire orchestration
- **lucia.ServiceDefaults**: Shared service configurations
- **lucia.Tests**: Comprehensive test suite
- **custom_components/lucia**: Home Assistant Python integration

#### Code Quality
- **Home Assistant client**: Hand-written, type-safe API implementation
- **Dependency Injection**: Full DI throughout the application
- **Async/Await**: Proper async patterns everywhere
- **Error Handling**: Comprehensive exception handling with logging
- **Logging**: Structured logging with Microsoft.Extensions.Logging

#### Testing Infrastructure
- **Unit Tests**: Agent and skill testing
- **Integration Tests**: End-to-end Home Assistant integration
- **Test Scripts**: Python test utilities for JSON-RPC validation
  - `test_catalog_simple.py`: Agent catalog and messaging tests

## 🎯 What Works

### ✅ Fully Functional Features

1. **Light Control**
   - Find lights using natural language ("living room light", "kitchen ceiling")
   - Turn lights on/off
   - Set brightness (0-100%)
   - Set colors by name
   - Query light status
   - Switch entity support

2. **Music Control**
   - Play/pause/stop playback
   - Volume control
   - Skip tracks
   - Queue management
   - Player selection

3. **Conversation**
   - Natural language input processing
   - Multi-turn conversations with context
   - Speech output via Home Assistant
   - Error handling with user feedback

4. **Agent Management**
   - Agent discovery via catalog
   - Dynamic agent selection
   - Agent switching without reload
   - Health monitoring

5. **Home Assistant Integration**
   - HACS installation support
   - Configuration flow
   - Options management
   - Automatic reload on changes

## 🔨 Technical Details

### API Endpoints

- `GET /agents` - Agent catalog discovery
- `POST /a2a/light-agent` - Light agent JSON-RPC endpoint
- `POST /a2a/music-agent` - Music agent JSON-RPC endpoint
- `GET /health` - Health check endpoint
- `GET /swagger` - API documentation

### Configuration Format

**Agent Connection String:**
```
Endpoint=https://localhost:7235;AccessKey=your-key;Model=gpt-4o;Provider=openai
```

**Supported Providers:**
- `openai` - OpenAI API
- `azureopenai` - Azure OpenAI Service
- `azureaiinference` - Azure AI Inference
- `ollama` - Ollama local models

### JSON-RPC Message Format

**Request:**
```json
{
  "jsonrpc": "2.0",
  "method": "message/send",
  "params": {
    "message": {
      "kind": "message",
      "role": "user",
      "parts": [{"kind": "text", "text": "Turn on the lights"}],
      "messageId": "uuid",
      "contextId": "uuid",
      "taskId": null
    }
  },
  "id": 1
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "result": {
    "kind": "message",
    "role": "assistant",
    "parts": [{"kind": "text", "text": "I've turned on the lights."}],
    "messageId": "uuid",
    "contextId": "uuid",
    "taskId": null
  },
  "id": 1
}
```

## 🐛 Bug Fixes

### Critical Fixes

1. **ConversationResponse Error** (Fixed)
   - **Issue**: Using non-existent `conversation.ConversationResponse` class
   - **Fix**: Replaced with proper `intent.IntentResponse` implementation
   - **Impact**: Home Assistant integration now works correctly

2. **TaskId Handling** (Fixed)
   - **Issue**: Agent Framework doesn't support task management
   - **Fix**: Set `taskId: null` in all JSON-RPC requests
   - **Impact**: Agents now respond successfully to messages

3. **Agent Selection Persistence** (Fixed)
   - **Issue**: Agent selection not persisting across restarts
   - **Fix**: Proper storage in config entry options
   - **Impact**: Selected agent maintained after reload

4. **SSL Certificate Issues** (Addressed)
   - **Issue**: Self-signed certificates causing connection failures
   - **Fix**: Added `verify=False` for development environments
   - **Impact**: Local development now works smoothly

## 📊 Performance

- **Agent Response Time**: < 2 seconds for typical requests
- **Light Discovery**: Cached with 30-minute refresh interval
- **Embedding Generation**: Optimized for real-time semantic search
- **Memory Usage**: Efficient entity caching
- **HTTP Communication**: Async throughout for non-blocking operations

## 📚 Documentation

### Updated Documentation

- **README.md**: Comprehensive project overview with pronunciation guide
- **AGENT_SELECTION.md**: Detailed agent selection feature documentation
- **Code Examples**: Complete agent and skill implementation examples
- **API Documentation**: Swagger/OpenAPI specifications
- **Installation Guide**: Both HACS and manual installation methods

### About the Name

Lucia is named after the ancient Nordic sun goddess associated with light, wisdom, and bringing illumination during the darkest time of year. Pronounced **LOO-sha** (or **LOO-thee-ah** in traditional Nordic), the name reflects the project's mission to bring intelligent automation and insight to your home.

## 🔮 What's Next

See our [Roadmap](https://github.com/seiggy/lucia-dotnet/blob/master/.docs/product/roadmap.md) for upcoming features:

### Phase 2: Core Agents (In Progress)
- ClimateAgent (HVAC and temperature)
- SecurityAgent (alarms, locks, cameras)
- SceneAgent (scene management)
- Multi-agent orchestration

### Phase 3: Intelligence (Planned)
- Pattern recognition and learning
- Automation suggestions
- Cost optimization for multi-LLM routing
- Local LLM refinements

## 🙏 Acknowledgments

Special thanks to:
- Microsoft Agent Framework team
- Home Assistant community
- Music Assistant project
- All contributors and testers

## 📦 Installation

### HACS (Recommended)

1. Open HACS in Home Assistant
2. Go to Integrations
3. Click three dots → Custom repositories
4. Add: `https://github.com/seiggy/lucia-dotnet`
5. Category: Integration
6. Find "Lucia" and download
7. Restart Home Assistant
8. Add integration via Settings → Devices & Services

### Manual Installation

```bash
# Clone repository
git clone https://github.com/seiggy/lucia-dotnet.git
cd lucia-dotnet

# Install .NET 10
# Download from https://dotnet.microsoft.com/

# Run the agent host
dotnet run --project lucia.AgentHost

# Copy Home Assistant integration
cp -r custom_components/lucia /path/to/homeassistant/custom_components/

# Restart Home Assistant
```

## 🔗 Resources

- **Repository**: https://github.com/seiggy/lucia-dotnet
- **Issues**: https://github.com/seiggy/lucia-dotnet/issues
- **Discussions**: https://github.com/seiggy/lucia-dotnet/discussions
- **Documentation**: https://github.com/seiggy/lucia-dotnet/wiki

## 📄 License

MIT License - See [LICENSE](LICENSE) file for details.

---

**Built with ❤️ for the Home Assistant community**

*Bringing light to home automation, one agent at a time.* ☀️
