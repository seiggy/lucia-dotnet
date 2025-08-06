# API Specification

This is the API specification for the spec detailed in @.agent-os/specs/2025-08-06-home-assistant-conversation-plugin/spec.md

> Created: 2025-08-06
> Version: 1.0.0

## Lucia API Endpoints (Consumed by Plugin)

### GET /api/agents

**Purpose:** Retrieve list of available agents from the registry
**Headers:** 
- `X-Api-Key: {api_key}`
- `Accept: application/json`

**Response:** 200 OK
```json
[
  {
    "id": "orchestrator-agent",
    "name": "Lucia Orchestrator",
    "description": "Main orchestration agent for routing requests",
    "capabilities": {
      "supportedLanguages": ["en", "es", "fr"],
      "domains": ["lights", "climate", "security", "general"],
      "features": ["multi-turn", "context-aware", "tool-calling"]
    },
    "endpoint": "http://lucia-api:5211/api/agents/orchestrator-agent",
    "status": "online"
  }
]
```

**Errors:**
- 401 Unauthorized - Invalid API key
- 500 Internal Server Error - Registry unavailable

### POST /api/agents/{agent_id}/conversation

**Purpose:** Send conversation request to specific agent
**Headers:**
- `Authorization: Bearer {api_key}`
- `Content-Type: application/json`

**Request Body:**
```json
{
  "text": "turn on the living room lights",
  "conversation_id": "ha_conv_12345",
  "language": "en",
  "context": {
    "user_id": "home_assistant_user",
    "timestamp": "2025-08-06T10:30:00Z",
    "source": "conversation"
  }
}
```

**Response:** 200 OK
```json
{
  "response": {
    "text": "I've turned on the living room lights for you.",
    "conversation_id": "ha_conv_12345",
    "continue_conversation": false,
    "actions_taken": [
      {
        "type": "service_call",
        "domain": "light",
        "service": "turn_on",
        "entity_id": "light.living_room"
      }
    ]
  },
  "metadata": {
    "processing_time_ms": 250,
    "agent_id": "orchestrator-agent",
    "model_used": "gpt-4o"
  }
}
```

**Errors:**
- 400 Bad Request - Invalid request format
- 401 Unauthorized - Invalid API key
- 404 Not Found - Agent not found
- 429 Too Many Requests - Rate limit exceeded
- 500 Internal Server Error - Processing error
- 503 Service Unavailable - Agent offline

## Home Assistant Internal APIs (Used by Plugin)

### Config Entry Storage

**Purpose:** Store integration configuration
**Data Structure:**
```python
{
    "api_url": "http://lucia-api:5211",
    "api_key": "secret_key_here",
    "agent_id": "orchestrator-agent",
    "agent_name": "Lucia Orchestrator"
}
```

### Persistent Notification API

**Purpose:** Display error notifications to user
**Method:** `persistent_notification.async_create`
**Parameters:**
```python
{
    "title": "Lucia Integration Error",
    "message": "Failed to connect to Lucia API. Please check your configuration.",
    "notification_id": "lucia_connection_error"
}
```

### Conversation Entity Methods

**async_process()** - Main conversation processing
- Input: `ConversationInput` with text, conversation_id, language
- Output: `ConversationResult` with response text and metadata

**async_prepare()** - Pre-load resources (optional)
- Input: language code
- Purpose: Pre-fetch agent capabilities or warm up connection

## A2A Protocol Specification

### Agent Card Structure
```json
{
  "id": "string",
  "name": "string",
  "description": "string",
  "version": "1.0.0",
  "author": "string",
  "capabilities": {
    "actions": ["list", "of", "supported", "actions"],
    "interfaces": ["conversation", "tool-calling"],
    "extensions": {}
  },
  "endpoints": {
    "conversation": "/conversation",
    "status": "/status"
  },
  "authentication": {
    "type": "bearer",
    "required": true
  }
}
```

### Conversation Request (A2A)
```json
{
  "id": "unique-request-id",
  "timestamp": "ISO-8601",
  "source": {
    "agent_id": "home-assistant-plugin",
    "type": "integration"
  },
  "conversation": {
    "text": "user input",
    "conversation_id": "session-id",
    "language": "en",
    "context": {}
  }
}
```

### Conversation Response (A2A)
```json
{
  "id": "unique-response-id",
  "request_id": "unique-request-id",
  "timestamp": "ISO-8601",
  "agent_id": "responding-agent",
  "response": {
    "text": "agent response",
    "continue_conversation": false,
    "metadata": {}
  },
  "status": "success",
  "errors": []
}
```

## Rate Limiting

- **Agent Registry:** 10 requests per minute
- **Conversation Endpoint:** 60 requests per minute per agent
- **Headers:** `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset`

## Authentication Flow

1. User provides API key in config flow
2. Plugin validates key by calling GET /api/agents
3. If successful, store encrypted in config entry
4. Include as Bearer token in all subsequent requests
5. Handle 401 responses by triggering reconfiguration flow