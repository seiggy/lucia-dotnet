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