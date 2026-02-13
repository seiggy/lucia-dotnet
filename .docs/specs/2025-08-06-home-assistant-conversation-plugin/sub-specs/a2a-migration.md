# A2A Package Migration Specification

This document details the migration from custom A2A implementation to the official A2A NuGet package for the spec detailed in @.agent-os/specs/2025-08-06-home-assistant-conversation-plugin/spec.md

> Created: 2025-08-06
> Version: 1.0.0

## Migration Overview

Replace the custom A2A protocol implementation in `lucia.Agents` with the official A2A NuGet package version `0.1.0-preview.2`. This ensures standards compliance and reduces maintenance burden.

## Files to Remove

### Custom A2A Implementation
```
lucia.Agents/A2A/
├── AgentCapabilities.cs
├── AgentCard.cs
├── AgentExtension.cs
├── AgentInterface.cs
├── AgentProvider.cs
├── AgentSkill.cs
├── SecurityScheme.cs
└── Services/
    ├── A2AService.cs
    └── IA2AService.cs
```

## Package Reference Changes

### lucia.Agents.csproj
**Remove:**
- Custom A2A classes and interfaces

**Add:**
```xml
<PackageReference Include="A2A" Version="0.1.0-preview.2" />
```

## Code Changes Required

### AgentRegistryApi.cs
**Before:**
```csharp
using lucia.Agents.A2A;
using lucia.Agents.A2A.Services;

// Custom AgentCard usage
public IActionResult GetAgents()
{
    var cards = _registry.GetAllAgents()
        .Select(agent => new AgentCard
        {
            Id = agent.Id,
            Name = agent.Name,
            // ... custom implementation
        });
}
```

**After:**
```csharp
using A2A.Protocol;
using A2A.Models;

// Official A2A package usage
public IActionResult GetAgents()
{
    var cards = _registry.GetAllAgents()
        .Select(agent => new AgentCard
        {
            ProtocolVersion = "0.3.0",
            Name = agent.Name,
            Url = agent.Endpoint,
            // ... official implementation
        });
}
```

### Agent Base Classes
**Replace custom interfaces with official package interfaces:**
```csharp
// Before
using lucia.Agents.A2A;

// After  
using A2A.Agents;
using A2A.Protocol;
```

### Service Registration
**Update service registration in Extensions/ServiceCollectionExtensions.cs:**
```csharp
// Before
services.AddScoped<IA2AService, A2AService>();

// After
services.AddA2AProtocol(options => {
    options.ProtocolVersion = "0.3.0";
    options.EnableSignatures = false; // Optional for local deployment
});
```

## API Endpoint Changes

### Agent Registry Response Format
Update to match official A2A AgentCard schema:

**New Response Structure:**
```json
{
  "protocolVersion": "0.3.0",
  "name": "Lucia Orchestrator",
  "description": "Main orchestration agent for routing requests",
  "url": "https://lucia-api:5211/api/agents/orchestrator-agent/v1",
  "preferredTransport": "JSONRPC",
  "additionalInterfaces": [
    {
      "url": "https://lucia-api:5211/api/agents/orchestrator-agent/v1",
      "transport": "JSONRPC"
    }
  ],
  "capabilities": {
    "streaming": true,
    "pushNotifications": false,
    "stateTransitionHistory": true
  },
  "defaultInputModes": ["application/json", "text/plain"],
  "defaultOutputModes": ["application/json"],
  "skills": [
    {
      "id": "route-request",
      "name": "AI Agentic Request Router",
      "description": "Routes requests to appropriate agents based on capabilities"
    }
  ],
  "version": "1.0.0"
}
```

### Conversation Endpoints
Implement JSON-RPC 2.0 endpoints as per A2A specification:

**New Endpoint:** `POST /api/agents/{agent_id}/v1`
**Protocol:** JSON-RPC 2.0
**Methods:**
- `message/send` - Send conversation message
- `task/get` - Retrieve task status
- `task/cancel` - Cancel running task

## Migration Strategy

### Phase 1: Package Installation
1. Add A2A NuGet package reference
2. Remove custom A2A project references
3. Update using statements throughout codebase

### Phase 2: Model Migration
1. Replace custom AgentCard with official A2A AgentCard
2. Update agent capabilities mapping
3. Migrate skill definitions to official format

### Phase 3: Service Implementation
1. Replace IA2AService with official A2A interfaces
2. Implement JSON-RPC 2.0 endpoints
3. Update conversation message formatting

### Phase 4: Testing & Validation
1. Test agent registry responses match A2A schema
2. Validate JSON-RPC conversation endpoints
3. Ensure backward compatibility during transition

## Breaking Changes

### API Response Changes
- Agent registry responses will use new A2A schema
- Conversation endpoints move to JSON-RPC 2.0 format
- Agent capability structure changes

### Internal Architecture Changes
- Remove custom A2A service interfaces
- Update dependency injection registration
- Change agent base class inheritance

## Backward Compatibility

### Transition Period
- Maintain both old and new endpoints temporarily
- Add API versioning to support gradual migration
- Document migration path for existing clients

### Deprecation Timeline
- Immediate: Add official A2A package
- Week 1: Update internal implementations
- Week 2: Deprecate custom A2A endpoints
- Week 4: Remove custom A2A implementation

## Testing Updates

### Unit Tests
Update tests to use official A2A models:
```csharp
// Before
var agentCard = new lucia.Agents.A2A.AgentCard { ... };

// After
var agentCard = new A2A.Models.AgentCard { ... };
```

### Integration Tests
- Test JSON-RPC 2.0 endpoint compliance
- Validate A2A schema adherence
- Test Home Assistant plugin compatibility

## Documentation Updates

- Update API documentation with new A2A schemas
- Document JSON-RPC 2.0 conversation endpoints
- Update README with A2A package information
- Create migration guide for developers