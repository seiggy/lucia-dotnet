
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
- **lucia.HomeAssistant.SourceGenerator**: Roslyn-based API code generation
- **lucia.AppHost**: .NET Aspire orchestration
- **lucia.ServiceDefaults**: Shared service configurations
- **lucia.Tests**: Comprehensive test suite
- **custom_components/lucia**: Home Assistant Python integration

#### Code Quality
- **Source Generators**: Automatic Home Assistant API client generation
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
- **Documentation**: https://github.com/seiggy/lucia-dotnet/wiki *(coming soon)*

## 📄 License

MIT License - See [LICENSE](LICENSE) file for details.

---

**Built with ❤️ for the Home Assistant community**

*Bringing light to home automation, one agent at a time.* ☀️
