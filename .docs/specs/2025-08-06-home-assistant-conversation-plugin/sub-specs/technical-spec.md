# Technical Specification

This is the technical specification for the spec detailed in @.agent-os/specs/2025-08-06-home-assistant-conversation-plugin/spec.md

> Created: 2025-08-06
> 
> Version: 1.0.0

## Technical Requirements

### Integration Structure
- Follow Home Assistant's custom component structure with proper `manifest.json`, `__init__.py`, `config_flow.py`, and `strings.json`
- Implement as a conversation integration using `ConversationEntity` base class
- Support Home Assistant 2024.1.0 or later (for current conversation API)
- Use async/await patterns throughout for non-blocking operations

### Configuration Flow
- Implement `ConfigFlow` class extending `config_entries.ConfigFlow`
- Support user-initiated setup via UI (`async_step_user`)
- Validate API connectivity before creating config entry
- Store configuration in config entry data: API URL, API key, selected agent ID
- Support reconfiguration flow for updating credentials or agent selection

### Conversation Processing
- Implement `_async_handle_message` to process conversation input
- Extract text, conversation_id, language from `ConversationInput`
- Forward requests to selected Lucia agent via A2A protocol
- Handle multi-turn conversations using conversation_id tracking
- Return properly formatted `ConversationResult` with speech response

### A2A Protocol Implementation
- Create async HTTP client using `aiohttp` for A2A communication
- Implement agent registry endpoint calls (`GET /api/agents`)
- Support A2A conversation endpoint for agent communication
- Parse `AgentCard` responses with capabilities and metadata
- Handle request/response according to A2A specification

### Error Handling
- Catch connection errors and display user-friendly messages
- Use Home Assistant's persistent notification system for critical errors
- Implement exponential backoff for retries on transient failures
- Log detailed errors for debugging while keeping user messages simple
- Validate agent availability before processing requests

## Approach Options

### Option A: Direct HTTP Client
- Pros: Simple implementation, full control over requests, easy debugging
- Cons: More code to maintain, need to handle all edge cases

### Option B: Generated Client from OpenAPI (Selected)
- Pros: Type-safe client, automatic validation, less boilerplate
- Cons: Requires OpenAPI spec maintenance, additional build step

**Rationale:** Selected Option A initially for faster development and flexibility. Can migrate to Option B once A2A spec stabilizes.

## External Dependencies

### .NET Packages (Backend Migration)
- **A2A** (0.1.0-preview.2) - Official A2A protocol implementation
  - **Justification:** Replace custom A2A implementation with official package for better maintainability and standards compliance
  - **Impact:** Remove custom A2A classes from lucia.Agents project

### Python Packages
- **aiohttp** (>=3.9.0) - Async HTTP client for API communication
  - **Justification:** Home Assistant's standard async HTTP client
- **voluptuous** (>=0.13.1) - Data validation for config flow
  - **Justification:** Home Assistant's standard validation library

### Home Assistant Components
- **homeassistant.config_entries** - Configuration flow base classes
- **homeassistant.components.conversation** - Conversation entity base
- **homeassistant.helpers.aiohttp_client** - Managed HTTP session
- **homeassistant.components.persistent_notification** - User notifications

## File Structure

```
custom_components/lucia/
├── __init__.py          # Integration setup and entry point
├── manifest.json        # Integration metadata and dependencies
├── config_flow.py       # Configuration UI flow
├── conversation.py      # ConversationEntity implementation
├── a2a_client.py       # A2A protocol client
├── const.py            # Constants and configuration keys
├── strings.json        # UI strings for translations
└── translations/
    └── en.json         # English translations
```

## Configuration Schema

```python
CONFIG_SCHEMA = {
    "api_url": str,      # Lucia API endpoint URL
    "api_key": str,      # API authentication key
    "agent_id": str,     # Selected orchestration agent ID
    "timeout": int,      # Request timeout (default: 30)
}
```

## Security Considerations

- Store API keys securely in Home Assistant's config entry system
- Validate SSL certificates for HTTPS connections (with option to bypass for local deployments)
- Sanitize user input before sending to Lucia API
- Never log sensitive information like API keys
- Use Home Assistant's built-in authentication for the integration itself

# A2A Compliance (from A2A v0.3.0 spec)
For an agent to be considered A2A-compliant, it MUST:

### 11.1.1. Transport Support Requirements
- Support at least one transport: Agents MUST implement at least one transport protocols as defined in Section 3.2.
- Expose Agent Card: MUST provide a valid AgentCard document as defined in Section 5.
- Declare transport capabilities: MUST accurately declare all supported transports in the AgentCard using preferredTransport and additionalInterfaces fields following the requirements in Section 5.6.

### 11.1.2. Core Method Implementation
MUST implement all of the following core methods via at least one supported transport:

* `message/send` - Send messages and initiate tasks
* `tasks/get` - Retrieve task status and results
* `tasks/cancel` - Request task cancellation

### 11.1.3. Optional Method Implementation
MAY implement the following optional methods:

- `message/stream` - Streaming message interaction (requires capabilities.streaming: true)
- `tasks/resubscribe` - Resume streaming for existing tasks (requires capabilities.streaming: true)
- `tasks/pushNotificationConfig/set` - Configure push notifications (requires capabilities.pushNotifications: true)
- `tasks/pushNotificationConfig/get` - Retrieve push notification config (requires capabilities.pushNotifications: true)
- `tasks/pushNotificationConfig/list` - List push notification configs (requires capabilities.pushNotifications: true)
- `tasks/pushNotificationConfig/delete` - Delete push notification config (requires capabilities.pushNotifications: true)
- `agent/authenticatedExtendedCard` - Retrieve authenticated agent card (requires supportsAuthenticatedExtendedCard: true)

### 11.1.4. Multi-Transport Compliance
If an agent supports additional transports (gRPC, HTTP+JSON), it MUST:

- **Functional equivalence:** Provide identical functionality across all supported transports.
- **Consistent behavior:** Return semantically equivalent results for the same operations.
- **Transport-specific requirements:** Conform to all requirements defined in Section 3.2 for each supported transport.
- **Method mapping compliance:** Use the standard method mappings defined in Section 3.5 for all supported transports.

### 11.1.5. Data Format Compliance¶
- **JSON-RPC structure:** MUST use valid JSON-RPC 2.0 request/response objects as defined in Section 6.11.
- **A2A data objects:** MUST use the data structures defined in Section 6 for all protocol entities.
- **Error handling:** MUST use the error codes defined in Section 8.