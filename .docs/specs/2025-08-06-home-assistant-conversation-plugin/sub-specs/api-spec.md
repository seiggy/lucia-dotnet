# API Specification

This is the API specification for the spec detailed in @.agent-os/specs/2025-08-06-home-assistant-conversation-plugin/spec.md

> Created: 2025-08-06
> Version: 1.0.0

## Needed Endpoints
- **Agent Registry:** JSON REST API for Agent Discovery, registration, and management
- **Conversation Endpoint:** A2A protocol for sending and receiving conversation messages
    - Need to migrate from custom A2A implementation to the new related A2A nuget package: A2A --version 0.1.0-preview.2

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
    "protocolVersion": "0.2.9",
    "name": "Lucia Orchestrator",
    "description": "Main orchestration agent for routing requests",
    "uri": "https://lucia-api:5211/api/agents/orchestrator-agent/.well-known/agent-card.json",
    "preferredTransport": "JSONRPC",
    "additionalInterfaces": [
      {
        "url": "https://lucia-api:5211/api/agents/orchestrator-agent/v1",
        "transport": "JSONRPC"
      },
      {
        "url": "https://lucia-api:5211/api/agents/orchestrator-agent/grpc",
        "transport": "GRPC"
      },
      {
        "url": "https://lucia-api:5211/api/agents/orchestrator-agent/json",
        "transport": "HTTP+JSON"
      }
    ],
    "iconUrl": "https://lucia-api:5211/api/agents/orchestrator-agent/icon",
    "provider": {
      "organization": "Lucia AI",
      "url": "https://github.com/seiggy/lucia-dotnet"
    },
    "version": "1.0.0",
    "documentationUrl": "https://lucia-api:5211/api/agents/orchestrator-agent/docs",
    "capabilities": {
      "streaming": true,
      "pushNotifications": false,
      "stateTransitionHistory": true,
      "extensions": []
    },
    "securitySchemes": [],
    "defaultInputModes": ["application/json", "text/plain"],
    "defaultOutputModes": ["application/json", "image/png"],
    "skills": [
      {
        "id": "route-request",
        "name": "AI Agentic Request Router",
        "description": "Routes requests to appropriate agents based on capabilities",
        "tags": ["ai", "routing", "orchestration"],
        "examples": [
          "How do I turn on the living room lights?",
          "{\"text\": \"The living room lights have been turned on.\", \"conversation_id\": \"ha_conv_12345\"}"
        ],
        "inputModes": ["application/json", "text/plain"],
        "outputModes": ["application/json"]
      }
    ],
    "supportsAuthenticatedExtendedCard": false,
    "signatures": [
      {
        "protected": "eyJhbGciOiJFUzI1NiIsInR5cCI6IkpPU0UiLCJraWQiOiJrZXktMSIsImprdSI6Imh0dHBzOi8vZXhhbXBsZS5jb20vYWdlbnQvandrcy5qc29uIn0",
        "signature": "QFdkNLNszlGj3z3u0YQGt_T9LixY3qtdQpZmsTdDHDe3fXV9y9-B3m2-XgCpzuhiLt8E0tV6HXoZKHv4GtHgKQ"
      }
    ]
  }
]
```

**Errors:**
- 401 Unauthorized - Invalid API key
- 500 Internal Server Error - Registry unavailable

### JSON-RPC /api/agents/{agent_id}/v1

**Purpose:** Send conversation request to specific agent
**Headers:**
- `X-Api-Key: {api_key}`
- `Content-Type: application/json`

**Request Body:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "message/send",
  "params": {
    "message": {
      "role": "user",
      "parts": [
        {
          "kind": "text",
          "text": "Turn on the living room lights"
        }
      ]
    },
    "metadata": {
      "ha_conversation_id": "ha_conv_12345"
    }
  }
}
```

**Response:** 200 OK
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "id": "363422be-b0f9-4692-a24d-278670e7c7f1",
    "contextId": "c295ea44-7543-4f78-b524-7a38915ad6e4",
    "status": {
      "state": "completed"
    },
    "artifacts": [
      {
        "artifactId": "9b6934dd-37e3-4eb1-8766-962efaab63a1",
        "name": "result",
        "parts": [
          {
            "kind": "text",
            "text": "I've turned on the living room lights for you."
          }
        ]
      }
    ],
    "history": [
      {
        "role": "user",
        "parts": [
          {
            "kind": "text",
            "text": "Turn on the living room lights"
          }
        ],
        "messageId": "9229e770-767c-417b-a0b0-f0741243c589",
        "taskId": "363422be-b0f9-4692-a24d-278670e7c7f1",
        "contextId": "c295ea44-7543-4f78-b524-7a38915ad6e4"
      }
    ],
    "kind": "task",
    "metadata": {
      "ha_conversation_id": "ha_conv_12345"
    }
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
```typescript
/**
 * The AgentCard is a self-describing manifest for an agent. It provides essential
 * metadata including the agent's identity, capabilities, skills, supported
 * communication methods, and security requirements.
 */
export interface AgentCard {
    /**
    * The version of the A2A protocol this agent supports.
    * @default "0.3.0"
    */
    protocolVersion: string;
    /**
    * A human-readable name for the agent.
    *
    * @TJS-examples ["Recipe Agent"]
    */
    name: string;
    /**
    * A human-readable description of the agent, assisting users and other agents
    * in understanding its purpose.
    *
    * @TJS-examples ["Agent that helps users with recipes and cooking."]
    */
    description: string;
    /**
    * The preferred endpoint URL for interacting with the agent.
    * This URL MUST support the transport specified by 'preferredTransport'.
    *
    * @TJS-examples ["https://api.example.com/a2a/v1"]
    */
    url: string;
    /**
    * The transport protocol for the preferred endpoint (the main 'url' field).
    * If not specified, defaults to 'JSONRPC'.
    *
    * IMPORTANT: The transport specified here MUST be available at the main 'url'.
    * This creates a binding between the main URL and its supported transport protocol.
    * Clients should prefer this transport and URL combination when both are supported.
    *
    * @default "JSONRPC"
    * @TJS-examples ["JSONRPC", "GRPC", "HTTP+JSON"]
    */
    preferredTransport?: TransportProtocol | string;
    /**
    * A list of additional supported interfaces (transport and URL combinations).
    * This allows agents to expose multiple transports, potentially at different URLs.
    *
    * Best practices:
    * - SHOULD include all supported transports for completeness
    * - SHOULD include an entry matching the main 'url' and 'preferredTransport'
    * - MAY reuse URLs if multiple transports are available at the same endpoint
    * - MUST accurately declare the transport available at each URL
    *
    * Clients can select any interface from this list based on their transport capabilities
    * and preferences. This enables transport negotiation and fallback scenarios.
    */
    additionalInterfaces?: AgentInterface[];
    /** An optional URL to an icon for the agent. */
    iconUrl?: string;
    /** Information about the agent's service provider. */
    provider?: AgentProvider;
    /**
     * The agent's own version number. The format is defined by the provider.
     *
     * @TJS-examples ["1.0.0"]
     */
    version: string;
    /** An optional URL to the agent's documentation. */
    documentationUrl?: string;
    /** A declaration of optional capabilities supported by the agent. */
    capabilities: AgentCapabilities;
    /**
     * A declaration of the security schemes available to authorize requests. The key is the
     * scheme name. Follows the OpenAPI 3.0 Security Scheme Object.
     */
    securitySchemes?: { [scheme: string]: SecurityScheme };
    /**
     * A list of security requirement objects that apply to all agent interactions. Each object
     * lists security schemes that can be used. Follows the OpenAPI 3.0 Security Requirement Object.
     * This list can be seen as an OR of ANDs. Each object in the list describes one possible
     * set of security requirements that must be present on a request. This allows specifying,
     * for example, "callers must either use OAuth OR an API Key AND mTLS."
     *
     * @TJS-examples [[{"oauth": ["read"]}, {"api-key": [], "mtls": []}]]
     */
    security?: { [scheme: string]: string[] }[];
    /**
     * Default set of supported input MIME types for all skills, which can be
     * overridden on a per-skill basis.
     */
    defaultInputModes: string[];
    /**
     * Default set of supported output MIME types for all skills, which can be
     * overridden on a per-skill basis.
     */
    defaultOutputModes: string[];
    /** The set of skills, or distinct capabilities, that the agent can perform. */
    skills: AgentSkill[];
    /**
     * If true, the agent can provide an extended agent card with additional details
     * to authenticated users. Defaults to false.
     */
    supportsAuthenticatedExtendedCard?: boolean;
    /** JSON Web Signatures computed for this AgentCard. */
    signatures?: AgentCardSignature[];
}
```

### Conversation Request (A2A)
```typescript
/**
 * Represents a single message in the conversation between a user and an agent.
 */
export interface Message {
    /** Identifies the sender of the message. `user` for the client, `agent` for the service. */
    readonly role: "user" | "agent";
    /**
    * An array of content parts that form the message body. A message can be
    * composed of multiple parts of different types (e.g., text and files).
    */
    parts: Part[];
    /** Optional metadata for extensions. The key is an extension-specific identifier. */
    metadata?: {
        [key: string]: any;
    };
    /** The URIs of extensions that are relevant to this message. */
    extensions?: string[];
    /** A list of other task IDs that this message references for additional context. */
    referenceTaskIds?: string[];
    /** A unique identifier for the message, typically a UUID, generated by the sender. */
    messageId: string;
    /** The identifier of the task this message is part of. Can be omitted for the first message of a new task. */
    taskId?: string;
    /** The context identifier for this message, used to group related interactions. */
    contextId?: string;
    /** The type of this object, used as a discriminator. Always 'message' for a Message. */
    readonly kind: "message";
}
```

### Conversation Response (A2A)
```typescript
/**
 * Represents a single, stateful operation or conversation between a client and an agent.
 */
export interface Task {
  /** A unique identifier for the task, generated by the server for a new task. */
  id: string;
  /** A server-generated identifier for maintaining context across multiple related tasks or interactions. */
  contextId: string;
  /** The current status of the task, including its state and a descriptive message. */
  status: TaskStatus;
  /** An array of messages exchanged during the task, representing the conversation history. */
  history?: Message[];
    /** A collection of artifacts generated by the agent during the execution of the task. */
    artifacts?: Artifact[];
    /** Optional metadata for extensions. The key is an extension-specific identifier. */
    metadata?: {
        [key: string]: any;
    };
    /** The type of this object, used as a discriminator. Always 'task' for a Task. */
    readonly kind: "task";
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